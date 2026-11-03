using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

/// <summary>
/// Adapter implementation of IAuthService for API / JWT clients.
/// Uses the shared IAccountIdentityService to perform credentials verification,
/// account status check, last_login_at updating, and login history recording once,
/// then issues JWT tokens via IJwtTokenGenerator without recording duplicate history.
/// </summary>
public class AuthServiceKhoiVTH : IAuthService
{
    private readonly IAccountIdentityService _accountIdentityService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthServiceKhoiVTH(
        IAccountIdentityService accountIdentityService,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _accountIdentityService = accountIdentityService ?? throw new ArgumentNullException(nameof(accountIdentityService));
        _jwtTokenGenerator = jwtTokenGenerator ?? throw new ArgumentNullException(nameof(jwtTokenGenerator));
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Shared identity authentication: validates credentials, active status, updates last_login_at,
        // and records login history (succeeded/failed/locked/blocked) exactly once.
        var account = await _accountIdentityService.AuthenticateAsync(
            request.Email,
            request.Password,
            ipAddress,
            userAgent,
            cancellationToken);

        // Issue JWT token for API clients without recording duplicate login history
        var (token, expiresAt) = _jwtTokenGenerator.GenerateToken(account.UserId, account.Email, account.Roles);

        return new LoginResponse
        {
            UserId = account.UserId,
            DisplayName = account.DisplayName,
            Email = account.Email,
            Roles = account.Roles,
            AccessToken = token,
            ExpiresAt = expiresAt
        };
    }

    public Task<CustomerRegisterResponse> RegisterCustomerAsync(
        CustomerRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _accountIdentityService.RegisterCustomerAsync(request, cancellationToken);
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _accountIdentityService.GetCurrentUserAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new UnauthorizedException("User not found or account is deactivated.");
        }

        return user;
    }
}
