using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.Admin.Accounts;

[Authorize(Policy = "AdminOnly")]
public class CreateModel : PageModel
{
    private readonly IAdminAccountService _adminAccountService;
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(
        IAdminAccountService adminAccountService,
        ILogger<CreateModel> logger)
    {
        _adminAccountService = adminAccountService ?? throw new ArgumentNullException(nameof(adminAccountService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public CreateStaffInput Input { get; set; } = new();

    public List<FacilityLookupDto> Facilities { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public class CreateStaffInput
    {
        [Required(ErrorMessage = "Email là bắt buộc.")]
        [EmailAddress(ErrorMessage = "Định dạng email không hợp lệ.")]
        [MaxLength(255, ErrorMessage = "Email không được vượt quá 255 ký tự.")]
        [Display(Name = "Địa chỉ Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu khởi tạo là bắt buộc.")]
        [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu khởi tạo")]
        public string Password { get; set; } = string.Empty;

        [MaxLength(255, ErrorMessage = "Số điện thoại không được vượt quá 255 ký tự.")]
        [Display(Name = "Số điện thoại")]
        public string? PhoneNumber { get; set; }

        [Required(ErrorMessage = "Họ và tên nhân viên là bắt buộc.")]
        [MaxLength(255, ErrorMessage = "Họ tên không được vượt quá 255 ký tự.")]
        [Display(Name = "Họ và tên")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã nhân viên là bắt buộc.")]
        [MaxLength(255, ErrorMessage = "Mã nhân viên không được vượt quá 255 ký tự.")]
        [Display(Name = "Mã nhân viên")]
        public string EmployeeCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ngày vào làm là bắt buộc.")]
        [Display(Name = "Ngày vào làm")]
        public DateOnly HireDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

        [Required(ErrorMessage = "Vai trò nhân viên là bắt buộc.")]
        [Display(Name = "Vai trò nhân sự")]
        public string RoleCode { get; set; } = RoleConstants.FacilityStaff;

        [Display(Name = "Cơ sở phân công ban đầu (Tùy chọn)")]
        public long? InitialFacilityId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken = default)
    {
        await LoadFacilitiesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken = default)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            await LoadFacilitiesAsync(cancellationToken);
            return Page();
        }

        var request = new CreateStaffAccountRequest
        {
            Email = Input.Email.Trim(),
            Password = Input.Password,
            PhoneNumber = string.IsNullOrWhiteSpace(Input.PhoneNumber) ? null : Input.PhoneNumber.Trim(),
            FullName = Input.FullName.Trim(),
            EmployeeCode = Input.EmployeeCode.Trim(),
            HireDate = Input.HireDate,
            RoleCode = Input.RoleCode.Trim(),
            InitialFacilityId = Input.InitialFacilityId
        };

        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var requestId = HttpContext.TraceIdentifier;

            var createdAccount = await _adminAccountService.CreateStaffAccountAsync(
                request,
                actorUserId,
                ip,
                requestId,
                cancellationToken);

            TempData["SuccessMessage"] = $"Tạo tài khoản nhân viên {createdAccount.Email} (Mã: {createdAccount.EmployeeCode}) thành công!";
            return RedirectToPage("./Details", new { id = createdAccount.Id });
        }
        catch (ConflictException ex)
        {
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi tạo tài khoản nhân viên cho email {Email}", Input.Email);
            ErrorMessage = "Đã xảy ra lỗi không mong muốn khi tạo tài khoản nhân viên. Vui lòng thử lại.";
        }

        await LoadFacilitiesAsync(cancellationToken);
        return Page();
    }

    private async Task LoadFacilitiesAsync(CancellationToken cancellationToken)
    {
        try
        {
            Facilities = await _adminAccountService.GetFacilitiesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi tải danh mục cơ sở cho trang tạo nhân viên.");
            Facilities = new List<FacilityLookupDto>();
        }
    }

    private long GetActorUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idClaim, out var id) ? id : 0;
    }
}
