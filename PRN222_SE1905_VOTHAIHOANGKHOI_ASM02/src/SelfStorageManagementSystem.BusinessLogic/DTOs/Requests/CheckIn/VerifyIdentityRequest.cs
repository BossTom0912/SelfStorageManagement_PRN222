namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;

public class VerifyIdentityRequest
{
    public string VerificationMethod { get; set; } = "government_id";
    public string? DocumentNumber { get; set; }
    public bool IsConfirmedMatch { get; set; }
    public string? Notes { get; set; }
}
