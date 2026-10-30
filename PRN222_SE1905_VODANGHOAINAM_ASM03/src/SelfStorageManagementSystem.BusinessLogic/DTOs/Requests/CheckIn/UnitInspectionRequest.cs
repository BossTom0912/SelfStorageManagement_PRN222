namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;

public class UnitInspectionRequest
{
    public long CurrentAllocationId { get; set; }
    public bool IsUnsafe { get; set; } = false;
    public List<InspectionItemRequest> Items { get; set; } = new();
    public string? OverallSummary { get; set; }
}

public class InspectionItemRequest
{
    public string ItemName { get; set; } = string.Empty;
    public string Condition { get; set; } = "good";
    public string? Notes { get; set; }
}
