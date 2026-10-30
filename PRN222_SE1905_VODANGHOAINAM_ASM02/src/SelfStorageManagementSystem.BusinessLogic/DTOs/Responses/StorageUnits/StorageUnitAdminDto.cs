using System;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;

/// <summary>
/// Administrative summary DTO for storage unit listings and operations.
/// </summary>
public class StorageUnitAdminDto
{
    public long Id { get; set; }
    public long FacilityId { get; set; }
    public string FacilityCode { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public long UnitTypeId { get; set; }
    public string UnitTypeCode { get; set; } = string.Empty;
    public string UnitTypeName { get; set; } = string.Empty;
    public long? AreaId { get; set; }
    public string? AreaCode { get; set; }
    public string? AreaName { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string? FloorLabel { get; set; }
    public string? ZoneLabel { get; set; }
    public string PhysicalStatus { get; set; } = string.Empty;
    public bool IsListed { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
