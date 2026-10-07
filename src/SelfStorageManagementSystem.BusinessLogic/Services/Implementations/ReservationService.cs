using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class ReservationService : IReservationService
{
    private readonly IReservationRepository _reservationRepository;
    private readonly IFacilityScopeService _facilityScopeService;
    private readonly ILogger<ReservationService> _logger;

    public ReservationService(
        IReservationRepository reservationRepository,
        IFacilityScopeService facilityScopeService,
        ILogger<ReservationService> logger)
    {
        _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
        _facilityScopeService = facilityScopeService ?? throw new ArgumentNullException(nameof(facilityScopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReservationDetailResponse> CreateReservationHoldAsync(
        long currentUserId,
        CreateReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Verify customer profile and active customer role
        var customerProfile = await _reservationRepository.GetCustomerProfileAsync(currentUserId, cancellationToken);
        if (customerProfile == null || customerProfile.user.status != UserStatusConstants.Active)
        {
            throw new ForbiddenException("Tài khoản chưa có hồ sơ khách hàng hợp lệ để thực hiện đặt chỗ.");
        }

        var hasCustomerRole = customerProfile.user.user_roleusers.Any(ur => ur.role?.code == RoleConstants.StorageCustomer);
        if (!hasCustomerRole)
        {
            throw new ForbiddenException("Tài khoản không có vai trò khách hàng (StorageCustomer) để thực hiện đặt chỗ.");
        }

        // 2. Verify active facility
        var facility = await _reservationRepository.GetFacilityAsync(request.FacilityId, cancellationToken);
        if (facility == null || facility.status != "active")
        {
            throw new NotFoundException($"Cơ sở kho với mã ID {request.FacilityId} không tồn tại hoặc hiện không hoạt động.");
        }

        // 3. Verify active unit type
        var unitType = await _reservationRepository.GetUnitTypeAsync(request.UnitTypeId, cancellationToken);
        if (unitType == null || !unitType.is_active)
        {
            throw new NotFoundException($"Loại kho với mã ID {request.UnitTypeId} không tồn tại hoặc hiện không áp dụng.");
        }

        // 4. Validate rental dates and duration per BR-RSV-02
        var facilityTz = ResolveTimeZone(facility.timezone);
        var todayInFacility = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, facilityTz));

        if (request.RentalStartDate < todayInFacility)
        {
            throw new BadRequestException($"Ngày bắt đầu thuê ({request.RentalStartDate:yyyy-MM-dd}) không được ở quá khứ (hôm nay theo giờ cơ sở là {todayInFacility:yyyy-MM-dd}).");
        }

        if (request.RentalEndDate <= request.RentalStartDate)
        {
            throw new BadRequestException("Ngày kết thúc thuê phải lớn hơn ngày bắt đầu thuê.");
        }

        var minEndDate = request.RentalStartDate.AddMonths(1);
        if (request.RentalEndDate < minEndDate)
        {
            throw new BadRequestException($"Thời hạn thuê tối thiểu là 1 tháng theo quy định BR-RSV-02 (ngày kết thúc tối thiểu: {minEndDate:yyyy-MM-dd}).");
        }

        var maxEndDate = request.RentalStartDate.AddMonths(12);
        if (request.RentalEndDate > maxEndDate)
        {
            throw new BadRequestException($"Thời hạn thuê tối đa là 12 tháng theo quy định BR-RSV-02 (ngày kết thúc tối đa: {maxEndDate:yyyy-MM-dd}).");
        }

        // 5. Query active facility rate
        var rate = await _reservationRepository.GetActiveFacilityRateAsync(
            request.FacilityId,
            request.UnitTypeId,
            request.RentalStartDate,
            cancellationToken);

        if (rate == null)
        {
            throw new BadRequestException($"Không tìm thấy biểu phí hợp lệ áp dụng cho loại kho '{unitType.name}' tại cơ sở '{facility.name}' vào ngày bắt đầu thuê ({request.RentalStartDate:yyyy-MM-dd}).");
        }

        // 6. Compute financial snapshot & quoted total
        // BR-FIN-01: deposit equals 100% of 1 month rent
        // Initial payment due at reservation checkout: Deposit + First month rent + Booking fee - Discount
        var monthlyRate = rate.monthly_rate;
        var depositAmount = rate.deposit_amount;
        var bookingFee = rate.booking_fee;
        var discount = 0m;
        var quotedTotal = depositAmount + monthlyRate + bookingFee - discount;

        var nowUtc = DateTimeOffset.UtcNow;
        var holdUntil = nowUtc.AddMinutes(15);
        var reservationCode = GenerateReservationCode(facility.code);

        var newReservation = new reservation
        {
            reservation_code = reservationCode,
            customer_id = customerProfile.user_id,
            facility_id = request.FacilityId,
            unit_type_id = request.UnitTypeId,
            facility_rate_id = rate.id,
            start_date = request.RentalStartDate,
            end_date = request.RentalEndDate,
            monthly_rate_snapshot = monthlyRate,
            deposit_snapshot = depositAmount,
            booking_fee_snapshot = bookingFee,
            discount_snapshot = discount,
            quoted_total = quotedTotal,
            hold_until = holdUntil,
            status = "pending",
            created_at = nowUtc,
            updated_at = nowUtc
        };

        try
        {
            var saved = await _reservationRepository.CreateReservationHoldWithLockAsync(
                newReservation,
                cancellationToken);

            _logger.LogInformation(
                "Created reservation hold {Code} (ID: {Id}) for customer {CustomerId} at facility {FacilityId}, unit type {UnitTypeId}. Hold until {HoldUntil}",
                saved.reservation_code,
                saved.id,
                saved.customer_id,
                saved.facility_id,
                saved.unit_type_id,
                saved.hold_until);

            // Re-fetch with all navigations loaded for response
            var loaded = await _reservationRepository.GetByIdWithDetailsAsync(saved.id, cancellationToken);
            return MapToDetailResponse(loaded ?? saved, nowUtc);
        }
        catch (InvalidOperationException ex) when (ex.Message == "CAPACITY_EXHAUSTED")
        {
            _logger.LogWarning(
                "Capacity exhausted for facility {FacilityId}, unit type {UnitTypeId} during [{Start} - {End}]",
                request.FacilityId,
                request.UnitTypeId,
                request.RentalStartDate,
                request.RentalEndDate);

            throw new ConflictException("Hiện tại cơ sở không còn đủ sức chứa khả dụng cho loại kho đã chọn trong khoảng thời gian này.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "CONCURRENCY_LOCK_TIMEOUT")
        {
            _logger.LogWarning("Lock acquisition timed out for facility {FacilityId}, unit type {UnitTypeId}", request.FacilityId, request.UnitTypeId);
            throw new ConflictException("Hệ thống đang bận xử lý giữ chỗ cho loại kho này. Vui lòng thử lại sau giây lát.");
        }
    }

    public async Task<ReservationDetailResponse> GetReservationByIdAsync(
        long currentUserId,
        IReadOnlyList<string> currentUserRoles,
        long reservationId,
        CancellationToken cancellationToken = default)
    {
        var reservation = await _reservationRepository.GetByIdWithDetailsAsync(reservationId, cancellationToken);
        if (reservation == null)
        {
            throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với mã ID {reservationId}.");
        }

        var nowUtc = DateTimeOffset.UtcNow;

        // 1. Authorization check MUST be performed before any database modification
        await VerifyAccessPermissionAsync(currentUserId, currentUserRoles, reservation, cancellationToken);

        // 2. Dynamic expiration check on read with atomic update and invoice cleanup
        if ((reservation.status == "pending" || reservation.status == "awaiting_deposit") && reservation.hold_until <= nowUtc)
        {
            var expired = await _reservationRepository.TryExpireSingleReservationIfOverdueAsync(reservation.id, nowUtc, cancellationToken);
            if (expired)
            {
                reservation.status = "expired";
                reservation.updated_at = nowUtc;
            }
            else
            {
                // Reload true state if update condition was not met (e.g. concurrent confirmation or cancellation)
                var reloaded = await _reservationRepository.GetByIdWithDetailsAsync(reservationId, cancellationToken);
                if (reloaded != null)
                {
                    reservation = reloaded;
                }
            }
        }

        return MapToDetailResponse(reservation, nowUtc);
    }

    public async Task<PagedResult<ReservationListItemResponse>> GetMyReservationsAsync(
        long currentUserId,
        GetMyReservationsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (items, totalCount) = await _reservationRepository.GetPagedByCustomerAsync(
            currentUserId,
            request.Status,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        var nowUtc = DateTimeOffset.UtcNow;
        var dtos = items.Select(r => MapToListItemResponse(r, nowUtc)).ToList();

        return new PagedResult<ReservationListItemResponse>(
            dtos,
            totalCount,
            request.PageNumber,
            request.PageSize);
    }

    public async Task<ReservationDetailResponse> CancelReservationAsync(
        long currentUserId,
        IReadOnlyList<string> currentUserRoles,
        long reservationId,
        CancelReservationRequest? request,
        CancellationToken cancellationToken = default)
    {
        var reservation = await _reservationRepository.GetByIdWithDetailsAsync(reservationId, cancellationToken);
        if (reservation == null)
        {
            throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với mã ID {reservationId}.");
        }

        // Verify authorization
        await VerifyAccessPermissionAsync(currentUserId, currentUserRoles, reservation, cancellationToken);

        var nowUtc = DateTimeOffset.UtcNow;

        // If already cancelled, return idempotent success
        if (reservation.status == "cancelled")
        {
            return MapToDetailResponse(reservation, nowUtc);
        }

        // If confirmed, reject cancellation in Function 3 (requires cancellation & refund workflow in future module)
        if (reservation.status == "confirmed")
        {
            throw new ConflictException("Đơn đặt chỗ đã được xác nhận thanh toán (Confirmed). Để hủy đơn và xử lý hoàn tiền theo chính sách, vui lòng liên hệ nhân viên hoặc quản lý cơ sở.");
        }

        // Check if expired
        if (reservation.status == "expired" ||
            ((reservation.status == "pending" || reservation.status == "awaiting_deposit") && reservation.hold_until <= nowUtc))
        {
            await _reservationRepository.TryExpireSingleReservationIfOverdueAsync(reservation.id, nowUtc, cancellationToken);
            throw new ConflictException("Đơn đặt chỗ đã hết hạn giữ chỗ (Expired), không thể thực hiện hủy.");
        }

        // Check un-cancellable terminal statuses
        if (reservation.status is "checked_in" or "converted" or "completed" or "no_show")
        {
            throw new ConflictException($"Không thể hủy đơn đặt chỗ ở trạng thái '{reservation.status}'.");
        }

        reservation cancelled;
        try
        {
            cancelled = await _reservationRepository.CancelReservationAsync(
                reservationId,
                request?.Reason,
                nowUtc,
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message == "CANNOT_CANCEL_CONFIRMED")
        {
            throw new ConflictException("Đơn đặt chỗ đã được xác nhận thanh toán (Confirmed). Để hủy đơn và xử lý hoàn tiền theo chính sách, vui lòng liên hệ nhân viên hoặc quản lý cơ sở.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "CANNOT_CANCEL_EXPIRED")
        {
            throw new ConflictException("Đơn đặt chỗ đã hết hạn giữ chỗ (Expired), không thể thực hiện hủy.");
        }

        _logger.LogInformation(
            "Reservation {Code} (ID: {Id}) cancelled by user {UserId}. Reason: {Reason}",
            cancelled.reservation_code,
            cancelled.id,
            currentUserId,
            cancelled.cancellation_reason);

        // Keep navigations from previously loaded entity
        cancelled.facility = reservation.facility;
        cancelled.unit_type = reservation.unit_type;
        cancelled.facility_rate = reservation.facility_rate;
        cancelled.customer = reservation.customer;

        return MapToDetailResponse(cancelled, nowUtc);
    }

    public async Task<int> ExpireOverdueHoldsAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var count = await _reservationRepository.ExpireOverdueReservationHoldsAsync(nowUtc, cancellationToken);
        if (count > 0)
        {
            _logger.LogInformation("Background worker expired {Count} overdue reservation holds at {NowUtc}", count, nowUtc);
        }
        return count;
    }

    private async Task VerifyAccessPermissionAsync(
        long currentUserId,
        IReadOnlyList<string> currentUserRoles,
        reservation r,
        CancellationToken cancellationToken)
    {
        // 1. Owner can always access
        if (r.customer_id == currentUserId)
        {
            return;
        }

        // 2. Admin & Business Operations Manager have system-wide access
        if (currentUserRoles.Contains(RoleConstants.SystemAdministrator) ||
            currentUserRoles.Contains(RoleConstants.BusinessOperationsManager))
        {
            return;
        }

        // 3. Facility Manager & Staff can access if assigned to this facility
        if (currentUserRoles.Contains(RoleConstants.FacilityManager) ||
            currentUserRoles.Contains(RoleConstants.FacilityStaff))
        {
            var hasAccess = await _facilityScopeService.HasAccessToFacilityAsync(
                currentUserId,
                r.facility_id,
                RoleConstants.FacilityStaff,
                cancellationToken);

            if (hasAccess)
            {
                return;
            }
        }

        throw new ForbiddenException("Bạn không có quyền truy cập hoặc thao tác trên đơn đặt chỗ này.");
    }

    private static ReservationDetailResponse MapToDetailResponse(reservation r, DateTimeOffset nowUtc)
    {
        var isPendingHold = r.status == "pending" || r.status == "awaiting_deposit";
        var isHoldActive = isPendingHold && r.hold_until > nowUtc;
        var remainingSeconds = isHoldActive ? Math.Max(0, (int)(r.hold_until - nowUtc).TotalSeconds) : 0;
        var canCancel = isPendingHold && r.hold_until > nowUtc;

        var rentalMonths = CalculateRentalMonths(r.start_date, r.end_date);

        return new ReservationDetailResponse
        {
            Id = r.id,
            ReservationCode = r.reservation_code,
            CustomerId = r.customer_id,
            CustomerName = r.customer?.full_name ?? "Khách hàng",
            CustomerEmail = r.customer?.user?.email,
            FacilityId = r.facility_id,
            FacilityName = r.facility?.name ?? string.Empty,
            FacilityCode = r.facility?.code ?? string.Empty,
            FacilityAddress = $"{r.facility?.address_line}, {r.facility?.district}, {r.facility?.city}".Trim(',', ' '),
            UnitTypeId = r.unit_type_id,
            UnitTypeCode = r.unit_type?.code ?? string.Empty,
            UnitTypeName = r.unit_type?.name ?? string.Empty,
            UnitTypeAreaM2 = r.unit_type?.area_m2 ?? 0,
            ClimateControlled = r.unit_type?.climate_controlled ?? false,
            StartDate = r.start_date,
            EndDate = r.end_date,
            RentalMonths = rentalMonths,
            MonthlyRateSnapshot = r.monthly_rate_snapshot,
            DepositSnapshot = r.deposit_snapshot,
            BookingFeeSnapshot = r.booking_fee_snapshot,
            DiscountSnapshot = r.discount_snapshot,
            QuotedTotal = r.quoted_total,
            Status = r.status,
            StatusDisplayName = MapStatusDisplayName(r.status, isHoldActive),
            HoldUntil = r.hold_until,
            ServerNow = nowUtc,
            ConfirmedAt = r.confirmed_at,
            CancelledAt = r.cancelled_at,
            CancellationReason = r.cancellation_reason,
            CreatedAt = r.created_at,
            UpdatedAt = r.updated_at,
            IsHoldActive = isHoldActive,
            RemainingSeconds = remainingSeconds,
            CanCancel = canCancel
        };
    }

    private static ReservationListItemResponse MapToListItemResponse(reservation r, DateTimeOffset nowUtc)
    {
        var isPendingHold = r.status == "pending" || r.status == "awaiting_deposit";
        var isHoldActive = isPendingHold && r.hold_until > nowUtc;
        var remainingSeconds = isHoldActive ? Math.Max(0, (int)(r.hold_until - nowUtc).TotalSeconds) : 0;
        var canCancel = isPendingHold && r.hold_until > nowUtc;

        return new ReservationListItemResponse
        {
            Id = r.id,
            ReservationCode = r.reservation_code,
            FacilityId = r.facility_id,
            FacilityName = r.facility?.name ?? string.Empty,
            UnitTypeId = r.unit_type_id,
            UnitTypeName = r.unit_type?.name ?? string.Empty,
            StartDate = r.start_date,
            EndDate = r.end_date,
            MonthlyRateSnapshot = r.monthly_rate_snapshot,
            DepositSnapshot = r.deposit_snapshot,
            QuotedTotal = r.quoted_total,
            Status = r.status,
            StatusDisplayName = MapStatusDisplayName(r.status, isHoldActive),
            HoldUntil = r.hold_until,
            ServerNow = nowUtc,
            CreatedAt = r.created_at,
            IsHoldActive = isHoldActive,
            RemainingSeconds = remainingSeconds,
            CanCancel = canCancel
        };
    }

    private static string MapStatusDisplayName(string status, bool isHoldActive) => status switch
    {
        "pending" => isHoldActive ? "Đang giữ chỗ (Chờ thanh toán)" : "Hết hạn giữ chỗ",
        "awaiting_deposit" => isHoldActive ? "Chờ cọc" : "Hết hạn giữ chỗ",
        "confirmed" => "Đã xác nhận",
        "checked_in" => "Đã nhận kho",
        "converted" => "Đã tạo hợp đồng",
        "completed" => "Đã hoàn thành",
        "cancelled" => "Đã hủy",
        "expired" => "Hết hạn",
        "no_show" => "Khách không đến",
        _ => status
    };

    private static int CalculateRentalMonths(DateOnly start, DateOnly end)
    {
        var months = (end.Year - start.Year) * 12 + (end.Month - start.Month);
        if (end.Day < start.Day) months--;
        return Math.Max(1, months);
    }

    private static string GenerateReservationCode(string facilityCode)
    {
        var cleanCode = string.IsNullOrWhiteSpace(facilityCode) ? "SS" : facilityCode.Trim().ToUpper();
        var datePart = DateTime.UtcNow.ToString("yyyyMMdd");
        var randomHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToUpper();
        return $"RES-{cleanCode}-{datePart}-{randomHex}";
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows fallback for common IANA names
            if (timezoneId.Equals("Asia/Ho_Chi_Minh", StringComparison.OrdinalIgnoreCase) ||
                timezoneId.Equals("Asia/Bangkok", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                }
                catch
                {
                    return TimeZoneInfo.Utc;
                }
            }
            return TimeZoneInfo.Utc;
        }
    }
}
