namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Catalog;

public class GetFacilityUnitTypesRequest
{
    public DateOnly? RentalStartDate { get; set; }
    public DateOnly? RentalEndDate { get; set; }
    public decimal? MaxPrice { get; set; }
    public decimal? MinAreaM2 { get; set; }
    public decimal? MaxAreaM2 { get; set; }
    public bool? ClimateControlled { get; set; }
}

