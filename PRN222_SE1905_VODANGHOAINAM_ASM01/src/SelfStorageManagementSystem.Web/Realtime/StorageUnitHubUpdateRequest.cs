using System;
using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// Hub request DTO for HubUpdate_StorageUnit deserialized from client JSON.
/// Validates ID, modified fields, and optional ExpectedUpdatedAt for optimistic concurrency.
/// </summary>
public class StorageUnitHubUpdateRequest
{
    [Required]
    [Range(1, long.MaxValue, ErrorMessage = "ID ô kho không hợp lệ.")]
    public long Id { get; set; }

    [Required(ErrorMessage = "Mã ô kho không được để trống.")]
    [StringLength(50, ErrorMessage = "Mã ô kho tối đa 50 ký tự.")]
    public string UnitCode { get; set; } = string.Empty;

    public long? AreaId { get; set; }

    [StringLength(50, ErrorMessage = "Nhãn tầng tối đa 50 ký tự.")]
    public string? FloorLabel { get; set; }

    [StringLength(50, ErrorMessage = "Nhãn phân khu tối đa 50 ký tự.")]
    public string? ZoneLabel { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Notes { get; set; }

    public DateTimeOffset? ExpectedUpdatedAt { get; set; }
}
