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
public class DetailsModel : PageModel
{
    private readonly IAdminAccountService _adminAccountService;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(
        IAdminAccountService adminAccountService,
        ILogger<DetailsModel> logger)
    {
        _adminAccountService = adminAccountService ?? throw new ArgumentNullException(nameof(adminAccountService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public UserAccountDto? Account { get; set; }

    public List<FacilityLookupDto> Facilities { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    // Input Models
    [BindProperty]
    public UpdateStatusInput StatusInput { get; set; } = new();

    [BindProperty]
    public ManageRolesInput RolesInput { get; set; } = new();

    [BindProperty]
    public AssignFacilityInput AssignmentInput { get; set; } = new();

    public class UpdateStatusInput
    {
        [Required(ErrorMessage = "Trạng thái là bắt buộc.")]
        public string Status { get; set; } = string.Empty;
    }

    public class ManageRolesInput
    {
        public List<string> SelectedRoles { get; set; } = new();
    }

    public class AssignFacilityInput
    {
        [Required(ErrorMessage = "Vui lòng chọn cơ sở.")]
        public long FacilityId { get; set; }

        [Required(ErrorMessage = "Vai trò phân công là bắt buộc.")]
        public string AssignmentRole { get; set; } = RoleConstants.FacilityStaff;

        [Required(ErrorMessage = "Thời điểm bắt đầu là bắt buộc.")]
        public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? EndsAt { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        try
        {
            Account = await _adminAccountService.GetAccountByIdAsync(id, cancellationToken);
            Facilities = await _adminAccountService.GetFacilitiesAsync(cancellationToken);

            // Prepopulate form inputs
            StatusInput.Status = Account.Status;
            RolesInput.SelectedRoles = Account.Roles.ToList();
            AssignmentInput.StartsAt = DateTimeOffset.UtcNow;

            return Page();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi nạp thông tin tài khoản ID {UserId}", id);
            ErrorMessage = "Đã xảy ra lỗi khi tải thông tin tài khoản.";
            return RedirectToPage("./Index");
        }
    }

    public async Task<IActionResult> OnPostUpdateStatusAsync(long id, CancellationToken cancellationToken = default)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(StatusInput.Status))
        {
            ErrorMessage = "Trạng thái cập nhật không được để trống.";
            return RedirectToPage("./Details", new { id });
        }

        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var requestId = HttpContext.TraceIdentifier;

            var updated = await _adminAccountService.UpdateUserStatusAsync(
                id,
                new UpdateUserStatusRequest { Status = StatusInput.Status.Trim() },
                actorUserId,
                ip,
                requestId,
                cancellationToken);

            SuccessMessage = $"Cập nhật trạng thái tài khoản sang '{updated.Status}' thành công.";
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            await OnGetAsync(id, cancellationToken);
            return Page();
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            await OnGetAsync(id, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật trạng thái tài khoản {UserId}", id);
            ErrorMessage = "Không thể cập nhật trạng thái tài khoản. Vui lòng thử lại.";
            await OnGetAsync(id, cancellationToken);
            return Page();
        }

        return RedirectToPage("./Details", new { id });
    }

    public async Task<IActionResult> OnPostManageRolesAsync(long id, CancellationToken cancellationToken = default)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return Challenge();
        }

        if (RolesInput.SelectedRoles == null || RolesInput.SelectedRoles.Count == 0)
        {
            ErrorMessage = "Tài khoản phải có ít nhất một vai trò.";
            await OnGetAsync(id, cancellationToken);
            return Page();
        }

        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var requestId = HttpContext.TraceIdentifier;

            var updated = await _adminAccountService.ManageUserRolesAsync(
                id,
                new ManageUserRolesRequest { RoleCodes = RolesInput.SelectedRoles },
                actorUserId,
                ip,
                requestId,
                cancellationToken);

            SuccessMessage = $"Cập nhật vai trò cho tài khoản thành công: {string.Join(", ", updated.Roles)}.";
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            await OnGetAsync(id, cancellationToken);
            return Page();
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            await OnGetAsync(id, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật vai trò cho tài khoản {UserId}", id);
            ErrorMessage = "Không thể cập nhật vai trò. Vui lòng kiểm tra lại ràng buộc hệ thống.";
            await OnGetAsync(id, cancellationToken);
            return Page();
        }

        return RedirectToPage("./Details", new { id });
    }

    public async Task<IActionResult> OnPostAssignFacilityAsync(long id, CancellationToken cancellationToken = default)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return Challenge();
        }

        if (AssignmentInput.FacilityId <= 0)
        {
            ErrorMessage = "Vui lòng chọn cơ sở phân công.";
            await OnGetAsync(id, cancellationToken);
            return Page();
        }

        if (AssignmentInput.EndsAt.HasValue && AssignmentInput.EndsAt.Value <= AssignmentInput.StartsAt)
        {
            ErrorMessage = "Thời điểm kết thúc phân công phải sau thời điểm bắt đầu.";
            await OnGetAsync(id, cancellationToken);
            return Page();
        }

        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var requestId = HttpContext.TraceIdentifier;

            var created = await _adminAccountService.AssignFacilityAsync(
                id,
                new CreateFacilityAssignmentRequest
                {
                    FacilityId = AssignmentInput.FacilityId,
                    AssignmentRole = AssignmentInput.AssignmentRole.Trim(),
                    StartsAt = AssignmentInput.StartsAt,
                    EndsAt = AssignmentInput.EndsAt
                },
                actorUserId,
                ip,
                requestId,
                cancellationToken);

            SuccessMessage = $"Phân công nhân sự vào cơ sở '{created.FacilityName}' ({created.AssignmentRole}) thành công.";
        }
        catch (ConflictException ex)
        {
            ErrorMessage = ex.Message;
            await OnGetAsync(id, cancellationToken);
            return Page();
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            await OnGetAsync(id, cancellationToken);
            return Page();
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            await OnGetAsync(id, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi phân công cơ sở cho tài khoản {UserId}", id);
            ErrorMessage = "Không thể phân công cơ sở. Vui lòng kiểm tra lại thời gian hoặc vai trò.";
            await OnGetAsync(id, cancellationToken);
            return Page();
        }

        return RedirectToPage("./Details", new { id });
    }

    public async Task<IActionResult> OnPostTerminateAssignmentAsync(long id, long assignmentId, CancellationToken cancellationToken = default)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return Challenge();
        }

        if (assignmentId <= 0)
        {
            ErrorMessage = "ID phân công không hợp lệ.";
            await OnGetAsync(id, cancellationToken);
            return Page();
        }

        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var requestId = HttpContext.TraceIdentifier;

            await _adminAccountService.TerminateFacilityAssignmentAsync(
                assignmentId,
                actorUserId,
                ip,
                requestId,
                cancellationToken);

            SuccessMessage = $"Đã chấm dứt hiệu lực phân công cơ sở #{assignmentId} thành công.";
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            await OnGetAsync(id, cancellationToken);
            return Page();
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            await OnGetAsync(id, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi chấm dứt phân công {AssignmentId}", assignmentId);
            ErrorMessage = "Không thể chấm dứt phân công cơ sở.";
            await OnGetAsync(id, cancellationToken);
            return Page();
        }

        return RedirectToPage("./Details", new { id });
    }

    private long GetActorUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idClaim, out var id) ? id : 0;
    }
}
