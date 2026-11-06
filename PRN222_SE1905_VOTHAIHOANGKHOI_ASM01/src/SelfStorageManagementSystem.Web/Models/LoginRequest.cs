using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.Web.Models;

/// <summary>
/// Mô hình dữ liệu đăng nhập kế thừa quy ước TeacherCode (LoginRequest.cs):
/// Giữ nguyên hai trường UserName và Password, hỗ trợ ReturnUrl và RememberMe.
/// Tuyệt đối không chứa role/scope trong form gửi lên để chống leo thang đặc quyền.
/// </summary>
public class LoginRequest
{
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập hoặc địa chỉ email.")]
    [Display(Name = "Tên đăng nhập / Email")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }

    [Display(Name = "Ghi nhớ đăng nhập")]
    public bool RememberMe { get; set; }
}
