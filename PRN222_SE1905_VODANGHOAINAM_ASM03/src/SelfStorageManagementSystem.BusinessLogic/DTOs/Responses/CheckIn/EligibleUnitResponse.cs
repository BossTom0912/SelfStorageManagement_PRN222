namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;

public class EligibleUnitResponse
{
    public long UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public long FacilityId { get; set; }
    public long UnitTypeId { get; set; }
    public string UnitTypeName { get; set; } = string.Empty;
    public decimal AreaM2 { get; set; }
    public bool ClimateControlled { get; set; }
    public string PhysicalStatus { get; set; } = string.Empty;
    public string? AreaName { get; set; }
    public string? FloorLabel { get; set; }
    public string? ZoneLabel { get; set; }
}
