namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;

public class CustomerHandoverResponse
{
    public long AgreementId { get; set; }
    public string AgreementNo { get; set; } = string.Empty;
    public string AgreementStatus { get; set; } = string.Empty;
    public string? StorageUnitCode { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public long HandoverRecordId { get; set; }
    public string HandoverType { get; set; } = string.Empty;
    public DateTimeOffset? HandedAt { get; set; }
    public DateTimeOffset? CheckedInAt { get; set; }
    public string? Notes { get; set; }
    public string? InspectionCondition { get; set; }
    public string? CredentialType { get; set; }
    public string? CredentialStatus { get; set; }
    public DateTimeOffset? CredentialIssuedAt { get; set; }
    public DateTimeOffset? CredentialExpiresAt { get; set; }
}
