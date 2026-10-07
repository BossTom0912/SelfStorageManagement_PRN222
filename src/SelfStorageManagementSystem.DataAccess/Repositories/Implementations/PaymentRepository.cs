using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Implementations;

public class PaymentRepository : IPaymentRepository
{
    private readonly SelfStorageDbContext _context;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> InMemorySemaphores = new();

    public PaymentRepository(SelfStorageDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<reservation?> GetReservationForQuoteAsync(
        long reservationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.reservations
            .AsNoTracking()
            .Include(r => r.customer)
                .ThenInclude(c => c.user)
            .Include(r => r.facility)
            .Include(r => r.unit_type)
            .Include(r => r.facility_rate)
            .Include(r => r.promotion_redemption)
                .ThenInclude(pr => pr!.promotion)
            .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);
    }

    public async Task<policy_version?> GetActiveRentalTermsPolicyVersionAsync(
        DateOnly forDate,
        CancellationToken cancellationToken = default)
    {
        return await _context.policy_versions
            .AsNoTracking()
            .Where(p => p.policy_type == "rental_terms" &&
                        p.valid_from <= forDate &&
                        (p.valid_to == null || p.valid_to > forDate))
            .OrderByDescending(p => p.valid_from)
            .ThenByDescending(p => p.id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<promotion?> GetPromotionByCodeAsync(
        string code,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var cleanCode = code.Trim().ToUpperInvariant();
        return await _context.promotions
            .Include(p => p.promotion_rules)
            .FirstOrDefaultAsync(p => p.code == cleanCode &&
                                      p.is_active &&
                                      p.valid_from <= nowUtc &&
                                      p.valid_to >= nowUtc,
                                      cancellationToken);
    }

    public async Task<int> GetPromotionActiveRedemptionsCountAsync(
        long promotionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.promotion_redemptions
            .CountAsync(pr => pr.promotion_id == promotionId &&
                              (pr.status == "reserved" || pr.status == "applied"),
                              cancellationToken);
    }

    public async Task<int> GetCustomerActiveRedemptionsCountAsync(
        long promotionId,
        long customerId,
        CancellationToken cancellationToken = default)
    {
        return await _context.promotion_redemptions
            .CountAsync(pr => pr.promotion_id == promotionId &&
                              pr.customer_id == customerId &&
                              (pr.status == "reserved" || pr.status == "applied"),
                              cancellationToken);
    }

    public async Task<bool> IsNewCustomerAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var hasPriorConfirmedReservations = await _context.reservations
            .AnyAsync(r => r.customer_id == customerId &&
                           (r.status == "confirmed" || r.status == "checked_in" || r.status == "converted" || r.status == "completed"),
                           cancellationToken);

        if (hasPriorConfirmedReservations) return false;

        var hasPriorAgreements = await _context.rental_agreements
            .AnyAsync(a => a.customer_id == customerId, cancellationToken);

        return !hasPriorAgreements;
    }

    public async Task<payment?> GetPaymentByIdAsync(
        long paymentId,
        CancellationToken cancellationToken = default)
    {
        return await _context.payments
            .Include(p => p.customer)
                .ThenInclude(c => c.user)
            .Include(p => p.target_invoice)
                .ThenInclude(inv => inv.reservation)
            .Include(p => p.target_invoice)
                .ThenInclude(inv => inv.agreement)
            .Include(p => p.payment_allocations)
            .FirstOrDefaultAsync(p => p.id == paymentId, cancellationToken);
    }

    public async Task<payment?> GetPaymentByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return await _context.payments
            .Include(p => p.target_invoice)
                .ThenInclude(inv => inv.reservation)
            .FirstOrDefaultAsync(p => p.idempotency_key == idempotencyKey, cancellationToken);
    }

    public async Task<payment?> GetActivePendingPaymentByReservationIdAsync(
        long reservationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        return await _context.payments
            .Include(p => p.target_invoice)
            .FirstOrDefaultAsync(p => p.target_invoice.reservation_id == reservationId &&
                                      (p.status == "initiated" || p.status == "pending") &&
                                      p.target_invoice.reservation != null &&
                                      p.target_invoice.reservation.hold_until > nowUtc,
                                      cancellationToken);
    }

