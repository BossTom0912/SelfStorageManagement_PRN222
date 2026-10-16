using System;
using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;

/// <summary>
/// Request to update an existing storage unit.
/// Per business rules, only UnitCode, AreaId, FloorLabel, ZoneLabel, and Notes can be modified.
/// Facility, UnitType, PhysicalStatus, and Listing status remain immutable across updates.
/// ExpectedUpdatedAt is strictly required for optimistic concurrency verification.
/// </summary>
public class UpdateStorageUnitRequest
{
    [Required]
    public DateTimeOffset ExpectedUpdatedAt { get; set; }

    [Required]
    [StringLength(50)]
    public string UnitCode { get; set; } = string.Empty;

    public long? AreaId { get; set; }

    [StringLength(50)]
    public string? FloorLabel { get; set; }

    [StringLength(50)]
    public string? ZoneLabel { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
