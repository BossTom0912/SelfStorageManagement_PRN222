using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.Web.Pages.Staff.CheckIn;

public class VerifyIdentityInput
{
    [Required]
    public long ReservationId { get; set; }

    [Required]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn phương thức xác minh.")]
    public string VerificationMethod { get; set; } = "government_id";

    public string? DocumentNumber { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Nhân viên phải xác nhận đã đối chiếu giấy tờ trùng khớp.")]
    public bool IsConfirmedMatch { get; set; }

    public string? Notes { get; set; }
}

public class AssignUnitInput
{
    [Required]
    public long ReservationId { get; set; }

    [Required]
    public string Code { get; set; } = string.Empty;

    [Range(1, long.MaxValue, ErrorMessage = "Vui lòng chọn ô kho hợp lệ để gán.")]
    public long StorageUnitId { get; set; }
}

public class ReleaseUnitInput
{
    [Required]
    public long ReservationId { get; set; }

    [Required]
    public string Code { get; set; } = string.Empty;

    [Range(1, long.MaxValue, ErrorMessage = "ID phân bổ hiện tại không hợp lệ.")]
    public long CurrentAllocationId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập lý do giải phóng ô kho.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Lý do giải phóng ô kho từ 3 đến 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

public class PerformInspectionInput
{
    [Required]
    public long ReservationId { get; set; }

    [Required]
    public string Code { get; set; } = string.Empty;

    [Range(1, long.MaxValue, ErrorMessage = "ID phân bổ hiện tại không hợp lệ.")]
    public long CurrentAllocationId { get; set; }

    public bool IsUnsafe { get; set; }

    public string? OverallSummary { get; set; }

    public List<InspectionItemInput> Items { get; set; } = new();
}

public class InspectionItemInput
{
    [Required]
    public string ItemName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn tình trạng hạng mục.")]
    public string Condition { get; set; } = "good";

    public string? Notes { get; set; }
}

public class CompleteCheckInInput
{
    [Required]
    public long ReservationId { get; set; }

    [Required]
    public string Code { get; set; } = string.Empty;

    [Range(1, long.MaxValue, ErrorMessage = "ID phân bổ hiện tại không hợp lệ.")]
    public long CurrentAllocationId { get; set; }

    [Range(1, long.MaxValue, ErrorMessage = "ID biên bản kiểm tra hiện tại không hợp lệ.")]
    public long CurrentInspectionId { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Khách hàng phải xác nhận đồng ý nhận bàn giao tại quầy.")]
    public bool CustomerAcknowledged { get; set; }

    public string? Notes { get; set; }
}

public class ReissuePinInput
{
    [Required]
    public long ReservationId { get; set; }

    [Required]
    public string Code { get; set; } = string.Empty;

    [Range(1, long.MaxValue, ErrorMessage = "ID mã truy cập hiện tại không hợp lệ.")]
    public long CurrentCredentialId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập số giấy tờ tùy thân để đối chiếu.")]
    public string DocumentNumber { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "Nhân viên phải xác nhận đối chiếu người nhận với hồ sơ.")]
    public bool IsConfirmedMatch { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập lý do cấp lại mã PIN.")]
    public string? Reason { get; set; }
}
