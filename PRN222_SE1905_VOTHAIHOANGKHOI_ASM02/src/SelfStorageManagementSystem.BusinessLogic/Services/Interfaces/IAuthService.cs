using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(
        LoginRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    Task<CustomerRegisterResponse> RegisterCustomerAsync(
        CustomerRegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<CurrentUserResponse> GetCurrentUserAsync(
        long userId,
        CancellationToken cancellationToken = default);
}
