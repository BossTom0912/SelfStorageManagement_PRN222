using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class StorageUnitHaTDT
{
    [Key]
    [Display(Name = "Mã định danh ô kho")]
    [Required(ErrorMessage = "Mã định danh ô kho không được để trống.")]
    [Range(1, long.MaxValue, ErrorMessage = "Mã định danh ô kho phải là số nguyên dương lớn hơn 0.")]
    public long StorageUnitHaTDTId { get; set; }

    [Display(Name = "Cơ sở lưu trữ")]
    [Required(ErrorMessage = "Mã cơ sở lưu trữ không được để trống.")]
    [Range(1, long.MaxValue, ErrorMessage = "Mã cơ sở lưu trữ phải là số nguyên dương lớn hơn 0.")]
    public long facility_id { get; set; }

    [Display(Name = "Loại ô kho")]
    [Required(ErrorMessage = "Mã loại ô kho không được để trống.")]
    [Range(1, long.MaxValue, ErrorMessage = "Mã loại ô kho phải là số nguyên dương lớn hơn 0.")]
    public long UnitTypeHaTDTId { get; set; }

    [Display(Name = "Khu vực")]
    [Required(ErrorMessage = "Mã khu vực không được để trống.")]
    [Range(1, long.MaxValue, ErrorMessage = "Mã khu vực phải là số nguyên dương lớn hơn 0.")]
    public long? area_id { get; set; }

    [Display(Name = "Mã ô kho")]
    [Required(ErrorMessage = "Mã ô kho không được để trống.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Mã ô kho phải từ 1 đến 50 ký tự.")]
    [RegularExpression(@"^[a-zA-Z0-9_\-]+$", ErrorMessage = "Mã ô kho không hợp lệ. Chỉ chấp nhận chữ cái, chữ số, gạch ngang (-) và gạch dưới (_).")]
    public string unit_code { get; set; } = null!;

    [Display(Name = "Nhãn tầng")]
    [Required(ErrorMessage = "Nhãn tầng không được để trống.")]
    [StringLength(50, ErrorMessage = "Nhãn tầng không được vượt quá 50 ký tự.")]
    public string? floor_label { get; set; }

    [Display(Name = "Phân khu")]
    [Required(ErrorMessage = "Phân khu không được để trống.")]
    [StringLength(50, ErrorMessage = "Phân khu không được vượt quá 50 ký tự.")]
    public string? zone_label { get; set; }

    [Display(Name = "Trạng thái vật lý")]
    [Required(ErrorMessage = "Trạng thái vật lý không được để trống.")]
    [StringLength(50, ErrorMessage = "Trạng thái vật lý không được vượt quá 50 ký tự.")]
    public string physical_status { get; set; } = null!;

    [Display(Name = "Trạng thái niêm yết")]
    [Required(ErrorMessage = "Trạng thái niêm yết không được để trống.")]
    public bool is_listed { get; set; }

    [Display(Name = "Ghi chú")]
    [Required(ErrorMessage = "Ghi chú không được để trống.")]
    [StringLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    public string? notes { get; set; }

    [Display(Name = "Thời gian tạo")]
    [Required(ErrorMessage = "Thời gian tạo không được để trống.")]
    public DateTimeOffset created_at { get; set; }

    [Display(Name = "Thời gian cập nhật")]
    [Required(ErrorMessage = "Thời gian cập nhật không được để trống.")]
    public DateTimeOffset updated_at { get; set; }
}
