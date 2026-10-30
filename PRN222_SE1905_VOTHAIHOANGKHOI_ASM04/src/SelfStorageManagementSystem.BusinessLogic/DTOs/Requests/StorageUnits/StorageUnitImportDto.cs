using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;

/// <summary>
/// Data Transfer Object for importing storage unit records safely from JSON/file.
/// Strictly enforces schema validation to prevent Insecure Deserialization vulnerabilities.
/// </summary>
public class StorageUnitImportDto
{
    [Required(ErrorMessage = "Mã cơ sở (FacilityId) là bắt buộc.")]
    [Range(1, long.MaxValue, ErrorMessage = "Mã cơ sở phải lớn hơn 0.")]
    public long FacilityId { get; set; }

    [Required(ErrorMessage = "Loại ô kho (UnitTypeId) là bắt buộc.")]
    [Range(1, long.MaxValue, ErrorMessage = "Mã loại ô kho phải lớn hơn 0.")]
    public long UnitTypeId { get; set; }

    public long? AreaId { get; set; }

    [Required(ErrorMessage = "Mã ô kho (UnitCode) là bắt buộc.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Mã ô kho phải từ 1 đến 50 ký tự.")]
    [RegularExpression(@"^[A-Za-z0-9\-_]+$", ErrorMessage = "Mã ô kho chỉ được chứa chữ cái, số, dấu gạch ngang hoặc gạch dưới.")]
    public string UnitCode { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Tầng không được vượt quá 50 ký tự.")]
    public string? FloorLabel { get; set; }

    [StringLength(50, ErrorMessage = "Khu vực không được vượt quá 50 ký tự.")]
    public string? ZoneLabel { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    public string? Notes { get; set; }
}
