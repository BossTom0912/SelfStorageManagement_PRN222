using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.Web.Pages.StorageUnits;

/// <summary>
/// PageModel cho trang chỉnh sửa ô kho (/StorageUnits/Edit).
/// Refactor từ khung scaffold sang BLL Service IStorageUnitAdminService.UpdateAsync.
/// Giữ FacilityId và UnitTypeId bất biến; truyền ExpectedUpdatedAt dạng round-trip phục vụ kiểm soát xung đột đồng thời.
/// Phát sự kiện realtime thông qua IStorageUnitRealtimePublisher.
/// </summary>
[Authorize(Policy = "StaffOnly")]
public class EditModel : PageModel
{
    private readonly IStorageUnitAdminService _storageUnitAdminService;
    private readonly IStorageUnitRealtimePublisher _realtimePublisher;
    private readonly ILogger<EditModel> _logger;

    public EditModel(
        IStorageUnitAdminService storageUnitAdminService,
        IStorageUnitRealtimePublisher realtimePublisher,
        ILogger<EditModel> logger)
    {
        _storageUnitAdminService = storageUnitAdminService ?? throw new ArgumentNullException(nameof(storageUnitAdminService));
        _realtimePublisher = realtimePublisher ?? throw new ArgumentNullException(nameof(realtimePublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public long Id { get; set; }

    [BindProperty]
    public UpdateStorageUnitRequest Input { get; set; } = new();

    // Thông tin hiển thị cố định (bất biến theo quy chuẩn kiến trúc)
    public string FacilityName { get; set; } = string.Empty;
    public string FacilityCode { get; set; } = string.Empty;
    public long FacilityId { get; set; }
    public string UnitTypeName { get; set; } = string.Empty;
    public string UnitTypeCode { get; set; } = string.Empty;
    public long UnitTypeId { get; set; }
    public string PhysicalStatus { get; set; } = string.Empty;
    public bool IsListed { get; set; }

    public StorageUnitLookupsDto Lookups { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public bool IsConcurrencyConflict { get; set; }

    public DateTimeOffset? LatestDbUpdatedAt { get; set; }

    public bool CanManage { get; set; } = true;

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

            PopulateUnitDetails(unit);

            // Tải lookup khu vực thuộc cơ sở của ô kho
            Lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, unit.FacilityId, cancellationToken);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền truy cập khi tải ô kho ID {UnitId} cho user {UserId}.", id, actorUserId);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải dữ liệu ô kho ID {UnitId}.", id);
            ErrorMessage = "Không thể tải thông tin ô kho. Vui lòng thử lại sau.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToPage("/Account/Login");
        }

        CheckUserRole();

        if (!ModelState.IsValid)
        {
            var errors = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            _logger.LogWarning("Validation failed on Edit: {Errors}", errors);
            await ReloadStateAsync(Id, actorUserId, cancellationToken);
            return Page();
        }

        StorageUnitAdminDto updated;
        try
        {
            updated = await _storageUnitAdminService.UpdateAsync(Id, Input, actorUserId, cancellationToken);
        }
        catch (ConflictException ex)
        {
            // Xung đột phiên làm việc (stale data hoặc duplicate unit code hoặc concurrency timeout)
            _logger.LogWarning(ex, "Xung đột đồng thời khi cập nhật ô kho ID {UnitId}: {Message}", Id, ex.Message);
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            IsConcurrencyConflict = true;

            // Nạp lại dữ liệu hiện thời trong CSDL để người dùng đối chiếu dữ liệu stale
            await ReloadStateAsync(Id, actorUserId, cancellationToken);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền khi cập nhật ô kho ID {UnitId}: {Message}", Id, ex.Message);
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadStateAsync(Id, actorUserId, cancellationToken);
            return Page();
        }
        catch (NotFoundException)
        {
            TempData["ErrorMessage"] = $"Ô kho ID {Id} không tồn tại hoặc đã bị xóa.";
            return RedirectToPage("./Index");
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(ex, "Yêu cầu cập nhật không hợp lệ cho ô kho ID {UnitId}: {Message}", Id, ex.Message);
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadStateAsync(Id, actorUserId, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi bất ngờ khi cập nhật ô kho ID {UnitId}.", Id);
            ErrorMessage = "Đã xảy ra lỗi không mong muốn khi lưu thay đổi. Vui lòng thử lại.";
            ModelState.AddModelError(string.Empty, ErrorMessage);
            await ReloadStateAsync(Id, actorUserId, cancellationToken);
            return Page();
        }

        // T13-R6-F01: Thao tác DB đã lưu thành công. Xử lý đồng bộ realtime sau commit độc lập.
        try
        {
            await _realtimePublisher.PublishUpdatedAsync(updated, cancellationToken);
            TempData["SuccessMessage"] = $"Cập nhật thông tin ô kho '{updated.UnitCode}' thành công.";
            return RedirectToPage("./Index", new { facilityId = updated.FacilityId });
        }
        catch (RealtimePublishDegradedException ex)
        {
            _logger.LogWarning(ex, "Đã cập nhật ô kho '{UnitCode}' (ID: {UnitId}) thành công trong CSDL nhưng đồng bộ realtime bị suy giảm cho cơ sở {FacilityId}.", updated.UnitCode, updated.Id, updated.FacilityId);
            TempData["WarningMessage"] = $"Cập nhật ô kho '{updated.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực chưa thể đồng bộ tự động tới các màn hình khác. Vui lòng tải lại trang để xem cập nhật mới nhất.";
            return RedirectToPage("./Index", new { facilityId = updated.FacilityId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi ngoài dự kiến khi phát realtime cập nhật cho ô kho '{UnitCode}' (ID: {UnitId}).", updated.UnitCode, updated.Id);
            TempData["WarningMessage"] = $"Cập nhật ô kho '{updated.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực gặp sự cố khi phát tin. Vui lòng tải lại trang để cập nhật.";
            return RedirectToPage("./Index", new { facilityId = updated.FacilityId });
        }
    }

    private void PopulateUnitDetails(StorageUnitAdminDetailDto unit)
    {
        Id = unit.Id;
        FacilityId = unit.FacilityId;
        FacilityName = unit.FacilityName;
        FacilityCode = unit.FacilityCode;
        UnitTypeId = unit.UnitTypeId;
        UnitTypeName = unit.UnitTypeName;
        UnitTypeCode = unit.UnitTypeCode;
        PhysicalStatus = unit.PhysicalStatus;
        IsListed = unit.IsListed;

        Input.UnitCode = unit.UnitCode;
        Input.AreaId = unit.AreaId;
        Input.FloorLabel = unit.FloorLabel;
        Input.ZoneLabel = unit.ZoneLabel;
        Input.Notes = unit.Notes;
        Input.ExpectedUpdatedAt = unit.UpdatedAt;
        LatestDbUpdatedAt = unit.UpdatedAt;
    }

    private async Task ReloadStateAsync(long unitId, long actorUserId, CancellationToken cancellationToken)
    {
        try
        {
            var freshUnit = await _storageUnitAdminService.GetByIdAsync(unitId, actorUserId, cancellationToken);
            if (freshUnit != null)
            {
                FacilityId = freshUnit.FacilityId;
                FacilityName = freshUnit.FacilityName;
                FacilityCode = freshUnit.FacilityCode;
                UnitTypeId = freshUnit.UnitTypeId;
                UnitTypeName = freshUnit.UnitTypeName;
                UnitTypeCode = freshUnit.UnitTypeCode;
                PhysicalStatus = freshUnit.PhysicalStatus;
                IsListed = freshUnit.IsListed;
                LatestDbUpdatedAt = freshUnit.UpdatedAt;

                Lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, freshUnit.FacilityId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể nạp lại trạng thái cho ô kho ID {UnitId}.", unitId);
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
