namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth;

public class LoginResponse
{
    public long UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