    public async Task<invoice?> GetInvoiceByIdAsync(
        long invoiceId,
        CancellationToken cancellationToken = default)
    {
        return await _context.invoices
            .Include(inv => inv.customer)
                .ThenInclude(c => c.user)
            .Include(inv => inv.reservation)
            .Include(inv => inv.agreement)
            .Include(inv => inv.invoice_lines)
            .Include(inv => inv.payment_allocations)
            .FirstOrDefaultAsync(inv => inv.id == invoiceId, cancellationToken);
    }

    public async Task<rental_agreement?> GetAgreementByIdAsync(
        long agreementId,
        CancellationToken cancellationToken = default)
    {
        return await _context.rental_agreements
            .Include(a => a.customer)
                .ThenInclude(c => c.user)
            .Include(a => a.facility)
            .Include(a => a.policy_version)
            .Include(a => a.reservation)
            .FirstOrDefaultAsync(a => a.id == agreementId, cancellationToken);
    }

    public async Task<rental_agreement?> GetAgreementByReservationIdAsync(
        long reservationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.rental_agreements
            .Include(a => a.customer)
                .ThenInclude(c => c.user)
            .Include(a => a.facility)
            .Include(a => a.policy_version)
            .FirstOrDefaultAsync(a => a.reservation_id == reservationId, cancellationToken);
    }

    public async Task<integration_event?> GetIntegrationEventAsync(
        string source,
        string externalEventId,
        CancellationToken cancellationToken = default)
    {
        return await _context.integration_events
            .FirstOrDefaultAsync(e => e.source == source && e.external_event_id == externalEventId, cancellationToken);
    }

    public async Task<List<payment>> GetPaymentsRequiringReconciliationAsync(
        List<long>? accessibleFacilityIds,
        CancellationToken cancellationToken = default)
    {
        var query = _context.payments
            .Include(p => p.customer)
                .ThenInclude(c => c.user)
            .Include(p => p.target_invoice)
                .ThenInclude(inv => inv.reservation)
                    .ThenInclude(r => r!.facility)
            .Where(p => p.failure_reason == "LATE_PAYMENT_HOLD_EXPIRED" ||
                        p.metadata.Contains("\"reconciliation_required\":true") ||
                        p.metadata.Contains("\"reconciliation_required\": true"));

        if (accessibleFacilityIds != null)
        {
            query = query.Where(p => p.target_invoice.reservation != null &&
                                     accessibleFacilityIds.Contains(p.target_invoice.reservation.facility_id));
        }

        return await query.OrderByDescending(p => p.created_at).ToListAsync(cancellationToken);
    }

    public async Task<CheckoutTransactionResult> ExecuteCheckoutTransactionAsync(
        CheckoutTransactionParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var lockKey = $"SS_Reservation_{parameters.ReservationId}";
        var isRelational = _context.Database.IsRelational();

        SemaphoreSlim? inMemorySemaphore = null;
        if (!isRelational)
        {
            inMemorySemaphore = InMemorySemaphores.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            var entered = await inMemorySemaphore.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!entered)
            {
                throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

                var resultParam = new SqlParameter
                {
                    ParameterName = "@Result",
                    SqlDbType = SqlDbType.Int,
                    Direction = ParameterDirection.Output
                };
                var resourceParam = new SqlParameter("@Resource", lockKey);

                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000",
                    new object[] { resultParam, resourceParam },
                    cancellationToken);

                var lockResult = (int)(resultParam.Value ?? -999);
                if (lockResult < 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
                }
            }

            // 1. Idempotency check: check if payment with this key already exists
            var existingPaymentWithKey = await _context.payments
                .Include(p => p.target_invoice)
                .FirstOrDefaultAsync(p => p.idempotency_key == parameters.IdempotencyKey, cancellationToken);

            if (existingPaymentWithKey != null)
            {
                var existingReservation = await _context.reservations
                    .FirstOrDefaultAsync(r => r.id == parameters.ReservationId, cancellationToken);

                if (existingReservation == null)
                {
                    if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("RESERVATION_NOT_FOUND");
                }

                if (transaction != null) await transaction.CommitAsync(cancellationToken);

                return new CheckoutTransactionResult
                {
                    IsIdempotentReplay = true,
                    Payment = existingPaymentWithKey,
                    Invoice = existingPaymentWithKey.target_invoice,
                    Reservation = existingReservation
                };
            }

