namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;

/// <summary>
/// Result of an administrative storage unit deletion operation.
/// </summary>
public class DeleteStorageUnitResult
{
    public long UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool IsUnlistedOnly { get; set; }
    public string Message { get; set; } = string.Empty;
}
