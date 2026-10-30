namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;

public class InspectionResponse
{
    public long InspectionId { get; set; }
    public long? AgreementId { get; set; }
    public long? StorageUnitId { get; set; }
    public string? UnitCode { get; set; }
    public string InspectionType { get; set; } = "check_in";
    public string Status { get; set; } = string.Empty;
    public string? OverallCondition { get; set; }
    public string? Summary { get; set; }
    public DateTimeOffset? InspectedAt { get; set; }
    public bool UnitReleasedDueToFailure { get; set; }
    public List<InspectionItemResponse> Items { get; set; } = new();
}

public class InspectionItemResponse
{
    public long Id { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public decimal ChargeAmount { get; set; }
}
