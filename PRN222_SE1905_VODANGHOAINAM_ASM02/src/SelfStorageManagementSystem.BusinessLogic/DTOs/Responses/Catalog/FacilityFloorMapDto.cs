namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Catalog;

public class FacilityFloorMapDto
{
    public long FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public List<FacilityAreaDto> Areas { get; set; } = new();
    public List<UnitMapItemDto> Units { get; set; } = new();
    public int TotalUnitsWithMap { get; set; }
    public int TotalUnitsWithoutMap { get; set; }
}

public class FacilityAreaDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AreaType { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public long? ParentAreaId { get; set; }
}

public class UnitMapItemDto
{
    public long UnitId { get; set; }
    public long AreaId { get; set; }
    public string AreaName { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public long UnitTypeId { get; set; }
    public string UnitTypeName { get; set; } = string.Empty;
    public decimal? AreaM2 { get; set; }
    public decimal? VolumeM3 { get; set; }
    public bool ClimateControlled { get; set; }
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public decimal RotationDegrees { get; set; }

    /// <summary>
    /// Simplified visual status: 'available', 'occupied', 'reserved', 'maintenance', 'unavailable'.
    /// Note: Does not contain private rental or customer information.
    /// </summary>
    public string DisplayStatus { get; set; } = "available";

    /// <summary>
    /// Whether user can select this unit type to proceed with reservation.
    /// In accordance with BR-RSV-03, customer reserves the Unit Type, not this specific Unit ID.
    /// </summary>
    public bool CanSelectToProceed { get; set; }

    public decimal? MonthlyRate { get; set; }
}
