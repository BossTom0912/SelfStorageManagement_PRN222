using Microsoft.Extensions.Configuration;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Common;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class CheckInService : ICheckInService
{
    private readonly ICheckInRepository _checkInRepository;
    private readonly IFacilityScopeService _facilityScopeService;
    private readonly IConfiguration _configuration;

    public CheckInService(
        ICheckInRepository checkInRepository,
        IFacilityScopeService facilityScopeService,
        IConfiguration configuration)
    {
        _checkInRepository = checkInRepository ?? throw new ArgumentNullException(nameof(checkInRepository));
        _facilityScopeService = facilityScopeService ?? throw new ArgumentNullException(nameof(facilityScopeService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task<CheckInSearchResponse> SearchCheckInAsync(
        string code,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new BadRequestException("Mã tra cứu đặt chỗ hoặc hợp đồng không được để trống.");
        }

        var res = await _checkInRepository.GetReservationForCheckInAsync(code, cancellationToken);
        if (res == null)
        {
            throw new NotFoundException($"Không tìm thấy thông tin đặt chỗ hoặc hợp đồng với mã '{code.Trim()}'.");
        }

        // Check staff facility counter access
        var hasAccess = await _facilityScopeService.HasCheckInCounterAccessAsync(currentUserId, res.facility_id, cancellationToken);
        if (!hasAccess)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên cơ sở của đơn đặt chỗ này.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var facilityToday = FacilityTimeHelper.GetFacilityToday(res.facility.timezone, nowUtc);
        var isDateEligible = facilityToday >= res.start_date && facilityToday < res.end_date;

        var reasons = new List<string>();

        if (facilityToday < res.start_date)
        {
            reasons.Add($"Chưa đến ngày bắt đầu thuê ({res.start_date:dd/MM/yyyy}). Chưa hỗ trợ nhận kho sớm.");
        }
        else if (facilityToday >= res.end_date)
        {
            reasons.Add($"Đơn đặt chỗ đã quá hạn thuê ({res.end_date:dd/MM/yyyy}).");
        }

        if (res.status != "confirmed")
        {
            reasons.Add($"Đơn đặt chỗ đang ở trạng thái '{res.status}', cần 'confirmed' để thực hiện check-in.");
        }

        var agreement = res.rental_agreement;
        if (agreement == null)
        {
            reasons.Add("Chưa tạo hợp đồng thuê cho đơn đặt chỗ này.");
        }
        else if (agreement.status != "scheduled" && agreement.status != "active")
        {
            reasons.Add($"Hợp đồng đang ở trạng thái '{agreement.status}'.");
        }

        var invoice = agreement?.invoices
            .FirstOrDefault(inv => inv.status == "paid" && inv.paid_amount == inv.total_amount && inv.total_amount > 0);

        var isFullyPaid = invoice != null;
        if (!isFullyPaid)
        {
            reasons.Add("Hóa đơn thanh toán kỳ đầu chưa được thanh toán đầy đủ.");
        }

        var latestVerif = res.identity_verifications
            .OrderByDescending(iv => iv.verified_at)
            .ThenByDescending(iv => iv.id)
            .FirstOrDefault();

        var isVerified = latestVerif?.result == "verified";
        if (!isVerified)
        {
            reasons.Add("Khách hàng chưa được xác minh giấy tờ thành công tại quầy.");
        }

        var activeAlloc = agreement?.unit_allocations
            .FirstOrDefault(ua => ua.status == "active");

        if (activeAlloc == null)
        {
            reasons.Add("Chưa gán ô kho vật lý.");
        }

        var latestInsp = res.inspections
            .Where(i => activeAlloc != null && i.storage_unit_id == activeAlloc.storage_unit_id && i.created_at >= activeAlloc.created_at)
            .OrderByDescending(i => i.created_at)
            .FirstOrDefault();

        var inspectionPassed = latestInsp != null && latestInsp.status == "completed" && latestInsp.overall_condition is ("good" or "acceptable");
        if (activeAlloc != null && !inspectionPassed)
        {
            reasons.Add("Chưa hoàn tất biên bản kiểm tra ô kho đạt yêu cầu.");
        }

        var handover = agreement?.handover_records
            .OrderByDescending(h => h.created_at)
            .FirstOrDefault();

        var canProceed = agreement != null && agreement.status == "scheduled" && reasons.Count == 0 && handover == null;

        var activeCredential = agreement?.access_credentials
            .FirstOrDefault(c => c.status == "active");

        return new CheckInSearchResponse
        {
            ReservationId = res.id,
            ReservationCode = res.reservation_code,
            ReservationStatus = res.status,
            StartDate = res.start_date,
            EndDate = res.end_date,
            CreatedAt = res.created_at,

            CustomerId = res.customer_id,
            CustomerName = res.customer.full_name,
            CustomerEmail = res.customer.user?.email,
            CustomerPhone = res.customer.user?.phone_number,
            MaskedIdentityNumber = IdentityNumberMasker.Mask(res.customer.identity_number),
            HasIdentityNumber = !string.IsNullOrWhiteSpace(res.customer.identity_number),

            FacilityId = res.facility_id,
            FacilityCode = res.facility.code,
            FacilityName = res.facility.name,
            FacilityTimezone = res.facility.timezone,
            UnitTypeId = res.unit_type_id,
            UnitTypeName = res.unit_type.name,
            AreaM2 = res.unit_type.area_m2 ?? 0m,
            ClimateControlled = res.unit_type.climate_controlled,

            AgreementId = agreement?.id,
            AgreementNo = agreement?.agreement_no,
            AgreementStatus = agreement?.status,
            MonthlyRateSnapshot = agreement?.monthly_rate_snapshot ?? res.monthly_rate_snapshot,
            DepositBalance = agreement?.deposit_balance ?? res.deposit_snapshot,

            CheckoutInvoiceId = invoice?.id,
            CheckoutInvoiceNo = invoice?.invoice_no,
            InvoiceStatus = invoice?.status,
            TotalAmount = invoice?.total_amount ?? 0,
            PaidAmount = invoice?.paid_amount ?? 0,
            IsFullyPaid = isFullyPaid,

            FacilityToday = facilityToday,
            IsDateEligible = isDateEligible,
            CanProceedToCheckIn = canProceed,
            ReasonsCannotProceed = reasons,

            LatestVerification = latestVerif == null ? null : new CheckInVerificationSummaryDto
            {
                VerificationId = latestVerif.id,
                VerificationMethod = latestVerif.verification_method,
                Result = latestVerif.result,
                VerifiedAt = latestVerif.verified_at,
                VerifiedBy = latestVerif.verified_by,
                VerifiedByName = latestVerif.verified_byNavigation?.full_name
            },

            ActiveAllocation = activeAlloc == null ? null : new CheckInAllocationSummaryDto
            {
                AllocationId = activeAlloc.id,
                StorageUnitId = activeAlloc.storage_unit_id,
                UnitCode = activeAlloc.storage_unit?.unit_code ?? string.Empty,
                AllocationKind = activeAlloc.allocation_kind,
                Status = activeAlloc.status,
                CreatedAt = activeAlloc.created_at
            },

            LatestInspection = latestInsp == null ? null : new CheckInInspectionSummaryDto
            {
                InspectionId = latestInsp.id,
                Status = latestInsp.status,
                OverallCondition = latestInsp.overall_condition,
                InspectedAt = latestInsp.inspected_at,
                Summary = latestInsp.summary,
                HasPassed = inspectionPassed
            },

            HandoverRecord = handover == null ? null : new CheckInHandoverSummaryDto
            {
                HandoverRecordId = handover.id,
                HandoverType = handover.handover_type,
                HandedAt = handover.created_at,
                IsCompleted = true
            },

            ActiveCredential = activeCredential == null ? null : new CheckInCredentialSummaryDto
            {
                CredentialId = activeCredential.id,
                CredentialType = activeCredential.credential_type,
                Status = activeCredential.status,
                IssuedAt = activeCredential.issued_at,
                ExpiresAt = activeCredential.expires_at
            }
        };
    }

    public async Task<List<EligibleUnitResponse>> GetEligibleUnitsAsync(
        long reservationId,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        var res = await _checkInRepository.GetByIdAsync([reservationId], cancellationToken);
        if (res == null)
        {
            throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với ID {reservationId}.");
        }

        var hasAccess = await _facilityScopeService.HasCheckInCounterAccessAsync(currentUserId, res.facility_id, cancellationToken);
        if (!hasAccess)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên cơ sở của đơn đặt chỗ này.");
        }

        var units = await _checkInRepository.GetEligibleUnitsAsync(
            res.facility_id,
            res.unit_type_id,
            res.start_date,
            res.end_date,
            cancellationToken);

        return units.Select(u => new EligibleUnitResponse
        {
            UnitId = u.id,
            UnitCode = u.unit_code,
            FacilityId = u.facility_id,
            UnitTypeId = u.unit_type_id,
            UnitTypeName = u.unit_type.name,
            AreaM2 = u.unit_type.area_m2 ?? 0m,
            ClimateControlled = u.unit_type.climate_controlled,
            PhysicalStatus = u.physical_status,
            AreaName = u.area?.name,
            FloorLabel = u.floor_label,
            ZoneLabel = u.zone_label
        }).ToList();
    }

    public async Task<IdentityVerificationResponse> VerifyIdentityAsync(
        long reservationId,
        VerifyIdentityRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var method = request.VerificationMethod?.Trim().ToLowerInvariant();
        if (method is not ("government_id" or "passport"))
        {
            throw new BadRequestException("Phương thức xác minh chỉ hỗ trợ 'government_id' hoặc 'passport'.");
        }

        var res = await _checkInRepository.GetReservationForCheckInAsync(reservationId.ToString(), cancellationToken);
        if (res == null)
        {
            throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với ID {reservationId}.");
        }

        var hasAccess = await _facilityScopeService.HasCheckInCounterAccessAsync(currentUserId, res.facility_id, cancellationToken);
        if (!hasAccess)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên cơ sở của đơn đặt chỗ này.");
        }

        if (request.IsConfirmedMatch && string.IsNullOrWhiteSpace(request.DocumentNumber) && string.IsNullOrWhiteSpace(res.customer.identity_number))
        {
            throw new BadRequestException("Hồ sơ khách hàng chưa có số giấy tờ. Vui lòng nhập số giấy tờ để bổ sung và xác minh.");
        }

        try
        {
            var outcome = await _checkInRepository.VerifyIdentityWithLockAsync(
                reservationId,
                currentUserId,
                method,
                request.DocumentNumber,
                request.IsConfirmedMatch,
                request.Notes,
                (perm, facId) => ValidateStaffCounterAccess(perm, facId),
                cancellationToken);

            if (outcome.IsMismatch)
            {
                throw new BadRequestException("Số giấy tờ xuất trình không khớp với số giấy tờ đã lưu trong hồ sơ khách hàng.");
            }

            var entity = outcome.Verification;
            var staffProfile = await _facilityScopeService.GetActiveAssignmentsForUserAsync(currentUserId, cancellationToken);

            return new IdentityVerificationResponse
            {
                VerificationId = entity.id,
                ReservationId = entity.reservation_id,
                VerificationMethod = entity.verification_method,
                Result = entity.result,
                VerifiedBy = entity.verified_by,
                VerifiedByName = staffProfile.FirstOrDefault()?.EmployeeId.ToString(),
                VerifiedAt = entity.verified_at,
                MaskedIdentityNumber = IdentityNumberMasker.Mask(request.DocumentNumber ?? res.customer.identity_number),
                Notes = entity.notes
            };
        }
        catch (Exception ex)
        {
            throw MapRepositoryException(ex);
        }
    }

    public async Task<UnitAllocationResponse> AssignUnitAsync(
        long reservationId,
        AssignUnitRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.StorageUnitId <= 0)
        {
            throw new BadRequestException("Vui lòng chọn ô kho hợp lệ để gán.");
        }

        var res = await _checkInRepository.GetByIdAsync([reservationId], cancellationToken);
        if (res == null)
        {
            throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với ID {reservationId}.");
        }

        var hasAccess = await _facilityScopeService.HasCheckInCounterAccessAsync(currentUserId, res.facility_id, cancellationToken);
        if (!hasAccess)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên cơ sở của đơn đặt chỗ này.");
        }

        try
        {
            var allocation = await _checkInRepository.AssignUnitWithLockAsync(
                reservationId,
                request.StorageUnitId,
                currentUserId,
                (perm, facId) => ValidateStaffCounterAccess(perm, facId),
                cancellationToken);

            return new UnitAllocationResponse
            {
                AllocationId = allocation.id,
                AgreementId = allocation.agreement_id,
                ReservationId = allocation.reservation_id,
                StorageUnitId = allocation.storage_unit_id,
                UnitCode = allocation.storage_unit?.unit_code ?? string.Empty,
                AllocationKind = allocation.allocation_kind,
                StartDate = allocation.allocation_start_date,
                EndDate = allocation.allocation_end_date,
                Status = allocation.status,
                CreatedAt = allocation.created_at,
                EndedAt = allocation.ended_at,
                Reason = allocation.reason
            };
        }
        catch (Exception ex)
        {
            throw MapRepositoryException(ex);
        }
    }

    public async Task<UnitAllocationResponse> ReleaseUnitAsync(
        long reservationId,
        ReleaseUnitRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CurrentAllocationId <= 0)
        {
            throw new BadRequestException("Vui lòng cung cấp ID phân bổ ô kho hiện tại.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BadRequestException("Lý do giải phóng ô kho không được để trống.");
        }

        var res = await _checkInRepository.GetByIdAsync([reservationId], cancellationToken);
        if (res == null)
        {
            throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với ID {reservationId}.");
        }

        var hasAccess = await _facilityScopeService.HasCheckInCounterAccessAsync(currentUserId, res.facility_id, cancellationToken);
        if (!hasAccess)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên cơ sở của đơn đặt chỗ này.");
        }

        try
        {
            var allocation = await _checkInRepository.ReleaseUnitWithLockAsync(
                reservationId,
                request.CurrentAllocationId,
                request.Reason,
                currentUserId,
                (perm, facId) => ValidateStaffCounterAccess(perm, facId),
                cancellationToken);

            return new UnitAllocationResponse
            {
                AllocationId = allocation.id,
                AgreementId = allocation.agreement_id,
                ReservationId = allocation.reservation_id,
                StorageUnitId = allocation.storage_unit_id,
                UnitCode = allocation.storage_unit?.unit_code ?? string.Empty,
                AllocationKind = allocation.allocation_kind,
                StartDate = allocation.allocation_start_date,
                EndDate = allocation.allocation_end_date,
                Status = allocation.status,
                CreatedAt = allocation.created_at,
                EndedAt = allocation.ended_at,
                Reason = allocation.reason
            };
        }
        catch (Exception ex)
        {
            throw MapRepositoryException(ex);
        }
    }

    public async Task<InspectionResponse> PerformInspectionAsync(
        long reservationId,
        UnitInspectionRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CurrentAllocationId <= 0)
        {
            throw new BadRequestException("Vui lòng cung cấp ID phân bổ ô kho hiện tại.");
        }

        if (request.Items == null || request.Items.Count < 4)
        {
            throw new BadRequestException("Checklist kiểm tra phải bao gồm đầy đủ 4 hạng mục: cửa, khóa, đèn và vệ sinh.");
        }

        var res = await _checkInRepository.GetByIdAsync([reservationId], cancellationToken);
        if (res == null)
        {
            throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với ID {reservationId}.");
        }

        var hasAccess = await _facilityScopeService.HasCheckInCounterAccessAsync(currentUserId, res.facility_id, cancellationToken);
        if (!hasAccess)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên cơ sở của đơn đặt chỗ này.");
        }

        var itemsTuple = request.Items
            .Select(it => (it.ItemName, it.Condition, it.Notes))
            .ToList();

        try
        {
            var inspection = await _checkInRepository.PerformInspectionWithLockAsync(
                reservationId,
                request.CurrentAllocationId,
                currentUserId,
                itemsTuple,
                request.OverallSummary,
                request.IsUnsafe,
                (perm, facId) => ValidateStaffCounterAccess(perm, facId),
                cancellationToken);

            return new InspectionResponse
            {
                InspectionId = inspection.id,
                AgreementId = inspection.agreement_id,
                StorageUnitId = inspection.storage_unit_id,
                UnitCode = inspection.storage_unit?.unit_code,
                InspectionType = inspection.inspection_type,
                Status = inspection.status,
                OverallCondition = inspection.overall_condition,
                Summary = inspection.summary,
                InspectedAt = inspection.inspected_at,
                UnitReleasedDueToFailure = inspection.status == "failed",
                Items = inspection.inspection_items.Select(it => new InspectionItemResponse
                {
                    Id = it.id,
                    ItemName = it.item_name,
                    Condition = it.condition,
                    Notes = it.notes,
                    ChargeAmount = it.charge_amount
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            throw MapRepositoryException(ex);
        }
    }

    public async Task<HandoverCompleteResponse> CompleteCheckInAsync(
        long reservationId,
        CompleteCheckInRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CurrentAllocationId <= 0)
        {
            throw new BadRequestException("Vui lòng cung cấp ID phân bổ ô kho hiện tại.");
        }

        if (request.CurrentInspectionId <= 0)
        {
            throw new BadRequestException("Vui lòng cung cấp ID biên bản kiểm tra ô kho hiện tại.");
        }

        if (!request.CustomerAcknowledged)
        {
            throw new BadRequestException("Khách hàng chưa xác nhận đồng ý nhận bàn giao tại quầy.");
        }

        var res = await _checkInRepository.GetByIdAsync([reservationId], cancellationToken);
        if (res == null)
        {
            throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với ID {reservationId}.");
        }

        var hasAccess = await _facilityScopeService.HasCheckInCounterAccessAsync(currentUserId, res.facility_id, cancellationToken);
        if (!hasAccess)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên cơ sở của đơn đặt chỗ này.");
        }

        var pinSecret = _configuration["CheckIn:PinHmacSecret"]
                        ?? _configuration["CheckIn__PinHmacSecret"]
                        ?? Environment.GetEnvironmentVariable("CHECKIN_PIN_HMAC_SECRET");

        if (string.IsNullOrWhiteSpace(pinSecret))
        {
            throw new InvalidOperationException("Khóa bí mật HMAC cho PIN chưa được cấu hình. Vui lòng thiết lập 'CheckIn:PinHmacSecret' hoặc biến môi trường 'CHECKIN_PIN_HMAC_SECRET'.");
        }

        try
        {
            var result = await _checkInRepository.CompleteCheckInWithLockAsync(
                reservationId,
                request.CurrentAllocationId,
                request.CurrentInspectionId,
                request.CustomerAcknowledged,
                request.Notes,
                currentUserId,
                pinSecret,
                (perm, facId) => ValidateStaffCounterAccess(perm, facId),
                cancellationToken);

            var handover = result.Handover;

            return new HandoverCompleteResponse
            {
                HandoverRecordId = handover.id,
                AgreementId = handover.agreement_id,
                AgreementNo = handover.agreement?.agreement_no ?? string.Empty,
                UnitAllocationId = handover.unit_allocation_id,
                StorageUnitId = handover.unit_allocation?.storage_unit_id ?? 0,
                UnitCode = handover.unit_allocation?.storage_unit?.unit_code ?? string.Empty,
                HandedAt = handover.created_at,
                IsAlreadyCompleted = result.IsAlreadyCompleted,
                Pin = result.Pin,
                CredentialId = result.CredentialId,
                CredentialExpiresAt = result.CredentialExpiresAt,
                NotificationId = result.NotificationId
            };
        }
        catch (Exception ex)
        {
            throw MapRepositoryException(ex);
        }
    }

    public async Task<PinReissueResponse> ReissuePinAsync(
        long reservationId,
        ReissuePinRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CurrentCredentialId <= 0)
        {
            throw new BadRequestException("Vui lòng cung cấp ID mã truy cập hiện tại.");
        }

        if (string.IsNullOrWhiteSpace(request.DocumentNumber))
        {
            throw new BadRequestException("Số giấy tờ xuất trình để đối chiếu không được để trống.");
        }

        if (!request.IsConfirmedMatch)
        {
            throw new BadRequestException("Nhân viên chưa xác nhận đối chiếu người nhận với hồ sơ khách hàng.");
        }

        var res = await _checkInRepository.GetByIdAsync([reservationId], cancellationToken);
        if (res == null)
        {
            throw new NotFoundException($"Không tìm thấy đơn đặt chỗ với ID {reservationId}.");
        }

        var hasAccess = await _facilityScopeService.HasCheckInCounterAccessAsync(currentUserId, res.facility_id, cancellationToken);
        if (!hasAccess)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên cơ sở của đơn đặt chỗ này.");
        }

        var pinSecret = _configuration["CheckIn:PinHmacSecret"]
                        ?? _configuration["CheckIn__PinHmacSecret"]
                        ?? Environment.GetEnvironmentVariable("CHECKIN_PIN_HMAC_SECRET");

        if (string.IsNullOrWhiteSpace(pinSecret))
        {
            throw new InvalidOperationException("Khóa bí mật HMAC cho PIN chưa được cấu hình. Vui lòng thiết lập 'CheckIn:PinHmacSecret' hoặc biến môi trường 'CHECKIN_PIN_HMAC_SECRET'.");
        }

        try
        {
            var result = await _checkInRepository.ReissuePinWithLockAsync(
                reservationId,
                request.CurrentCredentialId,
                request.DocumentNumber,
                request.IsConfirmedMatch,
                request.Reason,
                currentUserId,
                pinSecret,
                (perm, facId) => ValidateStaffCounterAccess(perm, facId),
                cancellationToken);

            return new PinReissueResponse
            {
                AgreementId = result.AgreementId,
                OldCredentialId = result.OldCredentialId,
                NewCredentialId = result.NewCredentialId,
                Pin = result.Pin,
                IssuedAt = result.IssuedAt,
                ExpiresAt = result.ExpiresAt
            };
        }
        catch (Exception ex)
        {
            throw MapRepositoryException(ex);
        }
    }

    public async Task<PagedResult<CustomerUnitListItemResponse>> GetCustomerUnitsAsync(
        long currentUserId,
        PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = pagedRequest.PageNumber < 1 ? 1 : pagedRequest.PageNumber;
        var pageSize = pagedRequest.PageSize < 1 ? 10 : Math.Min(100, pagedRequest.PageSize);

        var (items, total) = await _checkInRepository.GetCustomerAgreementsAsync(
            currentUserId,
            pageNumber,
            pageSize,
            cancellationToken);

        var dtos = items.Select(a =>
        {
            var activeAlloc = a.unit_allocations.FirstOrDefault(ua => ua.status == "active");
            var unit = activeAlloc?.storage_unit;

            return new CustomerUnitListItemResponse
            {
                AgreementId = a.id,
                AgreementNo = a.agreement_no,
                AgreementStatus = a.status,
                StorageUnitId = unit?.id,
                UnitCode = unit?.unit_code,
                FacilityId = a.facility_id,
                FacilityName = a.facility.name,
                UnitTypeId = a.reservation.unit_type_id,
                UnitTypeName = a.reservation.unit_type.name,
                AreaM2 = a.reservation.unit_type.area_m2 ?? 0m,
                StartDate = a.start_date,
                EndDate = a.end_date,
                CheckedInAt = a.checked_in_at,
                IsActive = a.status == "active"
            };
        }).ToList();

        return new PagedResult<CustomerUnitListItemResponse>(dtos, total, pageNumber, pageSize);
    }

    public async Task<CustomerHandoverResponse> GetCustomerHandoverAsync(
        long currentUserId,
        long agreementId,
        CancellationToken cancellationToken = default)
    {
        var agreement = await _checkInRepository.GetCustomerHandoverAgreementAsync(
            currentUserId,
            agreementId,
            cancellationToken);

        if (agreement == null)
        {
            throw new NotFoundException($"Không tìm thấy hợp đồng thuê với ID {agreementId} hoặc hợp đồng không thuộc quyền sở hữu của bạn.");
        }

        var handover = agreement.handover_records
            .OrderByDescending(h => h.created_at)
            .FirstOrDefault();

        var activeAlloc = agreement.unit_allocations
            .FirstOrDefault(ua => ua.status == "active");

        var latestInsp = agreement.inspections
            .OrderByDescending(i => i.created_at)
            .FirstOrDefault();

        var activeCred = agreement.access_credentials
            .FirstOrDefault(c => c.status == "active");

        return new CustomerHandoverResponse
        {
            AgreementId = agreement.id,
            AgreementNo = agreement.agreement_no,
            AgreementStatus = agreement.status,
            StorageUnitCode = activeAlloc?.storage_unit?.unit_code,
            FacilityName = agreement.facility.name,
            HandoverRecordId = handover?.id ?? 0,
            HandoverType = handover?.handover_type ?? "check_in",
            HandedAt = handover?.created_at,
            CheckedInAt = agreement.checked_in_at,
            Notes = handover?.notes,
            InspectionCondition = latestInsp?.overall_condition,
            CredentialType = activeCred?.credential_type,
            CredentialStatus = activeCred?.status,
            CredentialIssuedAt = activeCred?.issued_at,
            CredentialExpiresAt = activeCred?.expires_at
        };
    }

    public async Task<PagedResult<CustomerNotificationResponse>> GetCustomerNotificationsAsync(
        long currentUserId,
        PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = pagedRequest.PageNumber < 1 ? 1 : pagedRequest.PageNumber;
        var pageSize = pagedRequest.PageSize < 1 ? 10 : Math.Min(100, pagedRequest.PageSize);

        var (items, total) = await _checkInRepository.GetCustomerNotificationsAsync(
            currentUserId,
            pageNumber,
            pageSize,
            cancellationToken);

        var dtos = items.Select(n => new CustomerNotificationResponse
        {
            Id = n.id,
            Channel = n.channel,
            TemplateCode = n.template_code,
            Subject = n.subject,
            Payload = n.payload,
            Status = n.status,
            SentAt = n.sent_at,
            CreatedAt = n.created_at
        }).ToList();

        return new PagedResult<CustomerNotificationResponse>(dtos, total, pageNumber, pageSize);
    }

    private static Exception MapRepositoryException(Exception ex)
    {
        if (ex is KeyNotFoundException knf)
        {
            return knf.Message switch
            {
                "RESERVATION_NOT_FOUND" => new NotFoundException("Không tìm thấy đơn đặt chỗ được yêu cầu."),
                "STORAGE_UNIT_NOT_FOUND" => new NotFoundException("Không tìm thấy ô kho được yêu cầu."),
                "AGREEMENT_NOT_FOUND" => new NotFoundException("Không tìm thấy hợp đồng thuê được yêu cầu."),
                _ => new NotFoundException(knf.Message)
            };
        }

        if (ex is InvalidOperationException ioe)
        {
            return ioe.Message switch
            {
                "DOCUMENT_NUMBER_MISMATCH" => new BadRequestException("Số giấy tờ xuất trình không khớp với số giấy tờ đã lưu trong hồ sơ khách hàng."),
                "RESERVATION_NOT_CONFIRMED" => new BadRequestException("Đơn đặt chỗ không ở trạng thái 'confirmed' để thực hiện check-in."),
                "AGREEMENT_NOT_SCHEDULED" => new BadRequestException("Hợp đồng thuê không ở trạng thái 'scheduled'."),
                "AGREEMENT_NOT_ACTIVE" => new BadRequestException("Hợp đồng không ở trạng thái 'active'. Chỉ cấp lại PIN cho hợp đồng đang hoạt động."),
                "RENTAL_PERIOD_NOT_STARTED" => new BadRequestException("Chưa đến ngày bắt đầu thời hạn thuê. Chưa hỗ trợ nhận kho sớm."),
                "RENTAL_PERIOD_EXPIRED" => new BadRequestException("Đơn đặt chỗ hoặc hợp đồng đã quá hạn thuê."),
                "RENTAL_PERIOD_INVALID" => new BadRequestException("Thời điểm hiện tại nằm ngoài kỳ hạn thuê của hợp đồng."),
                "INVOICE_NOT_PAID" => new BadRequestException("Hóa đơn thanh toán kỳ đầu chưa được thanh toán đầy đủ."),
                "IDENTITY_NOT_VERIFIED" => new BadRequestException("Khách hàng chưa được xác minh giấy tờ thành công tại quầy (yêu cầu verified)."),
                "CUSTOMER_NOT_ACKNOWLEDGED" => new BadRequestException("Khách hàng chưa xác nhận đồng ý nhận bàn giao tại quầy."),
                "STORAGE_UNIT_MISMATCH" => new BadRequestException("Ô kho không thuộc cùng cơ sở hoặc loại kho của đơn đặt chỗ."),
                "STORAGE_UNIT_NOT_LISTED" => new BadRequestException("Ô kho này hiện không được niêm yết (is_listed = false)."),
                "CHECKLIST_ITEM_REQUIRED" => new BadRequestException("Tên hạng mục kiểm tra không được để trống."),
                "CHECKLIST_INCOMPLETE" => new BadRequestException("Checklist kiểm tra phải bao gồm đầy đủ 4 hạng mục: cửa, khóa, đèn và vệ sinh."),
                "CHECKLIST_DUPLICATE_ITEM" => new BadRequestException("Checklist kiểm tra có hạng mục bị trùng lặp."),
                "CHECKLIST_INVALID_CONDITION" => new BadRequestException("Tình trạng trong checklist kiểm tra không hợp lệ."),
                "CHECKLIST_UNSAFE_CONDITION_FORBIDDEN" => new BadRequestException("Tình trạng 'unsafe' không được áp dụng cho từng hạng mục kiểm tra. Vui lòng sử dụng cờ IsUnsafe ở cấp độ biên bản kiểm tra."),
                "CHECKLIST_NOT_APPLICABLE_FORBIDDEN" => new BadRequestException("Hạng mục kiểm tra bắt buộc không được phép chọn 'not_applicable'."),
                "INSPECTION_INVALID_OR_STALE" => new BadRequestException("Biên bản kiểm tra không hợp lệ, chưa đạt yêu cầu hoặc đã hết hiệu lực sau khi đổi ô."),
                "STAFF_CONFIRMATION_REQUIRED" => new BadRequestException("Nhân viên chưa xác nhận đối chiếu người nhận với hồ sơ khách hàng."),
                "RESERVATION_ALLOCATION_MISMATCH" => new BadRequestException("Phân bổ không khớp cơ sở, loại kho hoặc kỳ thuê của đơn đặt chỗ."),
                "AGREEMENT_ALLOCATION_MISMATCH" => new BadRequestException("Phân bổ không khớp cơ sở, loại kho hoặc kỳ thuê của hợp đồng."),
                "INSPECTION_FACILITY_MISMATCH" => new BadRequestException("Thông tin kiểm tra không thuộc cùng cơ sở."),

                "INSPECTOR_NOT_ASSIGNED" => new ForbiddenException("Nhân viên kiểm tra không được phân công tại cơ sở này."),

                "AGREEMENT_ALREADY_HAS_DIFFERENT_ALLOCATION" => new ConflictException("Hợp đồng đã được gán ô kho khác. Vui lòng giải phóng ô kho hiện tại trước khi gán ô mới."),
                "STORAGE_UNIT_NOT_AVAILABLE" => new ConflictException("Ô kho hiện không khả dụng để gán."),
                "STORAGE_UNIT_MAINTENANCE_BLOCKED" => new ConflictException("Ô kho đang có lệnh bảo trì ngăn chặn đặt hoặc gán kho."),
                "STORAGE_UNIT_OVERLAPPING_ALLOCATION" => new ConflictException("Ô kho đã có phân bổ hoạt động trùng lặp trong khoảng thời gian thuê."),
                "AGREEMENT_OVERLAPPING_ALLOCATION" => new ConflictException("Hợp đồng đã có phân bổ ô kho hoạt động trùng lặp."),
                "STORAGE_UNIT_NOT_BOOKABLE" => new ConflictException("Ô kho không khả dụng để đặt hoặc gán."),
                "ALLOCATION_NOT_FOUND_OR_STALE" => new ConflictException("ID phân bổ ô kho không khớp với phân bổ hiện tại hoặc dữ liệu đã thay đổi."),
                "STORAGE_UNIT_NOT_RESERVED" => new ConflictException("Ô kho phải ở trạng thái 'reserved' trước khi hoàn tất bàn giao."),
                "HANDOVER_REPLAY_CONFLICT" => new ConflictException("ID phân bổ hoặc ID kiểm tra không khớp với biên bản bàn giao đã lưu."),
                "HANDOVER_SCOPE_MISMATCH" => new ConflictException("Biên bản bàn giao và kiểm tra không thuộc cùng hợp đồng hoặc ô kho."),
                "PIN_COLLISION_EXHAUSTED" => new ConflictException("Không thể cấp phát mã PIN do xung đột sau nhiều lần thử. Vui lòng thử lại."),
                "CREDENTIAL_NOT_ACTIVE" => new ConflictException("Mã truy cập hiện tại không còn hiệu lực hoặc đã bị thu hồi."),
                "CONCURRENCY_LOCK_TIMEOUT" => new ConflictException("Hệ thống đang bận hoặc xảy ra xung đột đồng thời, vui lòng thử lại sau giây lát."),
                "UNIQUE_CONSTRAINT_VIOLATION" => new ConflictException("Dữ liệu bị trùng lặp với bản ghi đã tồn tại."),
                "INVALID_UNIT_STATUS_TRANSITION" => new ConflictException("Trạng thái ô kho chuyển đổi không hợp lệ."),
                "INVALID_RESERVATION_STATUS_TRANSITION" => new ConflictException("Trạng thái đơn đặt chỗ chuyển đổi không hợp lệ."),
                "INVALID_AGREEMENT_STATUS_TRANSITION" => new ConflictException("Trạng thái hợp đồng thuê chuyển đổi không hợp lệ."),
                "INVALID_STATUS_FOR_COMPLETE" => new ConflictException("Đơn đặt chỗ hoặc hợp đồng không ở trạng thái sẵn sàng hoàn tất check-in."),
                _ => ioe
            };
        }

        return ex;
    }

    private static void ValidateStaffCounterAccess(StaffCounterPermissionSnapshot snapshot, long facilityId)
    {
        if (!snapshot.IsUserActive)
        {
            throw new ForbiddenException("Tài khoản nhân viên không hoạt động hoặc đã bị vô hiệu hóa.");
        }

        var isManager = snapshot.Roles.Contains(RoleConstants.FacilityManager);
        var isStaff = snapshot.Roles.Contains(RoleConstants.FacilityStaff);

        if (!isManager && !isStaff)
        {
            throw new ForbiddenException("Người dùng không có quyền nhân viên quầy tại cơ sở (yêu cầu role facility_staff hoặc facility_manager).");
        }

        if (!snapshot.IsEmployeeActive)
        {
            throw new ForbiddenException("Hồ sơ nhân viên không tồn tại hoặc không ở trạng thái hoạt động.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var validAssignments = snapshot.Assignments
            .Where(a => a.FacilityId == facilityId && a.StartsAt <= nowUtc && (a.EndsAt == null || a.EndsAt > nowUtc))
            .ToList();

        if (isManager)
        {
            var hasValidAssignment = validAssignments.Any(a =>
                string.Equals(a.AssignmentRole, RoleConstants.FacilityManager, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.AssignmentRole, RoleConstants.FacilityStaff, StringComparison.OrdinalIgnoreCase));
            if (!hasValidAssignment)
            {
                throw new ForbiddenException("Nhân viên quản lý không có phân công hợp lệ còn hiệu lực tại cơ sở này.");
            }
        }
        else
        {
            var hasValidAssignment = validAssignments.Any(a =>
                string.Equals(a.AssignmentRole, RoleConstants.FacilityStaff, StringComparison.OrdinalIgnoreCase));
            if (!hasValidAssignment)
            {
                throw new ForbiddenException("Nhân viên không có phân công nhân viên quầy (facility_staff) hợp lệ còn hiệu lực tại cơ sở này.");
            }
        }
    }
}
