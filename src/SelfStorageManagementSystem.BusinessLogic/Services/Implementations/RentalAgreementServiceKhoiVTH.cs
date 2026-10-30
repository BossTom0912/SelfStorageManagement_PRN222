using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Agreements;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class RentalAgreementServiceKhoiVTH : IRentalAgreementService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IFacilityScopeService _facilityScopeService;

    public RentalAgreementServiceKhoiVTH(
        IPaymentRepository paymentRepository,
        IFacilityScopeService facilityScopeService)
    {
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _facilityScopeService = facilityScopeService ?? throw new ArgumentNullException(nameof(facilityScopeService));
    }

    public async Task<RentalAgreementDetailResponse> GetAgreementByIdAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long agreementId,
        CancellationToken cancellationToken = default)
    {
        var agreement = await _paymentRepository.GetAgreementByIdAsync(agreementId, cancellationToken);
        if (agreement == null)
        {
            throw new NotFoundException($"Không tìm thấy hợp đồng thuê với ID {agreementId}.");
        }

        // Authorization check
        var isAdmin = roles.Contains(RoleConstants.SystemAdministrator) || roles.Contains(RoleConstants.BusinessOperationsManager);
        var isOwner = roles.Contains(RoleConstants.StorageCustomer) && agreement.customer_id == currentUserId;

        if (!isAdmin && !isOwner)
        {
            var isStaff = roles.Any(r => r == RoleConstants.FacilityStaff || r == RoleConstants.FacilityManager);
            if (isStaff)
            {
                var hasAccess = await _facilityScopeService.HasAccessToFacilityAsync(
                    currentUserId,
                    agreement.facility_id,
                    RoleConstants.FacilityStaff,
                    cancellationToken);

                if (!hasAccess)
                {
                    throw new ForbiddenException("Bạn không có quyền truy cập vào hợp đồng của cơ sở này.");
                }
            }
            else
            {
                throw new ForbiddenException("Bạn không có quyền xem thông tin hợp đồng này.");
            }
        }

        var activeAllocation = agreement.unit_allocations.FirstOrDefault(ua => ua.status == "active");
        var isAllocated = activeAllocation != null;
        var notice = agreement.status switch
        {
            "active" => "Hợp đồng đã kích hoạt thành công (đã hoàn tất check-in và bàn giao kho).",
            "scheduled" when isAllocated => "Ô kho đã được gán, đang chờ kiểm tra và hoàn tất bàn giao tại quầy.",
            "scheduled" => "Hợp đồng ở trạng thái scheduled (chờ bàn giao kho). Ô kho cụ thể và biên bản bàn giao sẽ được thực hiện khi làm thủ tục check-in tại quầy (Function 5).",
            _ => $"Hợp đồng ở trạng thái {agreement.status}."
        };

        return new RentalAgreementDetailResponse
        {
            AgreementId = agreement.id,
            AgreementNo = agreement.agreement_no,
            ReservationId = agreement.reservation_id,
            ReservationCode = agreement.reservation?.reservation_code,
            CustomerId = agreement.customer_id,
            CustomerName = agreement.customer?.full_name ?? "Khách hàng",
            FacilityId = agreement.facility_id,
            FacilityName = agreement.facility?.name ?? string.Empty,
            PolicyVersionId = agreement.policy_version_id,
            PolicyVersion = agreement.policy_version?.version,
            StartDate = agreement.start_date,
            EndDate = agreement.end_date,
            MonthlyRateSnapshot = agreement.monthly_rate_snapshot,
            DepositSnapshot = agreement.deposit_snapshot,
            DepositBalance = agreement.deposit_balance,
            Status = agreement.status,
            SignedAt = agreement.signed_at,
            CheckedInAt = agreement.checked_in_at,
            IsStorageUnitAllocated = isAllocated,
            StorageUnitId = activeAllocation?.storage_unit_id,
            StorageUnitCode = activeAllocation?.storage_unit?.unit_code,
            CreatedAt = agreement.created_at,
            Notice = notice
        };
    }
}
