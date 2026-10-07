using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IFacilityScopeService _facilityScopeService;
    private readonly DemoGateway _demoGateway;
    private readonly VnpayGateway _vnpayGateway;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IFacilityScopeService facilityScopeService,
        DemoGateway demoGateway,
        VnpayGateway vnpayGateway,
        IConfiguration configuration,
        ILogger<PaymentService> logger)
    {
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _facilityScopeService = facilityScopeService ?? throw new ArgumentNullException(nameof(facilityScopeService));
        _demoGateway = demoGateway ?? throw new ArgumentNullException(nameof(demoGateway));
        _vnpayGateway = vnpayGateway ?? throw new ArgumentNullException(nameof(vnpayGateway));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CheckoutQuoteResponse> GetCheckoutQuoteAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long reservationId,
        string? promotionCode,
        CancellationToken cancellationToken = default)
    {
        var reservation = await _paymentRepository.GetReservationForQuoteAsync(reservationId, cancellationToken);
        if (reservation == null)
        {
            throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với ID {reservationId}.");
        }

        await AssertReservationAccessAsync(currentUserId, roles, reservation, cancellationToken);

        var nowUtc = DateTimeOffset.UtcNow;
        var isHoldActive = (reservation.status == "pending" || reservation.status == "awaiting_deposit") &&
                           reservation.hold_until > nowUtc;

        if (!isHoldActive)
        {
            throw new ConflictException("Đơn đặt chỗ đã hết hạn giữ chỗ (15 phút) hoặc không ở trạng thái chờ thanh toán. Vui lòng tạo đơn đặt chỗ mới.");
        }

        // BR-FIN-01: deposit equals 1 month rent
        if (reservation.deposit_snapshot != reservation.monthly_rate_snapshot)
        {
            throw new BadRequestException("Lỗi cấu hình hệ thống: Tiền cọc phải bằng đúng 1 tháng tiền thuê niêm yết (BR-FIN-01). Không thể tiếp tục checkout.");
        }

        var policyVersion = await _paymentRepository.GetActiveRentalTermsPolicyVersionAsync(reservation.start_date, cancellationToken);
        if (policyVersion == null)
        {
            throw new BadRequestException("Không tìm thấy phiên bản điều khoản hợp đồng ('rental_terms') đang có hiệu lực cho ngày bắt đầu thuê.");
        }

        var (discount, promo) = await CalculatePromotionDiscountAsync(
            reservation,
            promotionCode,
            nowUtc,
            cancellationToken);

        var subtotal = reservation.deposit_snapshot + reservation.monthly_rate_snapshot + reservation.booking_fee_snapshot;
        var quotedTotal = subtotal - discount;

        return new CheckoutQuoteResponse
        {
            ReservationId = reservation.id,
            ReservationCode = reservation.reservation_code,
            CustomerId = reservation.customer_id,
            CustomerName = reservation.customer?.full_name ?? "Khách hàng",
            FacilityId = reservation.facility_id,
            FacilityName = reservation.facility?.name ?? string.Empty,
            UnitTypeId = reservation.unit_type_id,
            UnitTypeName = reservation.unit_type?.name ?? string.Empty,
            StartDate = reservation.start_date,
            EndDate = reservation.end_date,
            RentalMonths = CalculateRentalMonths(reservation.start_date, reservation.end_date),
            MonthlyRateSnapshot = reservation.monthly_rate_snapshot,
            DepositSnapshot = reservation.deposit_snapshot,
            BookingFeeSnapshot = reservation.booking_fee_snapshot,
            DiscountAmount = discount,
            SubtotalAmount = subtotal,
            TaxAmount = 0m,
            QuotedTotal = quotedTotal,
            HoldUntil = reservation.hold_until,
            IsHoldActive = isHoldActive,
            PromotionCode = promo?.code,
            PromotionName = promo?.name,
            DiscountType = promo?.discount_type,
            DiscountValue = promo?.discount_value,
            PolicyVersionId = policyVersion.id,
            PolicyVersion = policyVersion.version,
            PolicyContentJson = policyVersion.content
        };
    }

    public async Task<CheckoutResponse> CheckoutAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        CheckoutRequest request,
        string? idempotencyKeyHeader,
        string? clientIp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!roles.Contains(RoleConstants.StorageCustomer))
        {
            throw new ForbiddenException("Chỉ khách hàng (StorageCustomer) mới có quyền tạo yêu cầu thanh toán checkout.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKeyHeader))
        {
            throw new BadRequestException("Header 'Idempotency-Key' là bắt buộc để khởi tạo giao dịch thanh toán.");
        }

        var cleanIdempotencyKey = idempotencyKeyHeader.Trim();
        var nowUtc = DateTimeOffset.UtcNow;

        var reservation = await _paymentRepository.GetReservationForQuoteAsync(request.ReservationId, cancellationToken);
        if (reservation == null)
        {
            throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với ID {request.ReservationId}.");
        }

        if (reservation.customer_id != currentUserId)
        {
            throw new ForbiddenException("Bạn không có quyền thanh toán cho đơn đặt chỗ của người khác.");
        }

        if (reservation.hold_until <= nowUtc || (reservation.status != "pending" && reservation.status != "awaiting_deposit"))
        {
            throw new ConflictException("Đơn đặt chỗ đã hết thời gian giữ chỗ 15 phút hoặc không ở trạng thái chờ thanh toán.");
        }

        if (reservation.deposit_snapshot != reservation.monthly_rate_snapshot)
        {
            throw new BadRequestException("Lỗi cấu hình: Tiền cọc phải bằng đúng 1 tháng tiền thuê niêm yết (BR-FIN-01).");
        }

        // Verify accepted policy version matches the active version
        var activePolicy = await _paymentRepository.GetActiveRentalTermsPolicyVersionAsync(reservation.start_date, cancellationToken);
        if (activePolicy == null || activePolicy.id != request.AcceptedPolicyVersionId)
        {
            throw new BadRequestException("Phiên bản điều khoản hợp đồng được gửi không khớp với phiên bản đang có hiệu lực. Vui lòng làm mới trang và chấp thuận phiên bản mới nhất.");
        }

        // Recompute quote and voucher in server
        var (discount, promo) = await CalculatePromotionDiscountAsync(
            reservation,
            request.PromotionCode,
            nowUtc,
            cancellationToken);

        var subtotal = reservation.deposit_snapshot + reservation.monthly_rate_snapshot + reservation.booking_fee_snapshot;
        var quotedTotal = subtotal - discount;

        var correlationId = Guid.NewGuid().ToString("N");
        var evidenceMetadata = new Dictionary<string, object>
        {
            { "accepted_policy_version_id", activePolicy.id },
            { "accepted_policy_version", activePolicy.version },
            { "user_id", currentUserId },
            { "accepted_at_utc", nowUtc.ToString("O") },
            { "correlation_id", correlationId },
            { "client_ip", clientIp ?? "127.0.0.1" }
        };

        var selectedMethod = (request.PaymentMethod?.Trim().ToLowerInvariant()) switch
        {
            "vnpay" => "vnpay",
            _ => "other"
        };
        var selectedProvider = selectedMethod == "vnpay" ? "vnpay" : "demo";

        var txParams = new CheckoutTransactionParams
        {
            ReservationId = reservation.id,
            CustomerId = currentUserId,
            PolicyVersionId = activePolicy.id,
            PolicyVersionNumber = activePolicy.version,
            IdempotencyKey = cleanIdempotencyKey,
            PaymentMethod = selectedMethod,
            PaymentProvider = selectedProvider,
            ComputedDeposit = reservation.deposit_snapshot,
            ComputedRent = reservation.monthly_rate_snapshot,
            ComputedBookingFee = reservation.booking_fee_snapshot,
            ComputedDiscount = discount,
            ComputedTotal = quotedTotal,
            AppliedPromotion = promo,
            NowUtc = nowUtc,
            ClientIp = clientIp,
            EvidenceMetadataJson = JsonSerializer.Serialize(evidenceMetadata)
        };

        CheckoutTransactionResult txResult;
        try
        {
            txResult = await _paymentRepository.ExecuteCheckoutTransactionAsync(txParams, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ACTIVE_ATTEMPT_EXISTS:"))
        {
            var activeAttemptId = ex.Message.Substring("ACTIVE_ATTEMPT_EXISTS:".Length);
            throw new ConflictException($"Đã có phiên thanh toán đang chờ xử lý (Payment ID: {activeAttemptId}). Vui lòng tiếp tục giao dịch đó.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "HOLD_EXPIRED")
        {
            throw new ConflictException("Đơn đặt chỗ đã hết hạn giữ chỗ (15 phút).");
        }
        catch (InvalidOperationException ex) when (ex.Message == "DEPOSIT_RATE_MISMATCH")
        {
            throw new BadRequestException("Lỗi cấu hình: Tiền cọc phải bằng đúng 1 tháng tiền thuê niêm yết (BR-FIN-01).");
        }
        catch (InvalidOperationException ex) when (ex.Message == "PROMOTION_USAGE_LIMIT_EXCEEDED")
        {
            throw new BadRequestException("Mã giảm giá đã hết lượt sử dụng trên hệ thống.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "PROMOTION_CUSTOMER_LIMIT_EXCEEDED")
        {
            throw new BadRequestException("Bạn đã vượt quá số lần sử dụng tối đa của mã giảm giá này.");
        }

        // Generate hosted payment URL without holding DB transaction open
        IPaymentGateway gateway = selectedProvider == "vnpay" ? _vnpayGateway : _demoGateway;
        var order = new PaymentGatewayOrder
        {
            PaymentId = txResult.Payment.id,
            InvoiceId = txResult.Invoice.id,
            ReservationId = txResult.Reservation.id,
            ReservationCode = txResult.Reservation.reservation_code,
            Amount = txResult.Payment.amount,
            Currency = "VND",
            HoldUntil = txResult.Reservation.hold_until,
            ClientIp = clientIp ?? "127.0.0.1",
            Description = $"Thanh toán cọc & kỳ đầu đơn {txResult.Reservation.reservation_code}"
        };

        var checkoutUrl = await gateway.CreateCheckoutUrlAsync(order, cancellationToken);

        // Update payment attempt to 'pending'
        await _paymentRepository.UpdatePaymentStatusToPendingAsync(txResult.Payment.id, nowUtc, cancellationToken);

        return new CheckoutResponse
        {
            PaymentId = txResult.Payment.id,
            InvoiceId = txResult.Invoice.id,
            ReservationId = txResult.Reservation.id,
            ReservationCode = txResult.Reservation.reservation_code,
            Amount = txResult.Payment.amount,
            Currency = "VND",
            Status = "pending",
            CheckoutUrl = checkoutUrl,
            HoldUntil = txResult.Reservation.hold_until,
            Method = txResult.Payment.method,
            Provider = txResult.Payment.provider
        };
    }

    public async Task<PaymentGatewayCallbackResult> ProcessVnpayIpnAsync(
        IDictionary<string, string> queryParams,
        CancellationToken cancellationToken = default)
    {
        var verifyResult = await _vnpayGateway.VerifyCallbackAsync(queryParams, cancellationToken);
        if (!verifyResult.IsValidSignature)
        {
            _logger.LogWarning("VNPAY IPN callback signature verification failed.");
            return verifyResult;
        }

        if (!long.TryParse(verifyResult.TxnRef, out var paymentId))
        {
            _logger.LogWarning("VNPAY IPN invalid TxnRef: {TxnRef}", verifyResult.TxnRef);
            verifyResult.FailureReason = "Invalid payment TxnRef.";
            return verifyResult;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var rawPayloadJson = JsonSerializer.Serialize(queryParams);
        var externalEventId = $"VNPAY-{verifyResult.TxnRef}-{verifyResult.ProviderTransactionId ?? verifyResult.ResponseCode}";

        if (!verifyResult.IsSuccess)
        {
            _logger.LogInformation("VNPAY callback reported failed payment for Payment {PaymentId}. Reason: {Reason}", paymentId, verifyResult.FailureReason);

            var failedParams = new FinalizeFailedPaymentParams
            {
                PaymentId = paymentId,
                Provider = "vnpay",
                ExternalEventId = externalEventId,
                FailureReason = verifyResult.FailureReason ?? "VNPAY payment declined.",
                RawPayloadJson = rawPayloadJson,
                FailedAt = nowUtc
            };

            await _paymentRepository.ExecuteFinalizeFailedPaymentTransactionAsync(failedParams, cancellationToken);
            return verifyResult;
        }

        var finalizeParams = new FinalizePaymentParams
        {
            PaymentId = paymentId,
            Provider = "vnpay",
            ProviderTransactionId = verifyResult.ProviderTransactionId,
            Amount = verifyResult.Amount,
            ExternalEventId = externalEventId,
            RawPayloadJson = rawPayloadJson,
            PaidAt = verifyResult.PayDate ?? nowUtc
        };

        var finalResult = await _paymentRepository.ExecuteFinalizePaymentTransactionAsync(finalizeParams, cancellationToken);
        if (finalResult.ReconciliationRequired)
        {
            _logger.LogWarning(
                "Payment {PaymentId} succeeded at VNPAY, but hold expired/cancelled. Reconciliation required: {Reason}",
                paymentId,
                finalResult.ReconciliationReason);
        }

        return verifyResult;
    }

    public async Task<PaymentDetailResponse> ProcessDemoCompleteAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long paymentId,
        DemoPaymentCompleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var env = _configuration["ASPNETCORE_ENVIRONMENT"] ??
                  Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                  "Development";

        var allowDemoStr = _configuration["Payment:AllowDemoSimulator"];
        var allowDemo = string.IsNullOrWhiteSpace(allowDemoStr) || (bool.TryParse(allowDemoStr, out var d) && d);

        var isDevOrTest = env.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
                          env.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
                          allowDemo;

        if (!isDevOrTest)
        {
            throw new ForbiddenException("Mô phỏng hoàn tất thanh toán Demo chỉ được phép chạy trong môi trường Development/Test.");
        }

        var payment = await _paymentRepository.GetPaymentByIdAsync(paymentId, cancellationToken);
        if (payment == null)
        {
            throw new NotFoundException($"Không tìm thấy giao dịch thanh toán {paymentId}.");
        }

        var reservation = payment.target_invoice?.reservation;
        if (reservation != null)
        {
            await AssertReservationAccessAsync(currentUserId, roles, reservation, cancellationToken);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var externalEventId = $"DEMO-{paymentId}-{Guid.NewGuid():N}".Substring(0, 30);
        var rawPayloadJson = JsonSerializer.Serialize(request);

        if (!request.IsSuccess)
        {
            var failedParams = new FinalizeFailedPaymentParams
            {
                PaymentId = paymentId,
                Provider = "demo",
                ExternalEventId = externalEventId,
                FailureReason = request.FailureReason ?? "Mô phỏng thanh toán thất bại.",
                RawPayloadJson = rawPayloadJson,
                FailedAt = nowUtc
            };

            await _paymentRepository.ExecuteFinalizeFailedPaymentTransactionAsync(failedParams, cancellationToken);
        }
        else
        {
            var providerTxnId = string.IsNullOrWhiteSpace(request.ProviderTransactionId)
                ? $"DEMO-TXN-{Guid.NewGuid():N}".Substring(0, 20)
                : request.ProviderTransactionId.Trim();

            var finalizeParams = new FinalizePaymentParams
            {
                PaymentId = paymentId,
                Provider = "demo",
                ProviderTransactionId = providerTxnId,
                Amount = payment.amount,
                ExternalEventId = externalEventId,
                RawPayloadJson = rawPayloadJson,
                PaidAt = nowUtc
            };

            await _paymentRepository.ExecuteFinalizePaymentTransactionAsync(finalizeParams, cancellationToken);
        }

        var refreshed = await _paymentRepository.GetPaymentByIdAsync(paymentId, cancellationToken);
        return MapToPaymentDetailResponse(refreshed ?? payment, nowUtc);
    }

    public async Task<PaymentDetailResponse> GetPaymentByIdAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long paymentId,
        CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.GetPaymentByIdAsync(paymentId, cancellationToken);
        if (payment == null)
        {
            throw new NotFoundException($"Không tìm thấy giao dịch thanh toán với mã ID {paymentId}.");
        }

        var reservation = payment.target_invoice?.reservation;
        if (reservation != null)
        {
            await AssertReservationAccessAsync(currentUserId, roles, reservation, cancellationToken);
        }
        else if (payment.customer_id != currentUserId && !RoleConstants.IsEmployeeRole(roles.FirstOrDefault() ?? string.Empty))
        {
            throw new ForbiddenException("Bạn không có quyền xem thông tin giao dịch này.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        return MapToPaymentDetailResponse(payment, nowUtc);
    }

    public async Task<List<PaymentDetailResponse>> GetPaymentsRequiringReconciliationAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        var isEmployee = roles.Any(RoleConstants.IsEmployeeRole) || roles.Contains(RoleConstants.SystemAdministrator);
        if (!isEmployee)
        {
            throw new ForbiddenException("Chỉ nhân viên quản lý hoặc quản trị viên mới có quyền tra cứu danh sách đối soát thanh toán.");
        }

        List<long>? accessibleFacilityIds = null;
        if (!roles.Contains(RoleConstants.SystemAdministrator) && !roles.Contains(RoleConstants.BusinessOperationsManager))
        {
            accessibleFacilityIds = await _facilityScopeService.GetAccessibleFacilityIdsAsync(currentUserId, cancellationToken);
        }

        var payments = await _paymentRepository.GetPaymentsRequiringReconciliationAsync(accessibleFacilityIds, cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        return payments.Select(p => MapToPaymentDetailResponse(p, nowUtc)).ToList();
    }

    private async Task<(decimal Discount, promotion? Promotion)> CalculatePromotionDiscountAsync(
        reservation reservation,
        string? promotionCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(promotionCode))
        {
            return (0m, null);
        }

        var promo = await _paymentRepository.GetPromotionByCodeAsync(promotionCode, nowUtc, cancellationToken);
        if (promo == null || !promo.is_active)
        {
            throw new BadRequestException($"Mã giảm giá '{promotionCode.Trim().ToUpperInvariant()}' không tồn tại, đã hết hạn hoặc hiện không áp dụng.");
        }

        // Validate overall usage limit
        if (promo.usage_limit.HasValue)
        {
            var usageCount = await _paymentRepository.GetPromotionActiveRedemptionsCountAsync(promo.id, cancellationToken);
            if (usageCount >= promo.usage_limit.Value)
            {
                throw new BadRequestException("Mã giảm giá đã đạt giới hạn số lần sử dụng trên toàn hệ thống.");
            }
        }

        // Validate per customer limit
        if (promo.per_customer_limit.HasValue)
        {
            var customerUsage = await _paymentRepository.GetCustomerActiveRedemptionsCountAsync(promo.id, reservation.customer_id, cancellationToken);
            if (customerUsage >= promo.per_customer_limit.Value)
            {
                throw new BadRequestException("Bạn đã sử dụng hết lượt cho phép của mã giảm giá này.");
            }
        }

        // Validate promotion rules
        var rentalMonths = CalculateRentalMonths(reservation.start_date, reservation.end_date);
        var baseRentAndFee = reservation.monthly_rate_snapshot + reservation.booking_fee_snapshot;

        foreach (var rule in promo.promotion_rules)
        {
            switch (rule.rule_type)
            {
                case "minimum_months":
                    if (int.TryParse(rule.rule_value, out var minMonths) && rentalMonths < minMonths)
                    {
                        throw new BadRequestException($"Mã giảm giá yêu cầu thời hạn thuê tối thiểu {minMonths} tháng (thời hạn hiện tại: {rentalMonths} tháng).");
                    }
                    break;

                case "minimum_amount":
                    if (decimal.TryParse(rule.rule_value, out var minAmount) && baseRentAndFee < minAmount)
                    {
                        throw new BadRequestException($"Mã giảm giá yêu cầu giá trị tiền thuê tối thiểu {minAmount:N0} đ.");
                    }
                    break;

                case "facility":
                    if (long.TryParse(rule.rule_value, out var facilityId) && reservation.facility_id != facilityId)
                    {
                        throw new BadRequestException("Mã giảm giá không áp dụng cho cơ sở này.");
                    }
                    break;

                case "unit_type":
                    if (long.TryParse(rule.rule_value, out var unitTypeId) && reservation.unit_type_id != unitTypeId)
                    {
                        throw new BadRequestException("Mã giảm giá không áp dụng cho loại kho này.");
                    }
                    break;

                case "new_customer":
                    if (rule.rule_value == "1" || rule.rule_value.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        var isNew = await _paymentRepository.IsNewCustomerAsync(reservation.customer_id, cancellationToken);
                        if (!isNew)
                        {
                            throw new BadRequestException("Mã giảm giá này chỉ dành riêng cho khách hàng mới đặt chỗ lần đầu.");
                        }
                    }
                    break;
            }
        }

        // Calculate discount per business decisions
        decimal discount;
        if (promo.discount_type == "free_days")
        {
            var firstMonthEnd = reservation.start_date.AddMonths(1);
            var daysInFirstMonth = (firstMonthEnd.ToDateTime(TimeOnly.MinValue) - reservation.start_date.ToDateTime(TimeOnly.MinValue)).Days;

            var freeDays = (int)promo.discount_value;
            if (freeDays <= 0)
            {
                throw new BadRequestException("Cấu hình số ngày miễn phí không hợp lệ.");
            }

            if (freeDays > daysInFirstMonth)
            {
                throw new BadRequestException($"Số ngày miễn phí ({freeDays} ngày) vượt quá số ngày của kỳ thuê tháng đầu ({daysInFirstMonth} ngày).");
            }

            var rawDiscount = reservation.monthly_rate_snapshot * freeDays / (decimal)daysInFirstMonth;
            discount = Math.Round(rawDiscount, MidpointRounding.AwayFromZero);

            if (promo.max_discount_amount.HasValue && discount > promo.max_discount_amount.Value)
            {
                discount = promo.max_discount_amount.Value;
            }

            // Cap at first month rent (does NOT discount deposit or booking fee)
            if (discount > reservation.monthly_rate_snapshot)
            {
                discount = reservation.monthly_rate_snapshot;
            }
        }
        else if (promo.discount_type == "percentage")
        {
            var rawDiscount = baseRentAndFee * (promo.discount_value / 100m);
            discount = Math.Round(rawDiscount, MidpointRounding.AwayFromZero);

            if (promo.max_discount_amount.HasValue && discount > promo.max_discount_amount.Value)
            {
                discount = promo.max_discount_amount.Value;
            }

            // Cap at baseRentAndFee (does NOT discount deposit)
            if (discount > baseRentAndFee)
            {
                discount = baseRentAndFee;
            }
        }
        else // "fixed"
        {
            discount = promo.discount_value;

            if (promo.max_discount_amount.HasValue && discount > promo.max_discount_amount.Value)
            {
                discount = promo.max_discount_amount.Value;
            }

            // Cap at baseRentAndFee (does NOT discount deposit)
            if (discount > baseRentAndFee)
            {
                discount = baseRentAndFee;
            }
        }

        return (discount, promo);
    }

    private async Task AssertReservationAccessAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        reservation reservation,
        CancellationToken cancellationToken)
    {
        if (roles.Contains(RoleConstants.SystemAdministrator) ||
            roles.Contains(RoleConstants.BusinessOperationsManager))
        {
            return;
        }

        if (roles.Contains(RoleConstants.StorageCustomer) && reservation.customer_id == currentUserId)
        {
            return;
        }

        if (roles.Contains(RoleConstants.FacilityStaff) || roles.Contains(RoleConstants.FacilityManager))
        {
            var hasAccess = await _facilityScopeService.HasAccessToFacilityAsync(
                currentUserId,
                reservation.facility_id,
                roles.First(),
                cancellationToken);

            if (hasAccess) return;
        }

        throw new ForbiddenException("Bạn không có quyền truy cập vào thông tin đơn đặt chỗ này.");
    }

    private static int CalculateRentalMonths(DateOnly start, DateOnly end)
    {
        var months = (end.Year - start.Year) * 12 + (end.Month - start.Month);
        if (end.Day < start.Day) months--;
        return Math.Max(1, months);
    }

    private static PaymentDetailResponse MapToPaymentDetailResponse(payment p, DateTimeOffset nowUtc)
    {
        var holdUntil = p.target_invoice?.reservation?.hold_until;
        var holdExpired = holdUntil.HasValue && holdUntil.Value <= nowUtc;
        var isReconciliationRequired = p.failure_reason == "LATE_PAYMENT_HOLD_EXPIRED" ||
                                       p.metadata.Contains("\"reconciliation_required\":true") ||
                                       p.metadata.Contains("\"reconciliation_required\": true");

        return new PaymentDetailResponse
        {
            PaymentId = p.id,
            CustomerId = p.customer_id,
            TargetInvoiceId = p.target_invoice_id,
            ReservationId = p.target_invoice?.reservation_id,
            ReservationCode = p.target_invoice?.reservation?.reservation_code,
            AgreementId = p.target_invoice?.agreement_id,
            AgreementNo = p.target_invoice?.agreement?.agreement_no,
            Amount = p.amount,
            Currency = p.currency,
            Method = p.method,
            Provider = p.provider,
            ProviderTransactionId = p.provider_transaction_id,
            IdempotencyKey = p.idempotency_key,
            Status = p.status,
            PaidAt = p.paid_at,
            FailureReason = p.failure_reason,
            ReconciliationRequired = isReconciliationRequired,
            MetadataJson = p.metadata,
            CreatedAt = p.created_at,
            HoldUntil = holdUntil,
            HoldExpired = holdExpired
        };
    }
}
