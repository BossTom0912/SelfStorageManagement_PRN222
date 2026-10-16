namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;

public class IdentityVerificationResponse
{
    public long VerificationId { get; set; }
    public long ReservationId { get; set; }
    public string VerificationMethod { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public long VerifiedBy { get; set; }
    public string? VerifiedByName { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
    public string? MaskedIdentityNumber { get; set; }
    public string? Notes { get; set; }
}
