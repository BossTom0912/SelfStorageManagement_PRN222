namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth;

/// <summary>
/// Result of account authentication without any JWT access token or password hash.
/// Used for cookie-based authentication and shared identity verification.
/// </summary>
public class AuthenticatedAccountResponse
{
    public long UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public string? UserType { get; set; }
    public string? EmployeeCode { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
