namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;

public class CompleteCheckInRequest
{
    public long CurrentAllocationId { get; set; }
    public long CurrentInspectionId { get; set; }
    public bool CustomerAcknowledged { get; set; }
    public string? Notes { get; set; }
}
