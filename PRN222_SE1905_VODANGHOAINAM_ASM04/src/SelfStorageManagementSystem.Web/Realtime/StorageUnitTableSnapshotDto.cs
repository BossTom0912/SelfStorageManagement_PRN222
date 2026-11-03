using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// Query parameters sent by client for retrieving table snapshot over SignalR.
/// Properties use string/nullable primitives to prevent JSON deserialization failures
/// on empty strings from JavaScript form inputs while maintaining bigint safety.
/// </summary>
public class StorageUnitTableQueryRequest
{
    public string? FacilityId { get; set; }
    public string? UnitTypeId { get; set; }
    public string? AreaId { get; set; }
    public string? PhysicalStatus { get; set; }
    public string? IsListed { get; set; }
    public string? SearchTerm { get; set; }
    public string? SortBy { get; set; } = "unit_code";
    public bool SortDescending { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public long ClientGeneration { get; set; }
}

/// <summary>
/// Row presentation DTO for Storage Units table rendered via SignalR snapshot.
/// Strictly stringifies IDs to prevent JavaScript 64-bit integer precision truncation.
/// Omits internal sensitive data while preserving full table column requirements.
/// </summary>
public class StorageUnitTableRowDto
{
    public string Id { get; set; } = string.Empty;
    public long IdNumeric { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string FacilityId { get; set; } = string.Empty;
    public string FacilityCode { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public string UnitTypeId { get; set; } = string.Empty;
    public string UnitTypeCode { get; set; } = string.Empty;
    public string UnitTypeName { get; set; } = string.Empty;
    public string? AreaId { get; set; }
    public string? AreaName { get; set; }
    public string? FloorLabel { get; set; }
    public string? ZoneLabel { get; set; }
    public string PhysicalStatus { get; set; } = string.Empty;
    public bool IsListed { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public string UpdatedAtFormatted { get; set; } = string.Empty;
}

/// <summary>
/// Snapshot payload returned by HubGet_StorageUnitsTableSnapshot.
/// Provides server-authorized data, totals, pagination state, and sequence generation
/// without transmitting full-page HTML.
/// </summary>
public class StorageUnitTableSnapshotDto
{
    public IReadOnlyList<StorageUnitTableRowDto> Items { get; set; } = Array.Empty<StorageUnitTableRowDto>();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
    public bool CanManage { get; set; }
    public long ClientGeneration { get; set; }
}
