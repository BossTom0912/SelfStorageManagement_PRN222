using System.Collections.Generic;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;

/// <summary>
/// Lookup collections for populating dropdowns and filters in storage unit management.
/// </summary>
public class StorageUnitLookupsDto
{
    public List<FacilityLookupDto> Facilities { get; set; } = new();
    public List<StorageUnitTypeLookupDto> UnitTypes { get; set; } = new();
    public List<StorageUnitAreaLookupDto> Areas { get; set; } = new();
    public List<string> PhysicalStatuses { get; set; } = new()
    {
        "available",
        "occupied",
        "maintenance",
        "reserved"
    };
}

public class StorageUnitTypeLookupDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal WidthMeters { get; set; }
    public decimal LengthMeters { get; set; }
    public decimal HeightMeters { get; set; }
    public decimal? FloorAreaSqm { get; set; }
    public decimal? VolumeCbm { get; set; }
    public decimal? MonthlyRate { get; set; }
    public bool IsActive { get; set; }
}

public class StorageUnitAreaLookupDto
{
    public long Id { get; set; }
    public long FacilityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? FloorLabel { get; set; }
    public bool IsActive { get; set; }
}
