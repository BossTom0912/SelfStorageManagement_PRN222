using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Agreements;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class RentalAgreementService : IRentalAgreementService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IFacilityScopeService _facilityScopeService;

    public RentalAgreementService(
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
                    roles.First(),
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
            IsStorageUnitAllocated = false,
            CreatedAt = agreement.created_at
        };
    }
}
