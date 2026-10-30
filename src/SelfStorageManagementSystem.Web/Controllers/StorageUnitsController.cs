using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Models;
using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.Web.Controllers;

/// <summary>
/// MVC Controller cho quản trị ô kho (StorageUnits).
/// - Thực hiện chuyển đổi giao diện nghiệp vụ StorageUnits từ Razor Pages sang MVC theo yêu cầu nghiệm thu ASM03.
/// - Tuyệt đối không gọi DbContext; chỉ thao tác thông qua IStorageUnitKhoiVTHService.
/// - Đảm bảo phân quyền Policy StaffOnly, kiểm tra role/facility scope và anti-forgery token trên các thao tác POST.
/// - Tích hợp IStorageUnitRealtimePublisher phát sự kiện SignalR CRUD theo chuẩn TeacherCode & PRN222.
/// </summary>
[Authorize(Policy = "StaffOnly")]
public class StorageUnitsController : Controller
{
    private readonly IStorageUnitKhoiVTHService _storageUnitAdminService;
    private readonly IStorageUnitRealtimePublisher _realtimePublisher;
    private readonly ILogger<StorageUnitsController> _logger;

    public StorageUnitsController(
        IStorageUnitKhoiVTHService storageUnitAdminService,
        IStorageUnitRealtimePublisher realtimePublisher,
        ILogger<StorageUnitsController> logger)
    {
        _storageUnitAdminService = storageUnitAdminService ?? throw new ArgumentNullException(nameof(storageUnitAdminService));
        _realtimePublisher = realtimePublisher ?? throw new ArgumentNullException(nameof(realtimePublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] StorageUnitFilterRequest filter, CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToAction("Login", "Account");
        }

        var canManageUnits = CanManageUnitsRole();
        if (filter.PageNumber < 1) filter.PageNumber = 1;
        if (filter.PageSize < 1) filter.PageSize = 10;

        var viewModel = new StorageUnitsIndexViewModel
        {
            Filter = filter,
            CanManageUnits = canManageUnits
        };

        try
        {
            viewModel.Lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, filter.FacilityId, cancellationToken);
            viewModel.StorageUnits = await _storageUnitAdminService.ListAsync(filter, actorUserId, cancellationToken);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Truy cập bị từ chối khi tải danh sách ô kho cho user {UserId}.", actorUserId);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi bất ngờ khi tải danh sách ô kho cho user {UserId}.", actorUserId);
            viewModel.ErrorMessage = "Không thể tải danh sách ô kho vào lúc này. Vui lòng thử lại sau.";
            viewModel.StorageUnits = new PagedResult<StorageUnitAdminDto>(Array.Empty<StorageUnitAdminDto>(), 0, filter.PageNumber, filter.PageSize);
        }

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Details(long? id, CancellationToken cancellationToken)
    {
        if (id == null)
        {
            return NotFound();
        }

        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToAction("Login", "Account");
        }

        var viewModel = new StorageUnitDetailsViewModel
        {
            CanManage = CanManageUnitsRole()
        };

        try
        {
            var unit = await _storageUnitAdminService.GetByIdAsync(id.Value, actorUserId, cancellationToken);
            if (unit == null)
            {
                return NotFound();
            }

            viewModel.StorageUnit = unit;
            return View(viewModel);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền truy cập chi tiết ô kho ID {UnitId} cho user {UserId}.", id, actorUserId);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy chi tiết ô kho ID {UnitId}.", id);
            return NotFound();
        }
    }

    [HttpGet]
    public async Task<IActionResult> Create(long? facilityId, [FromQuery] string? handler, CancellationToken cancellationToken)
    {
        if (string.Equals(handler, "FacilityLookups", StringComparison.OrdinalIgnoreCase) && facilityId.HasValue)
        {
            return await FacilityLookups(facilityId.Value, cancellationToken);
        }

        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToAction("Login", "Account");
        }

        var viewModel = new StorageUnitCreateViewModel
        {
            CanManage = CanManageUnitsRole()
        };

        try
        {
            long? effectiveFacilityId = facilityId;
            if (!effectiveFacilityId.HasValue)
            {
                var initialLookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, null, cancellationToken);
                if (initialLookups.Facilities.Count == 1)
                {
                    effectiveFacilityId = initialLookups.Facilities[0].Id;
                }
            }

            viewModel.Lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, effectiveFacilityId, cancellationToken);
            if (effectiveFacilityId.HasValue && viewModel.Lookups.Facilities.Exists(f => f.Id == effectiveFacilityId.Value))
            {
                viewModel.Input.FacilityId = effectiveFacilityId.Value;
            }
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Truy cập bị từ chối khi lấy lookups cho user {UserId}.", actorUserId);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải dữ liệu khởi tạo tạo ô kho.");
            viewModel.ErrorMessage = "Không thể tải danh mục cơ sở. Vui lòng thử lại sau.";
        }

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> FacilityLookups(long facilityId, CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return Challenge();
        }

        try
        {
            var lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, facilityId, cancellationToken);
            return Json(new
            {
                unitTypes = lookups.UnitTypes.Select(ut => new
                {
                    id = ut.Id,
                    name = ut.Name,
                    code = ut.Code,
                    dimensions = $"{ut.WidthMeters}x{ut.LengthMeters}x{ut.HeightMeters} m"
                }),
                areas = lookups.Areas.Select(a => new
                {
                    id = a.Id,
                    name = a.Name,
                    code = a.Code
                })
            });
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền truy cập lookups cơ sở {FacilityId} cho user {UserId}.", facilityId, actorUserId);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi nạp lookups động cho cơ sở {FacilityId}.", facilityId);
            return StatusCode(500, "Lỗi nạp danh mục cơ sở.");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StorageUnitCreateViewModel model, CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToAction("Login", "Account");
        }

        model.CanManage = CanManageUnitsRole();

        if (!CanManageUnitsRole())
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            await ReloadCreateLookupsAsync(model, actorUserId, cancellationToken);
            return View(model);
        }

        StorageUnitAdminDto created;
        try
        {
            created = await _storageUnitAdminService.CreateAsync(model.Input, actorUserId, cancellationToken);
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning(ex, "Xung đột khi tạo ô kho: {Message}", ex.Message);
            model.ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadCreateLookupsAsync(model, actorUserId, cancellationToken);
            return View(model);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền khi tạo ô kho: {Message}", ex.Message);
            return Forbid();
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(ex, "Yêu cầu không hợp lệ khi tạo ô kho: {Message}", ex.Message);
            model.ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadCreateLookupsAsync(model, actorUserId, cancellationToken);
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi tạo ô kho trong CSDL.");
            model.ErrorMessage = "Đã xảy ra lỗi không mong muốn khi tạo ô kho. Vui lòng thử lại.";
            ModelState.AddModelError(string.Empty, model.ErrorMessage);
            await ReloadCreateLookupsAsync(model, actorUserId, cancellationToken);
            return View(model);
        }

        try
        {
            await _realtimePublisher.PublishCreatedAsync(created, cancellationToken);
            TempData["SuccessMessage"] = $"Tạo mới ô kho '{created.UnitCode}' tại cơ sở '{created.FacilityName}' thành công.";
            return RedirectToAction(nameof(Index), new { facilityId = created.FacilityId });
        }
        catch (RealtimePublishDegradedException ex)
        {
            _logger.LogWarning(ex, "Đã tạo ô kho '{UnitCode}' (ID: {UnitId}) thành công trong CSDL nhưng đồng bộ realtime bị suy giảm cho cơ sở {FacilityId}.", created.UnitCode, created.Id, created.FacilityId);
            TempData["WarningMessage"] = $"Tạo mới ô kho '{created.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực chưa thể đồng bộ tự động tới các màn hình khác. Vui lòng tải lại trang để xem cập nhật mới nhất.";
            return RedirectToAction(nameof(Index), new { facilityId = created.FacilityId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi ngoài dự kiến khi phát realtime tạo mới cho ô kho '{UnitCode}' (ID: {UnitId}).", created.UnitCode, created.Id);
            TempData["WarningMessage"] = $"Tạo mới ô kho '{created.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực gặp sự cố khi phát tin. Vui lòng tải lại trang để cập nhật.";
            return RedirectToAction(nameof(Index), new { facilityId = created.FacilityId });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(long? id, CancellationToken cancellationToken)
    {
        if (id == null)
        {
            return NotFound();
        }

        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToAction("Login", "Account");
        }

        var viewModel = new StorageUnitEditViewModel
        {
            CanManage = CanManageUnitsRole()
        };

        try
        {
            var unit = await _storageUnitAdminService.GetByIdAsync(id.Value, actorUserId, cancellationToken);
            if (unit == null)
            {
                return NotFound();
            }

            PopulateEditDetails(viewModel, unit);
            viewModel.Lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, unit.FacilityId, cancellationToken);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền truy cập khi tải ô kho ID {UnitId} cho user {UserId}.", id, actorUserId);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải dữ liệu ô kho ID {UnitId}.", id);
            viewModel.ErrorMessage = "Không thể tải thông tin ô kho. Vui lòng thử lại sau.";
        }

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(long id, StorageUnitEditViewModel model, CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToAction("Login", "Account");
        }

        model.Id = id;
        model.CanManage = CanManageUnitsRole();

        if (!CanManageUnitsRole())
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            await ReloadEditStateAsync(model, id, actorUserId, cancellationToken);
            return View(model);
        }

        StorageUnitAdminDto updated;
        try
        {
            updated = await _storageUnitAdminService.UpdateAsync(id, model.Input, actorUserId, cancellationToken);
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning(ex, "Xung đột đồng thời khi cập nhật ô kho ID {UnitId}: {Message}", id, ex.Message);
            model.ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            model.IsConcurrencyConflict = true;
            await ReloadEditStateAsync(model, id, actorUserId, cancellationToken);
            return View(model);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền khi cập nhật ô kho ID {UnitId}: {Message}", id, ex.Message);
            return Forbid();
        }
        catch (NotFoundException)
        {
            TempData["ErrorMessage"] = $"Ô kho ID {id} không tồn tại hoặc đã bị xóa.";
            return RedirectToAction(nameof(Index));
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(ex, "Yêu cầu cập nhật không hợp lệ cho ô kho ID {UnitId}: {Message}", id, ex.Message);
            model.ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadEditStateAsync(model, id, actorUserId, cancellationToken);
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi bất ngờ khi cập nhật ô kho ID {UnitId}.", id);
            model.ErrorMessage = "Đã xảy ra lỗi không mong muốn khi lưu thay đổi. Vui lòng thử lại.";
            ModelState.AddModelError(string.Empty, model.ErrorMessage);
            await ReloadEditStateAsync(model, id, actorUserId, cancellationToken);
            return View(model);
        }

        try
        {
            await _realtimePublisher.PublishUpdatedAsync(updated, cancellationToken);
            TempData["SuccessMessage"] = $"Cập nhật thông tin ô kho '{updated.UnitCode}' thành công.";
            return RedirectToAction(nameof(Index), new { facilityId = updated.FacilityId });
        }
        catch (RealtimePublishDegradedException ex)
        {
            _logger.LogWarning(ex, "Đã cập nhật ô kho '{UnitCode}' (ID: {UnitId}) thành công trong CSDL nhưng đồng bộ realtime bị suy giảm cho cơ sở {FacilityId}.", updated.UnitCode, updated.Id, updated.FacilityId);
            TempData["WarningMessage"] = $"Cập nhật ô kho '{updated.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực chưa thể đồng bộ tự động tới các màn hình khác. Vui lòng tải lại trang để xem cập nhật mới nhất.";
            return RedirectToAction(nameof(Index), new { facilityId = updated.FacilityId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi ngoài dự kiến khi phát realtime cập nhật cho ô kho '{UnitCode}' (ID: {UnitId}).", updated.UnitCode, updated.Id);
            TempData["WarningMessage"] = $"Cập nhật ô kho '{updated.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực gặp sự cố khi phát tin. Vui lòng tải lại trang để cập nhật.";
            return RedirectToAction(nameof(Index), new { facilityId = updated.FacilityId });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Delete(long? id, CancellationToken cancellationToken)
    {
        if (id == null)
        {
            return NotFound();
        }

        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToAction("Login", "Account");
        }

        var viewModel = new StorageUnitDeleteViewModel
        {
            CanManage = CanManageUnitsRole()
        };

        try
        {
            var unit = await _storageUnitAdminService.GetByIdAsync(id.Value, actorUserId, cancellationToken);
            if (unit == null)
            {
                return NotFound();
            }

            viewModel.StorageUnit = unit;
            viewModel.Id = unit.Id;
            viewModel.ExpectedUpdatedAt = unit.UpdatedAt;

            EvaluateDeletePreconditions(viewModel, unit);
            return View(viewModel);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền truy cập xác nhận xóa ô kho ID {UnitId} cho user {UserId}.", id, actorUserId);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy thông tin xác nhận xóa ô kho ID {UnitId}.", id);
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id, DateTimeOffset expectedUpdatedAt, CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToAction("Login", "Account");
        }

        var viewModel = new StorageUnitDeleteViewModel
        {
            Id = id,
            ExpectedUpdatedAt = expectedUpdatedAt,
            CanManage = CanManageUnitsRole()
        };

        if (!CanManageUnitsRole())
        {
            return Forbid();
        }

        DeleteStorageUnitResult result;
        long facilityId = 0;
        try
        {
            var existingUnit = await _storageUnitAdminService.GetByIdAsync(id, actorUserId, cancellationToken);
            facilityId = existingUnit?.FacilityId ?? 0;

            result = await _storageUnitAdminService.DeleteAsync(id, expectedUpdatedAt, actorUserId, cancellationToken);
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning(ex, "Xung đột khi xóa ô kho ID {UnitId}: {Message}", id, ex.Message);
            viewModel.ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);

            var freshUnit = await _storageUnitAdminService.GetByIdAsync(id, actorUserId, cancellationToken);
            if (freshUnit == null)
            {
                TempData["WarningMessage"] = "Ô kho không còn tồn tại hoặc đã được xóa trước đó.";
                return RedirectToAction(nameof(Index));
            }

            viewModel.StorageUnit = freshUnit;
            viewModel.ExpectedUpdatedAt = freshUnit.UpdatedAt;
            viewModel.CanDelete = false;
            viewModel.BlockReason = ex.Message;
            return View(viewModel);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền khi xóa ô kho ID {UnitId}: {Message}", id, ex.Message);
            return Forbid();
        }
        catch (NotFoundException)
        {
            TempData["WarningMessage"] = "Ô kho không còn tồn tại hoặc đã được xóa trước đó.";
            return RedirectToAction(nameof(Index));
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(ex, "Yêu cầu xóa không hợp lệ cho ô kho ID {UnitId}: {Message}", id, ex.Message);
            viewModel.ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            viewModel.CanDelete = false;
            viewModel.BlockReason = ex.Message;
            try
            {
                var freshUnit = await _storageUnitAdminService.GetByIdAsync(id, actorUserId, cancellationToken);
                if (freshUnit != null)
                {
                    viewModel.StorageUnit = freshUnit;
                    viewModel.ExpectedUpdatedAt = freshUnit.UpdatedAt;
                }
            }
            catch
            {
                // Bỏ qua nếu không tải được chi tiết
            }
            return View(viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi bất ngờ khi xóa ô kho ID {UnitId}.", id);
            viewModel.ErrorMessage = "Đã xảy ra lỗi không mong muốn khi thực hiện thao tác xóa. Vui lòng thử lại.";
            ModelState.AddModelError(string.Empty, viewModel.ErrorMessage);
            viewModel.CanDelete = false;
            return View(viewModel);
        }

        try
        {
            if (result.IsUnlistedOnly)
            {
                var fresh = await _storageUnitAdminService.GetByIdAsync(id, actorUserId, cancellationToken);
                if (fresh != null)
                {
                    await _realtimePublisher.PublishUpdatedAsync(new StorageUnitAdminDto
                    {
                        Id = fresh.Id,
                        FacilityId = fresh.FacilityId,
                        FacilityCode = fresh.FacilityCode,
                        FacilityName = fresh.FacilityName,
                        UnitTypeId = fresh.UnitTypeId,
                        UnitTypeCode = fresh.UnitTypeCode,
                        UnitTypeName = fresh.UnitTypeName,
                        AreaId = fresh.AreaId,
                        AreaName = fresh.AreaName,
                        UnitCode = fresh.UnitCode,
                        FloorLabel = fresh.FloorLabel,
                        ZoneLabel = fresh.ZoneLabel,
                        PhysicalStatus = fresh.PhysicalStatus,
                        IsListed = fresh.IsListed,
                        Notes = fresh.Notes,
                        CreatedAt = fresh.CreatedAt,
                        UpdatedAt = fresh.UpdatedAt
                    }, cancellationToken);
                }
                TempData["SuccessMessage"] = $"Ô kho '{result.UnitCode}' có dữ liệu liên kết nên đã được chuyển sang trạng thái ngừng niêm yết.";
            }
            else if (facilityId > 0)
            {
                await _realtimePublisher.PublishDeletedAsync(id, facilityId, cancellationToken);
                TempData["SuccessMessage"] = $"Xóa thành công ô kho '{result.UnitCode}' khỏi hệ thống.";
            }
            else
            {
                await _realtimePublisher.PublishDeletedAsync(id, cancellationToken);
                TempData["SuccessMessage"] = $"Xóa thành công ô kho '{result.UnitCode}' khỏi hệ thống.";
            }
            return RedirectToAction(nameof(Index));
        }
        catch (RealtimePublishDegradedException ex)
        {
            _logger.LogWarning(ex, "Đã xóa ô kho '{UnitCode}' (ID: {UnitId}) thành công trong CSDL nhưng đồng bộ realtime bị suy giảm cho cơ sở {FacilityId}.", result.UnitCode, id, facilityId);
            TempData["WarningMessage"] = $"Xóa ô kho '{result.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực chưa thể đồng bộ tự động tới các màn hình khác. Vui lòng tải lại trang để xem cập nhật mới nhất.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi ngoài dự kiến khi phát realtime xóa cho ô kho '{UnitCode}' (ID: {UnitId}).", result.UnitCode, id);
            TempData["WarningMessage"] = $"Xóa ô kho '{result.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực gặp sự cố khi phát tin. Vui lòng tải lại trang để cập nhật.";
            return RedirectToAction(nameof(Index));
        }
    }

    private static void EvaluateDeletePreconditions(StorageUnitDeleteViewModel viewModel, StorageUnitAdminDetailDto unit)
    {
        if (unit.PhysicalStatus != "available")
        {
            viewModel.CanDelete = false;
            viewModel.BlockReason = $"Ô kho đang ở trạng thái '{unit.PhysicalStatus}'. Theo quy chuẩn hệ thống, chỉ có thể xóa các ô kho ở trạng thái khả dụng ('available').";
            return;
        }

        // Related records are retained by the service; the unit is marked unlisted.
    }

    private static void PopulateEditDetails(StorageUnitEditViewModel viewModel, StorageUnitAdminDetailDto unit)
    {
        viewModel.Id = unit.Id;
        viewModel.FacilityId = unit.FacilityId;
        viewModel.FacilityName = unit.FacilityName;
        viewModel.FacilityCode = unit.FacilityCode;
        viewModel.UnitTypeId = unit.UnitTypeId;
        viewModel.UnitTypeName = unit.UnitTypeName;
        viewModel.UnitTypeCode = unit.UnitTypeCode;
        viewModel.PhysicalStatus = unit.PhysicalStatus;
        viewModel.IsListed = unit.IsListed;

        viewModel.Input.UnitCode = unit.UnitCode;
        viewModel.Input.AreaId = unit.AreaId;
        viewModel.Input.FloorLabel = unit.FloorLabel;
        viewModel.Input.ZoneLabel = unit.ZoneLabel;
        viewModel.Input.Notes = unit.Notes;
        viewModel.Input.ExpectedUpdatedAt = unit.UpdatedAt;
        viewModel.LatestDbUpdatedAt = unit.UpdatedAt;
    }

    private async Task ReloadCreateLookupsAsync(StorageUnitCreateViewModel model, long actorUserId, CancellationToken cancellationToken)
    {
        try
        {
            var facilityId = model.Input.FacilityId > 0 ? model.Input.FacilityId : (long?)null;
            model.Lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, facilityId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể tải lại lookups.");
            model.Lookups = new StorageUnitLookupsDto();
        }
    }

    private async Task ReloadEditStateAsync(StorageUnitEditViewModel model, long unitId, long actorUserId, CancellationToken cancellationToken)
    {
        try
        {
            var freshUnit = await _storageUnitAdminService.GetByIdAsync(unitId, actorUserId, cancellationToken);
            if (freshUnit != null)
            {
                model.FacilityId = freshUnit.FacilityId;
                model.FacilityName = freshUnit.FacilityName;
                model.FacilityCode = freshUnit.FacilityCode;
                model.UnitTypeId = freshUnit.UnitTypeId;
                model.UnitTypeName = freshUnit.UnitTypeName;
                model.UnitTypeCode = freshUnit.UnitTypeCode;
                model.PhysicalStatus = freshUnit.PhysicalStatus;
                model.IsListed = freshUnit.IsListed;
                model.LatestDbUpdatedAt = freshUnit.UpdatedAt;

                model.Lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, freshUnit.FacilityId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể nạp lại trạng thái cho ô kho ID {UnitId}.", unitId);
        }
    }

    private bool CanManageUnitsRole()
    {
        return User.IsInRole(RoleConstants.FacilityManager) ||
               User.IsInRole(RoleConstants.BusinessOperationsManager) ||
               User.IsInRole(RoleConstants.SystemAdministrator);
    }

    private long GetActorUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdStr, out var id) ? id : 0;
    }
}
