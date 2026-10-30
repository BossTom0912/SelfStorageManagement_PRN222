using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SelfStorageManagementSystem.Web.Pages;

public class LayoutTestModel : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Vui lòng nhập tên người dùng mẫu.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Tên người dùng phải từ 3 đến 50 ký tự.")]
    public string? SampleName { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng.")]
    public string? SampleEmail { get; set; }

    public decimal SampleRentAmount { get; set; } = 1500000m;
    public DateTimeOffset SampleStartDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset SampleEndDate { get; set; } = DateTimeOffset.UtcNow.AddMonths(1);

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Dữ liệu nhập vào chưa hợp lệ, vui lòng kiểm tra lại.";
            return Page();
        }

        TempData["SuccessMessage"] = $"Xác nhận thành công thông tin cho {SampleName} ({SampleEmail}).";
        return RedirectToPage("/LayoutTest");
    }
}
