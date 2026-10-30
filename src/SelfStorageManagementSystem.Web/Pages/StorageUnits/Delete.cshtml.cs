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

using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.Web.Pages.StorageUnits;

/// <summary>
/// PageModel cho trang xác nhận xóa ô kho (/StorageUnits/Delete).
/// Quy chuẩn PRN222:
/// - GET chỉ hiển thị xác nhận và thông tin ô kho kèm kiểm tra điều kiện xóa; tuyệt đối không xóa dữ liệu tại GET.
/// - POST thực thi xóa nguyên tử thông qua BLL Service IStorageUnitAdminService.DeleteAsync.
/// - Truyền ExpectedUpdatedAt dạng round-trip để bảo vệ chống xóa đè khi dữ liệu đã stale.
/// - Bắt và hiển thị rõ ràng lỗi ConflictException, ForbiddenException, NotFoundException.
/// - Phát sự kiện realtime thông qua IStorageUnitRealtimePublisher.
/// </summary>
[Authorize(Policy = "StaffOnly")]
public class DeleteModel : PageModel
{
    private readonly IStorageUnitAdminService _storageUnitAdminService;
    private readonly IStorageUnitRealtimePublisher _realtimePublisher;
    private readonly ILogger<DeleteModel> _logger;

    public DeleteModel(
        IStorageUnitAdminService storageUnitAdminService,
        IStorageUnitRealtimePublisher realtimePublisher,
        ILogger<DeleteModel> logger)
    {
        _storageUnitAdminService = storageUnitAdminService ?? throw new ArgumentNullException(nameof(storageUnitAdminService));
        _realtimePublisher = realtimePublisher ?? throw new ArgumentNullException(nameof(realtimePublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public long Id { get; set; }

    [BindProperty]
    public DateTimeOffset ExpectedUpdatedAt { get; set; }

    public StorageUnitAdminDetailDto StorageUnit { get; set; } = default!;

    public bool CanDelete { get; set; } = true;

    public string? BlockReason { get; set; }

    public string? ErrorMessage { get; set; }

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

            StorageUnit = unit;
            Id = unit.Id;
            ExpectedUpdatedAt = unit.UpdatedAt;

            // Kiểm tra các điều kiện tiên quyết xóa ô kho an toàn
            EvaluatePreconditions(unit);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền truy cập xác nhận xóa ô kho ID {UnitId} cho user {UserId}.", id, actorUserId);
            ErrorMessage = ex.Message;
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy thông tin xác nhận xóa ô kho ID {UnitId}.", id);
            return NotFound();
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToPage("/Account/Login");
        }

        CheckUserRole();

        DeleteStorageUnitResult result;
        long facilityId = 0;
        try
        {
            var existingUnit = await _storageUnitAdminService.GetByIdAsync(Id, actorUserId, cancellationToken);
            facilityId = existingUnit?.FacilityId ?? 0;

            result = await _storageUnitAdminService.DeleteAsync(Id, ExpectedUpdatedAt, actorUserId, cancellationToken);
        }
        catch (ConflictException ex)
        {
            // Xung đột phiên làm việc, vi phạm tham chiếu hoặc cam kết sức chứa
            _logger.LogWarning(ex, "Xung đột khi xóa ô kho ID {UnitId}: {Message}", Id, ex.Message);
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);

            var freshUnit = await _storageUnitAdminService.GetByIdAsync(Id, actorUserId, cancellationToken);
            if (freshUnit == null)
            {
                TempData["WarningMessage"] = "Ô kho không còn tồn tại hoặc đã được xóa trước đó.";
                return RedirectToPage("./Index");
            }

            StorageUnit = freshUnit;
            ExpectedUpdatedAt = freshUnit.UpdatedAt;
            CanDelete = false;
            BlockReason = ex.Message;
            return Page();
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền khi xóa ô kho ID {UnitId}: {Message}", Id, ex.Message);
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            CanDelete = false;
            BlockReason = ex.Message;
            return Page();
        }
        catch (NotFoundException)
        {
            TempData["WarningMessage"] = "Ô kho không còn tồn tại hoặc đã được xóa trước đó.";
            return RedirectToPage("./Index");
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(ex, "Yêu cầu xóa không hợp lệ cho ô kho ID {UnitId}: {Message}", Id, ex.Message);
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            CanDelete = false;
            BlockReason = ex.Message;
            try
            {
                var freshUnit = await _storageUnitAdminService.GetByIdAsync(Id, actorUserId, cancellationToken);
                if (freshUnit != null)
                {
                    StorageUnit = freshUnit;
                    ExpectedUpdatedAt = freshUnit.UpdatedAt;
                }
            }
            catch
            {
                // Bỏ qua nếu không tải được chi tiết do quyền hạn
            }
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi bất ngờ khi xóa ô kho ID {UnitId}.", Id);
            ErrorMessage = "Đã xảy ra lỗi không mong muốn khi thực hiện thao tác xóa. Vui lòng thử lại.";
            ModelState.AddModelError(string.Empty, ErrorMessage);
            CanDelete = false;
            return Page();
        }

        // T13-R6-F01: Thao tác DB đã lưu thành công. Xử lý đồng bộ realtime sau commit độc lập.
        try
        {
            if (facilityId > 0)
            {
                await _realtimePublisher.PublishDeletedAsync(Id, facilityId, cancellationToken);
            }
            else
            {
                await _realtimePublisher.PublishDeletedAsync(Id, cancellationToken);
            }

            TempData["SuccessMessage"] = $"Xóa thành công ô kho '{result.UnitCode}' khỏi hệ thống.";
            return RedirectToPage("./Index");
        }
        catch (RealtimePublishDegradedException ex)
        {
            _logger.LogWarning(ex, "Đã xóa ô kho '{UnitCode}' (ID: {UnitId}) thành công trong CSDL nhưng đồng bộ realtime bị suy giảm cho cơ sở {FacilityId}.", result.UnitCode, Id, facilityId);
            TempData["WarningMessage"] = $"Xóa ô kho '{result.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực chưa thể đồng bộ tự động tới các màn hình khác. Vui lòng tải lại trang để xem cập nhật mới nhất.";
            return RedirectToPage("./Index");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi ngoài dự kiến khi phát realtime xóa cho ô kho '{UnitCode}' (ID: {UnitId}).", result.UnitCode, Id);
            TempData["WarningMessage"] = $"Xóa ô kho '{result.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực gặp sự cố khi phát tin. Vui lòng tải lại trang để cập nhật.";
            return RedirectToPage("./Index");
        }
    }

    private void EvaluatePreconditions(StorageUnitAdminDetailDto unit)
    {
        if (unit.PhysicalStatus != "available")
        {
            CanDelete = false;
            BlockReason = $"Ô kho đang ở trạng thái '{unit.PhysicalStatus}'. Theo quy chuẩn hệ thống, chỉ có thể xóa các ô kho ở trạng thái khả dụng ('available').";
            return;
        }

        if (unit.AllocationsCount > 0 || unit.InspectionsCount > 0 ||
            unit.MaintenanceOrdersCount > 0 || unit.SupportTicketsCount > 0)
        {
            CanDelete = false;
            BlockReason = $"Ô kho có dữ liệu lịch sử liên kết ({unit.AllocationsCount} lần gán thuê, {unit.InspectionsCount} lượt kiểm tra, {unit.MaintenanceOrdersCount} lệnh bảo trì, {unit.SupportTicketsCount} phiếu hỗ trợ). Vì lý do bảo toàn dữ liệu lịch sử và kiểm toán, ô kho không thể bị xóa.";
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
