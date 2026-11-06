using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.StorageUnits;

/// <summary>
/// PageModel cho trang chi tiết ô kho (/StorageUnits/Details).
/// Gọi BLL Service IStorageUnitAdminService.GetByIdAsync và hiển thị StorageUnitAdminDetailDto.
/// </summary>
[Authorize(Policy = "StaffOnly")]
public class DetailsModel : PageModel
{
    private readonly IStorageUnitAdminService _storageUnitAdminService;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(
        IStorageUnitAdminService storageUnitAdminService,
        ILogger<DetailsModel> logger)
    {
        _storageUnitAdminService = storageUnitAdminService ?? throw new ArgumentNullException(nameof(storageUnitAdminService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public StorageUnitAdminDetailDto StorageUnit { get; set; } = default!;

    public bool CanManage { get; set; } = true;

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long? id, CancellationToken cancellationToken)
    {
        if (id == null)
        {
            return NotFound();
        }

        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToPage("/Account/Login");
        }

        CheckUserRole();

        try
        {
            var unit = await _storageUnitAdminService.GetByIdAsync(id.Value, actorUserId, cancellationToken);
            if (unit == null)
            {
                return NotFound();
            }

            StorageUnit = unit;
            return Page();
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền truy cập chi tiết ô kho ID {UnitId} cho user {UserId}.", id, actorUserId);
            ErrorMessage = ex.Message;
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy chi tiết ô kho ID {UnitId}.", id);
            return NotFound();
        }
    }

    private void CheckUserRole()
    {
        CanManage = User.IsInRole(RoleConstants.FacilityManager) ||
                    User.IsInRole(RoleConstants.BusinessOperationsManager) ||
                    User.IsInRole(RoleConstants.SystemAdministrator);
    }

    private long GetActorUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdStr, out var id) ? id : 0;
    }
}
