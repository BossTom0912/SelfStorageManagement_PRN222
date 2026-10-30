namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;

/// <summary>
/// Filter request for listing and querying storage units in administration views.
/// </summary>
public class StorageUnitFilterRequest
{
    public long? FacilityId { get; set; }
    public long? UnitTypeId { get; set; }
    public long? AreaId { get; set; }
    public string? PhysicalStatus { get; set; }
    public bool? IsListed { get; set; }
    public string? SearchTerm { get; set; }
    public string? SortBy { get; set; } = "unit_code";
    public bool SortDescending { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
