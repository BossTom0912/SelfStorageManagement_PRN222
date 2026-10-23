using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Agreements;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IRentalAgreementService
{
    Task<RentalAgreementDetailResponse> GetAgreementByIdAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long agreementId,
        CancellationToken cancellationToken = default);
}