            // 2. Check if another active pending payment attempt exists with a DIFFERENT key
            var otherActiveAttempt = await _context.payments
                .Include(p => p.target_invoice)
                .FirstOrDefaultAsync(p => p.target_invoice.reservation_id == parameters.ReservationId &&
                                          (p.status == "initiated" || p.status == "pending"),
                                          cancellationToken);

            var reservation = await _context.reservations
                .Include(r => r.facility)
                .FirstOrDefaultAsync(r => r.id == parameters.ReservationId, cancellationToken);

            if (reservation == null)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("RESERVATION_NOT_FOUND");
            }

            if (otherActiveAttempt != null && reservation.hold_until > parameters.NowUtc)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException($"ACTIVE_ATTEMPT_EXISTS:{otherActiveAttempt.id}");
            }

            // 3. Validate reservation hold state
            if (reservation.status != "pending" && reservation.status != "awaiting_deposit")
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException($"INVALID_RESERVATION_STATUS:{reservation.status}");
            }

            if (reservation.hold_until <= parameters.NowUtc)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("HOLD_EXPIRED");
            }

            if (reservation.deposit_snapshot != reservation.monthly_rate_snapshot)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("DEPOSIT_RATE_MISMATCH");
            }

            // 4. Handle promotion voucher inside locked transaction
            if (parameters.AppliedPromotion != null)
            {
                // Revalidate usage limit inside lock
                if (parameters.AppliedPromotion.usage_limit.HasValue)
                {
                    var currentUsage = await _context.promotion_redemptions
                        .CountAsync(pr => pr.promotion_id == parameters.AppliedPromotion.id &&
                                          pr.reservation_id != parameters.ReservationId &&
                                          (pr.status == "reserved" || pr.status == "applied"),
                                          cancellationToken);

                    if (currentUsage >= parameters.AppliedPromotion.usage_limit.Value)
                    {
                        if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                        throw new InvalidOperationException("PROMOTION_USAGE_LIMIT_EXCEEDED");
                    }
                }

                // Revalidate customer limit inside lock
                if (parameters.AppliedPromotion.per_customer_limit.HasValue)
                {
                    var currentCustomerUsage = await _context.promotion_redemptions
                        .CountAsync(pr => pr.promotion_id == parameters.AppliedPromotion.id &&
                                          pr.customer_id == parameters.CustomerId &&
                                          pr.reservation_id != parameters.ReservationId &&
                                          (pr.status == "reserved" || pr.status == "applied"),
                                          cancellationToken);

                    if (currentCustomerUsage >= parameters.AppliedPromotion.per_customer_limit.Value)
                    {
                        if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                        throw new InvalidOperationException("PROMOTION_CUSTOMER_LIMIT_EXCEEDED");
                    }
                }

                var existingRedemption = await _context.promotion_redemptions
                    .FirstOrDefaultAsync(pr => pr.reservation_id == parameters.ReservationId, cancellationToken);

                if (existingRedemption != null)
                {
                    existingRedemption.promotion_id = parameters.AppliedPromotion.id;
                    existingRedemption.discount_amount = parameters.ComputedDiscount;
                    existingRedemption.status = "reserved";
                    existingRedemption.redeemed_at = parameters.NowUtc;
                }
                else
                {
                    var newRedemption = new promotion_redemption
                    {
                        promotion_id = parameters.AppliedPromotion.id,
                        customer_id = parameters.CustomerId,
                        reservation_id = parameters.ReservationId,
                        discount_amount = parameters.ComputedDiscount,
                        status = "reserved",
                        redeemed_at = parameters.NowUtc
                    };
                    _context.promotion_redemptions.Add(newRedemption);
                }
            }

            // Update reservation snapshots
            reservation.discount_snapshot = parameters.ComputedDiscount;
            reservation.quoted_total = parameters.ComputedTotal;
            reservation.status = "awaiting_deposit";
            reservation.updated_at = parameters.NowUtc;

            // 5. Create or reuse initial invoice
            var existingInvoice = await _context.invoices
                .Include(inv => inv.invoice_lines)
                .FirstOrDefaultAsync(inv => inv.reservation_id == parameters.ReservationId &&
                                            (inv.status == "draft" || inv.status == "open") &&
                                            inv.paid_amount == 0,
                                            cancellationToken);

            invoice targetInvoice;
            if (existingInvoice == null)
            {
                var today = DateOnly.FromDateTime(parameters.NowUtc.DateTime);
                var invoiceNo = GenerateInvoiceNo(reservation.facility?.code, parameters.NowUtc);

                targetInvoice = new invoice
                {
                    invoice_no = invoiceNo,
                    customer_id = parameters.CustomerId,
                    reservation_id = parameters.ReservationId,
                    issue_date = today,
                    due_date = reservation.start_date >= today ? reservation.start_date : today,
                    currency = "VND",
                    subtotal_amount = 0m,
                    discount_amount = 0m,
                    tax_amount = 0m,
                    total_amount = 0m,
                    paid_amount = 0m,
                    status = "draft",
                    created_at = parameters.NowUtc,
                    updated_at = parameters.NowUtc
                };

                _context.invoices.Add(targetInvoice);
                await _context.SaveChangesAsync(cancellationToken);

                // Add invoice lines
                var lines = new List<invoice_line>
                {
                    new invoice_line
                    {
                        invoice_id = targetInvoice.id,
                        line_type = "deposit",
                        description = "Tiền đặt cọc giữ kho (1 tháng thuê)",
                        quantity = 1,
                        unit_price = parameters.ComputedDeposit,
                        metadata = "{}"
                    },
                    new invoice_line
                    {
                        invoice_id = targetInvoice.id,
                        line_type = "rent",
                        description = "Tiền thuê kho kỳ đầu (tháng đầu tiên)",
                        quantity = 1,
                        unit_price = parameters.ComputedRent,
                        metadata = "{}"
                    }
                };

                if (parameters.ComputedBookingFee > 0)
                {
                    lines.Add(new invoice_line
                    {
                        invoice_id = targetInvoice.id,
                        line_type = "booking_fee",
                        description = "Phí đặt chỗ ban đầu",
                        quantity = 1,
                        unit_price = parameters.ComputedBookingFee,
                        metadata = "{}"
                    });
                }

                if (parameters.ComputedDiscount > 0)
                {
                    lines.Add(new invoice_line
                    {
                        invoice_id = targetInvoice.id,
                        line_type = "discount",
                        description = $"Ưu đãi giảm giá ({parameters.AppliedPromotion?.code})",
                        quantity = 1,
                        unit_price = -parameters.ComputedDiscount,
                        metadata = "{}"
                    });
                }

                _context.invoice_lines.AddRange(lines);
                await _context.SaveChangesAsync(cancellationToken);

                // For in-memory database where SQL triggers do not fire, manually compute totals
                if (!isRelational)
                {
                    var subtotal = parameters.ComputedDeposit + parameters.ComputedRent + parameters.ComputedBookingFee;
                    var discount = parameters.ComputedDiscount;
                    targetInvoice.subtotal_amount = subtotal;
                    targetInvoice.discount_amount = discount;
                    targetInvoice.total_amount = subtotal - discount;
                }
                else
                {
                    // Reload invoice updated by trg_invoice_lines_refresh_totals
                    _context.Entry(targetInvoice).State = EntityState.Detached;
                    targetInvoice = await _context.invoices
                        .FirstAsync(i => i.id == targetInvoice.id, cancellationToken);
                }

                if (targetInvoice.total_amount != parameters.ComputedTotal)
                {
                    if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException($"INVOICE_TOTAL_MISMATCH: Computed {parameters.ComputedTotal} vs DB {targetInvoice.total_amount}");
                }

                targetInvoice.status = "open";
                targetInvoice.opened_at = parameters.NowUtc;
                targetInvoice.updated_at = parameters.NowUtc;
                await _context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                targetInvoice = existingInvoice;
            }

            // 6. Create payment attempt record with status 'initiated'
            var payment = new payment
            {
                customer_id = parameters.CustomerId,
                target_invoice_id = targetInvoice.id,
                amount = targetInvoice.total_amount,
                currency = "VND",
                method = parameters.PaymentMethod,
                provider = parameters.PaymentProvider,
                idempotency_key = parameters.IdempotencyKey,
                status = "initiated",
                metadata = parameters.EvidenceMetadataJson,
                created_at = parameters.NowUtc,
                updated_at = parameters.NowUtc
            };

            _context.payments.Add(payment);
            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new CheckoutTransactionResult
            {
                IsIdempotentReplay = false,
                Payment = payment,
                Invoice = targetInvoice,
                Reservation = reservation
            };
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
            inMemorySemaphore?.Release();
        }
    }

    public async Task UpdatePaymentStatusToPendingAsync(
        long paymentId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var payment = await _context.payments.FirstOrDefaultAsync(p => p.id == paymentId, cancellationToken);
        if (payment != null && payment.status == "initiated")
        {
            payment.status = "pending";
            payment.updated_at = nowUtc;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<FinalizePaymentResult> ExecuteFinalizePaymentTransactionAsync(
        FinalizePaymentParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var payment = await _context.payments
            .Include(p => p.target_invoice)
                .ThenInclude(inv => inv.reservation)
                    .ThenInclude(r => r!.facility)
            .FirstOrDefaultAsync(p => p.id == parameters.PaymentId, cancellationToken);

        if (payment == null)
        {
            throw new InvalidOperationException("PAYMENT_NOT_FOUND");
        }

        var reservationId = payment.target_invoice.reservation_id ?? 0;
        var lockKey = $"SS_Reservation_{reservationId}";
        var isRelational = _context.Database.IsRelational();

        SemaphoreSlim? inMemorySemaphore = null;
        if (!isRelational)
        {
            inMemorySemaphore = InMemorySemaphores.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            var entered = await inMemorySemaphore.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!entered)
            {
                throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

                var resultParam = new SqlParameter
                {
                    ParameterName = "@Result",
                    SqlDbType = SqlDbType.Int,
                    Direction = ParameterDirection.Output
                };
                var resourceParam = new SqlParameter("@Resource", lockKey);

                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000",
                    new object[] { resultParam, resourceParam },
                    cancellationToken);

                var lockResult = (int)(resultParam.Value ?? -999);
                if (lockResult < 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
                }
            }

            // Check if integration event has already been processed (idempotency)
            var existingEvent = await _context.integration_events
                .FirstOrDefaultAsync(e => e.source == parameters.Provider &&
                                          e.external_event_id == parameters.ExternalEventId,
                                          cancellationToken);

            if (existingEvent != null && existingEvent.status == "processed")
            {
                var existingAgreement = await _context.rental_agreements
                    .FirstOrDefaultAsync(a => a.reservation_id == reservationId, cancellationToken);

                if (transaction != null) await transaction.CommitAsync(cancellationToken);

                return new FinalizePaymentResult
                {
                    IsAlreadyProcessed = true,
                    Payment = payment,
                    Invoice = payment.target_invoice,
                    Reservation = payment.target_invoice.reservation!,
                    Agreement = existingAgreement
                };
            }

            // Reload fresh state inside lock
            var invoice = payment.target_invoice;
            var reservation = invoice.reservation;

            if (reservation == null)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("RESERVATION_NOT_FOUND");
            }

            // Idempotent check on payment status
            if (payment.status == "succeeded")
            {
                var existingAgreement = await _context.rental_agreements
                    .FirstOrDefaultAsync(a => a.reservation_id == reservationId, cancellationToken);

                if (transaction != null) await transaction.CommitAsync(cancellationToken);

                return new FinalizePaymentResult
                {
                    IsAlreadyProcessed = true,
                    Payment = payment,
                    Invoice = invoice,
                    Reservation = reservation,
                    Agreement = existingAgreement
                };
            }

            // Check if reservation hold is expired or cancelled -> LATE PAYMENT CASE (BR-RSV-01, Plan 4.3, Decision 5)
            var isHoldExpiredOrCancelled = reservation.hold_until < parameters.PaidAt ||
                                           reservation.status == "cancelled" ||
                                           reservation.status == "expired" ||
                                           invoice.status == "voided";

            if (isHoldExpiredOrCancelled)
            {
                // Provider confirmed money, but hold has already expired/cancelled!
                // DO NOT restore reservation!
                // DO NOT create agreement!
                // DO NOT allocate payment to invoice!
                payment.status = "succeeded";
                payment.paid_at = parameters.PaidAt;
                payment.provider_transaction_id = parameters.ProviderTransactionId;
                payment.failure_reason = "LATE_PAYMENT_HOLD_EXPIRED";
                payment.updated_at = parameters.PaidAt;

                var metaDict = SafeParseJson(payment.metadata);
                metaDict["reconciliation_required"] = true;
                metaDict["reconciliation_reason"] = "Provider confirmed payment after hold expired or was cancelled.";
                metaDict["provider_transaction_id"] = parameters.ProviderTransactionId ?? string.Empty;
                payment.metadata = JsonSerializer.Serialize(metaDict);

                // Create refund request for staff review/approval
                var refundKey = $"REFUND-{payment.id}-{Guid.NewGuid():N}".Substring(0, 32);
                var refundRequest = new refund
                {
                    payment_id = payment.id,
                    agreement_id = null,
                    amount = payment.amount,
                    currency = "VND",
                    reason = "Khoản thanh toán đến sau khi hold giữ chỗ đã hết hạn/hủy (Cần đối soát & hoàn tiền)",
                    provider = parameters.Provider,
                    idempotency_key = refundKey,
                    status = "requested",
                    created_at = parameters.PaidAt,
                    updated_at = parameters.PaidAt
                };
                _context.refunds.Add(refundRequest);

                // Record integration event
                if (existingEvent == null)
                {
                    var lateEvent = new integration_event
                    {
                        source = parameters.Provider,
                        external_event_id = parameters.ExternalEventId,
                        event_type = "payment_succeeded_late_reconciliation",
                        payload = parameters.RawPayloadJson,
                        status = "processed",
                        received_at = parameters.PaidAt,
                        processed_at = parameters.PaidAt
                    };
                    _context.integration_events.Add(lateEvent);
                }
                else
                {
                    existingEvent.status = "processed";
                    existingEvent.processed_at = parameters.PaidAt;
                }

                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null) await transaction.CommitAsync(cancellationToken);

                return new FinalizePaymentResult
                {
                    IsAlreadyProcessed = false,
                    ReconciliationRequired = true,
                    ReconciliationReason = "Hold đã hết hạn trước khi thanh toán hoàn tất. Hệ thống đã ghi nhận khoản tiền và tạo yêu cầu hoàn tiền để nhân sự đối soát.",
                    Payment = payment,
                    Invoice = invoice,
                    Reservation = reservation,
                    Agreement = null
                };
            }

            // NORMAL SUCCESSFUL PAYMENT CASE
            payment.status = "succeeded";
            payment.paid_at = parameters.PaidAt;
            payment.provider_transaction_id = parameters.ProviderTransactionId;
            payment.updated_at = parameters.PaidAt;

            // Add payment allocation
            var allocation = new payment_allocation
            {
                payment_id = payment.id,
                invoice_id = invoice.id,
                allocated_amount = payment.amount,
                allocated_at = parameters.PaidAt
            };
            _context.payment_allocations.Add(allocation);
            await _context.SaveChangesAsync(cancellationToken);

            // In SQL Server, trg_payment_allocation_guard automatically updates invoice paid_amount and status to 'paid'.
            // For in-memory test database:
            if (!isRelational)
            {
                invoice.paid_amount = payment.amount;
                invoice.status = "paid";
                invoice.updated_at = parameters.PaidAt;
            }
            else
            {
                _context.Entry(invoice).State = EntityState.Detached;
                invoice = await _context.invoices.FirstAsync(i => i.id == invoice.id, cancellationToken);
            }

            // Query active rental terms policy version
            var policyDate = reservation.start_date;
            var policyVersion = await _context.policy_versions
                .Where(p => p.policy_type == "rental_terms" &&
                            p.valid_from <= policyDate &&
                            (p.valid_to == null || p.valid_to > policyDate))
                .OrderByDescending(p => p.valid_from)
                .ThenByDescending(p => p.id)
                .FirstOrDefaultAsync(cancellationToken);

            var policyVersionId = policyVersion?.id ?? 1;

            // Generate agreement_no
            var agreementNo = GenerateAgreementNo(reservation.facility?.code, parameters.PaidAt);

            var agreement = new rental_agreement
            {
                agreement_no = agreementNo,
                reservation_id = reservation.id,
                customer_id = reservation.customer_id,
                facility_id = reservation.facility_id,
                policy_version_id = policyVersionId,
                start_date = reservation.start_date,
                end_date = reservation.end_date,
                monthly_rate_snapshot = reservation.monthly_rate_snapshot,
                deposit_snapshot = reservation.deposit_snapshot,
                deposit_balance = reservation.deposit_snapshot,
                status = "scheduled", // Scheduled awaiting digital handover in Function 5
                signed_at = null,     // Signed at is null in Function 4
                checked_in_at = null,
                checked_out_at = null,
                actual_end_date = null,
                created_at = parameters.PaidAt,
                updated_at = parameters.PaidAt
            };

            _context.rental_agreements.Add(agreement);
            await _context.SaveChangesAsync(cancellationToken);

            // Link invoice with agreement
            invoice.agreement_id = agreement.id;
            invoice.updated_at = parameters.PaidAt;

            // Confirm reservation
            reservation.status = "confirmed";
            reservation.confirmed_at = parameters.PaidAt;
            reservation.updated_at = parameters.PaidAt;

            // Apply promotion redemption if reserved
            var redemption = await _context.promotion_redemptions
                .FirstOrDefaultAsync(pr => pr.reservation_id == reservation.id && pr.status == "reserved", cancellationToken);

            if (redemption != null)
            {
                redemption.status = "applied";
                redemption.invoice_id = invoice.id;
            }

            // Save integration event
            if (existingEvent == null)
            {
                var successEvent = new integration_event
                {
                    source = parameters.Provider,
                    external_event_id = parameters.ExternalEventId,
                    event_type = "payment_succeeded",
                    payload = parameters.RawPayloadJson,
                    status = "processed",
                    received_at = parameters.PaidAt,
                    processed_at = parameters.PaidAt
                };
                _context.integration_events.Add(successEvent);
            }
            else
            {
                existingEvent.status = "processed";
                existingEvent.processed_at = parameters.PaidAt;
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new FinalizePaymentResult
            {
                IsAlreadyProcessed = false,
                ReconciliationRequired = false,
                Payment = payment,
                Invoice = invoice,
                Reservation = reservation,
                Agreement = agreement
            };
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
            inMemorySemaphore?.Release();
        }
    }

    public async Task<bool> ExecuteFinalizeFailedPaymentTransactionAsync(
        FinalizeFailedPaymentParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var payment = await _context.payments
            .Include(p => p.target_invoice)
            .FirstOrDefaultAsync(p => p.id == parameters.PaymentId, cancellationToken);

        if (payment == null) return false;

        // If payment is already terminal, no update needed
        if (payment.status == "succeeded" || payment.status == "cancelled")
        {
            return false;
        }

        payment.status = "failed";
        payment.failure_reason = parameters.FailureReason;
        payment.updated_at = parameters.FailedAt;

        var existingEvent = await _context.integration_events
            .FirstOrDefaultAsync(e => e.source == parameters.Provider &&
                                      e.external_event_id == parameters.ExternalEventId,
                                      cancellationToken);

        if (existingEvent == null)
        {
            var failedEvent = new integration_event
            {
                source = parameters.Provider,
                external_event_id = parameters.ExternalEventId,
                event_type = "payment_failed",
                payload = parameters.RawPayloadJson,
                status = "processed",
                received_at = parameters.FailedAt,
                processed_at = parameters.FailedAt
            };
            _context.integration_events.Add(failedEvent);
        }
        else
        {
            existingEvent.status = "processed";
            existingEvent.processed_at = parameters.FailedAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string GenerateInvoiceNo(string? facilityCode, DateTimeOffset nowUtc)
    {
        var cleanCode = string.IsNullOrWhiteSpace(facilityCode) ? "FAC" : facilityCode.Trim().ToUpperInvariant();
        var datePart = nowUtc.ToString("yyyyMMdd");
        var randomHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToUpperInvariant();
        return $"INV-{cleanCode}-{datePart}-{randomHex}";
    }

    private static string GenerateAgreementNo(string? facilityCode, DateTimeOffset nowUtc)
    {
        var cleanCode = string.IsNullOrWhiteSpace(facilityCode) ? "FAC" : facilityCode.Trim().ToUpperInvariant();
        var datePart = nowUtc.ToString("yyyyMMdd");
        var randomHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToUpperInvariant();
        return $"AGR-{cleanCode}-{datePart}-{randomHex}";
    }

    private static Dictionary<string, object> SafeParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }
}
