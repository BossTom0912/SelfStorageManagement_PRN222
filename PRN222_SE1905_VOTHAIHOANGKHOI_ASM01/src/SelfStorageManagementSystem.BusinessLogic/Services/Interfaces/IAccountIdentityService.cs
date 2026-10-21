using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

/// <summary>
/// Core identity and account authentication service independent of JWT token generation.
/// Authenticates users for cookie sessions or token generation, registers customers,
/// and retrieves fresh identity and role snapshots directly from persistence.
/// </summary>
public interface IAccountIdentityService
{
    Task<AuthenticatedAccountResponse> AuthenticateAsync(
        string email,
        string password,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedAccountResponse> AuthenticateAsync(
        LoginRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    Task<CustomerRegisterResponse> RegisterCustomerAsync(
        CustomerRegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<CurrentUserResponse?> GetCurrentUserAsync(
        long userId,
        CancellationToken cancellationToken = default);
}
