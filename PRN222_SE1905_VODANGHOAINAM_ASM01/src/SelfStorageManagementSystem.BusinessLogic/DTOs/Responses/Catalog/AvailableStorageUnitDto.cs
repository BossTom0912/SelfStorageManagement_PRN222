namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Catalog;

public class AvailableStorageUnitDto
{
    public long Id { get; set; }
    public long FacilityId { get; set; }
    public long UnitTypeId { get; set; }
    public string UnitTypeName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public long? AreaId { get; set; }
    public string? AreaName { get; set; }
    public string? FloorLabel { get; set; }
    public string? ZoneLabel { get; set; }
    public string PhysicalStatus { get; set; } = "available";
    public bool IsListed { get; set; }
    public decimal WidthM { get; set; }
    public decimal LengthM { get; set; }
    public decimal HeightM { get; set; }
    public decimal? AreaM2 { get; set; }
    public decimal? VolumeM3 { get; set; }
    public bool ClimateControlled { get; set; }
    public decimal? MonthlyRate { get; set; }
    public bool HasMapPosition { get; set; }
    public decimal? MapX { get; set; }
    public decimal? MapY { get; set; }
    public decimal? MapWidth { get; set; }
    public decimal? MapHeight { get; set; }
}
