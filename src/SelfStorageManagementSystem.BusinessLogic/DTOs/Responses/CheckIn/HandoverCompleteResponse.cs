namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;

public class HandoverCompleteResponse
{
    public long HandoverRecordId { get; set; }
    public long AgreementId { get; set; }
    public string AgreementNo { get; set; } = string.Empty;
    public long UnitAllocationId { get; set; }
    public long StorageUnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public DateTimeOffset HandedAt { get; set; }
    public bool IsAlreadyCompleted { get; set; }
    public string? Pin { get; set; }
    public long? CredentialId { get; set; }
    public DateTimeOffset? CredentialExpiresAt { get; set; }
}
