using System;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// Event DTO broadcasted over SignalR for storage unit lifecycle events.
/// Strictly matches Contract v1 section 8.3 specification.
/// Does not leak EF entities or sensitive internal notes.
/// </summary>
public class StorageUnitRealtimeEventDto
{
    public long id { get; set; }
    public string unitCode { get; set; } = string.Empty;
    public long facilityId { get; set; }
    public string facilityCode { get; set; } = string.Empty;
    public string facilityName { get; set; } = string.Empty;
    public long unitTypeId { get; set; }
    public string typeName { get; set; } = string.Empty;
    public string unitTypeCode { get; set; } = string.Empty;
    public long? areaId { get; set; }
    public string? areaName { get; set; }
    public string status { get; set; } = string.Empty;
    public decimal monthlyRate { get; set; }
    public bool isClimateControlled { get; set; }
    public string updatedAt { get; set; } = string.Empty;

    // Presentation helpers for seamless UI upsert
    public string? floorLabel { get; set; }
    public string? zoneLabel { get; set; }
    public bool isListed { get; set; }

    public static StorageUnitRealtimeEventDto FromAdminDto(
        StorageUnitAdminDto dto,
        decimal monthlyRate = 0m,
        bool isClimateControlled = false)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var detail = dto as StorageUnitAdminDetailDto;

        return new StorageUnitRealtimeEventDto
        {
            id = dto.Id,
            unitCode = dto.UnitCode,
            facilityId = dto.FacilityId,
            facilityCode = dto.FacilityCode,
            facilityName = dto.FacilityName,
            unitTypeId = dto.UnitTypeId,
            typeName = dto.UnitTypeName,
            unitTypeCode = dto.UnitTypeCode,
            areaId = dto.AreaId,
            areaName = dto.AreaName,
            status = dto.PhysicalStatus,
            monthlyRate = detail?.MonthlyRate ?? monthlyRate,
            isClimateControlled = isClimateControlled,
            updatedAt = dto.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
            floorLabel = dto.FloorLabel,
            zoneLabel = dto.ZoneLabel,
            isListed = dto.IsListed
        };
    }
}
