namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;

public class CustomerUnitListItemResponse
{
    public long AgreementId { get; set; }
    public string AgreementNo { get; set; } = string.Empty;
    public string AgreementStatus { get; set; } = string.Empty;
    public long? StorageUnitId { get; set; }
    public string? UnitCode { get; set; }
    public long FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public long UnitTypeId { get; set; }
    public string UnitTypeName { get; set; } = string.Empty;
    public decimal AreaM2 { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTimeOffset? CheckedInAt { get; set; }
    public bool IsActive { get; set; }
}
