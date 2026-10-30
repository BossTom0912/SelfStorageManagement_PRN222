using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.Web.Models;

/// <summary>
/// Mô hình dữ liệu đăng ký tài khoản khách hàng mới trên giao diện MVC Web.
/// </summary>
public class RegisterViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    [MaxLength(255, ErrorMessage = "Họ và tên không được vượt quá 255 ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng.")]
    [MaxLength(255, ErrorMessage = "Email không được vượt quá 255 ký tự.")]
    [Display(Name = "Địa chỉ Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [MinLength(6, ErrorMessage = "Mật khẩu phải có độ dài tối thiểu 6 ký tự.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận lại mật khẩu.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không trùng khớp.")]
    [Display(Name = "Xác nhận mật khẩu")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Số điện thoại không đúng định dạng.")]
    [MaxLength(50, ErrorMessage = "Số điện thoại không được vượt quá 50 ký tự.")]
    [Display(Name = "Số điện thoại")]
    public string? PhoneNumber { get; set; }

    [MaxLength(50, ErrorMessage = "Số CMND/CCCD không được vượt quá 50 ký tự.")]
    [Display(Name = "Số CCCD / CMND")]
    public string? IdentityNumber { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Ngày sinh")]
    public DateOnly? DateOfBirth { get; set; }

    [MaxLength(255, ErrorMessage = "Địa chỉ không được vượt quá 255 ký tự.")]
    [Display(Name = "Địa chỉ liên hệ")]
    public string? Address { get; set; }

    [MaxLength(255, ErrorMessage = "Tên người liên hệ khẩn cấp không được vượt quá 255 ký tự.")]
    [Display(Name = "Người liên hệ khẩn cấp")]
    public string? EmergencyContactName { get; set; }

    [Phone(ErrorMessage = "Số điện thoại khẩn cấp không đúng định dạng.")]
    [MaxLength(50, ErrorMessage = "Số điện thoại khẩn cấp không được vượt quá 50 ký tự.")]
    [Display(Name = "SĐT liên hệ khẩn cấp")]
    public string? EmergencyContactPhone { get; set; }

    public string? ReturnUrl { get; set; }
}
