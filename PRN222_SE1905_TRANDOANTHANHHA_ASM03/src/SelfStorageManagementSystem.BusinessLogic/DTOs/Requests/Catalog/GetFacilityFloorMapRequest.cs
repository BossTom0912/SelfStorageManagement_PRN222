namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Catalog;

public class GetFacilityFloorMapRequest
{
    public long? AreaId { get; set; }
    public DateOnly? RentalStartDate { get; set; }
    public DateOnly? RentalEndDate { get; set; }
}
