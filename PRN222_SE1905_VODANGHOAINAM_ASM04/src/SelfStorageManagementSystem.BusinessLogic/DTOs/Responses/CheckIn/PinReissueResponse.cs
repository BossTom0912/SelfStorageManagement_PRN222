namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;

public class PinReissueResponse
{
    public long AgreementId { get; set; }
    public long OldCredentialId { get; set; }
    public long NewCredentialId { get; set; }
    public string Pin { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
