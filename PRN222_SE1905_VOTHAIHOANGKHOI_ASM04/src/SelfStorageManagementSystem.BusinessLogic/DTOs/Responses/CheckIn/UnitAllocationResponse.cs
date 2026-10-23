namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;

public class UnitAllocationResponse
{
    public long AllocationId { get; set; }
    public long? AgreementId { get; set; }
    public long? ReservationId { get; set; }
    public long StorageUnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string AllocationKind { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? Reason { get; set; }
}
