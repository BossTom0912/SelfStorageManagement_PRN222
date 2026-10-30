using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;

/// <summary>
/// Request to create a new storage unit.
/// Server always assigns physical_status = 'available' and is_listed = true upon creation.
/// </summary>
public class CreateStorageUnitRequest
{
    [Required]
    public long FacilityId { get; set; }

    [Required]
    public long UnitTypeId { get; set; }

    public long? AreaId { get; set; }

    [Required]
    [StringLength(50)]
    public string UnitCode { get; set; } = string.Empty;

    [StringLength(50)]
    public string? FloorLabel { get; set; }

    [StringLength(50)]
    public string? ZoneLabel { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
