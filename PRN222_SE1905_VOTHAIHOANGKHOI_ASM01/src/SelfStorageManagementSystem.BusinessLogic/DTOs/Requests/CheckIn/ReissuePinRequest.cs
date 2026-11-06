namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;

public class ReissuePinRequest
{
    public long CurrentCredentialId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public bool IsConfirmedMatch { get; set; }
    public string? Reason { get; set; }
}
