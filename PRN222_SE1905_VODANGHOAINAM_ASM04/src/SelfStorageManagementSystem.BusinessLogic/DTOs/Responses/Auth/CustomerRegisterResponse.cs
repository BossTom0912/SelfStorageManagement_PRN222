namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth;

public class CustomerRegisterResponse
{
    public long UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
