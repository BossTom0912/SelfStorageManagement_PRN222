using SelfStorageManagementSystem.BusinessLogic.Common;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Catalog;

public class GetAvailableUnitsRequest : PagedRequest
{
    public long? UnitTypeId { get; set; }
    public long? AreaId { get; set; }
    public bool? ClimateControlled { get; set; }
    public decimal? MaxPrice { get; set; }
    public decimal? MinAreaM2 { get; set; }
    public decimal? MaxAreaM2 { get; set; }
    public DateOnly? RentalStartDate { get; set; }
    public DateOnly? RentalEndDate { get; set; }
}

