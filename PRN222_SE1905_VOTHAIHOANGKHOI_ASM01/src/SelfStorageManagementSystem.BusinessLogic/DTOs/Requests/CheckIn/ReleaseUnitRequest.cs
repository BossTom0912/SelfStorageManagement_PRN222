namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;

public class ReleaseUnitRequest
{
    public long CurrentAllocationId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
