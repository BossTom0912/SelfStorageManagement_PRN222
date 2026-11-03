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
/// PageModel cho trang tạo mới ô kho (/StorageUnits/Create).
/// Gọi BLL Service IStorageUnitAdminService.CreateAsync và ràng buộc CreateStorageUnitRequest DTO.
/// Phát sự kiện realtime thông qua IStorageUnitRealtimePublisher.
/// </summary>
[Authorize(Policy = "StaffOnly")]
public class CreateModel : PageModel
{
    private readonly IStorageUnitAdminService _storageUnitAdminService;
    private readonly IStorageUnitRealtimePublisher _realtimePublisher;
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(
        IStorageUnitAdminService storageUnitAdminService,
        IStorageUnitRealtimePublisher realtimePublisher,
        ILogger<CreateModel> logger)
    {
        _storageUnitAdminService = storageUnitAdminService ?? throw new ArgumentNullException(nameof(storageUnitAdminService));
        _realtimePublisher = realtimePublisher ?? throw new ArgumentNullException(nameof(realtimePublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public CreateStorageUnitRequest Input { get; set; } = new();

    public StorageUnitLookupsDto Lookups { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public bool CanManage { get; set; } = true;

    public async Task<IActionResult> OnGetAsync(long? facilityId, CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToPage("/Account/Login");
        }

        CheckUserRole();

        try
        {
            // Xác định cơ sở hiệu lực: từ query string hoặc tự động chọn nếu chỉ có duy nhất 1 cơ sở được phân công
            long? effectiveFacilityId = facilityId;
            if (!effectiveFacilityId.HasValue)
            {
                var initialLookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, null, cancellationToken);
                if (initialLookups.Facilities.Count == 1)
                {
                    effectiveFacilityId = initialLookups.Facilities[0].Id;
                }
            }

            // Tải Lookups với effectiveFacilityId để nạp đầy đủ Areas và UnitTypes của cơ sở đó
            Lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, effectiveFacilityId, cancellationToken);

            if (effectiveFacilityId.HasValue && Lookups.Facilities.Exists(f => f.Id == effectiveFacilityId.Value))
            {
                Input.FacilityId = effectiveFacilityId.Value;
            }
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Truy cập bị từ chối khi lấy lookups cho user {UserId}.", actorUserId);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải dữ liệu khởi tạo tạo ô kho.");
            ErrorMessage = "Không thể tải danh mục cơ sở. Vui lòng thử lại sau.";
        }

        return Page();
    }

    /// <summary>
    /// Handler AJAX nạp động danh mục UnitTypes và Areas khi người dùng chọn/đổi cơ sở trên form tạo mới.
    /// Giữ kiểm tra phân quyền và scope chặt chẽ tại BLL; không tin cậy dữ liệu client.
    /// </summary>
    public async Task<IActionResult> OnGetFacilityLookupsAsync(long facilityId, CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return Challenge();
        }

        try
        {
            var lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, facilityId, cancellationToken);
            return new JsonResult(new
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
            await ReloadLookupsAsync(actorUserId, cancellationToken);
            return Page();
        }

        StorageUnitAdminDto created;
        try
        {
            created = await _storageUnitAdminService.CreateAsync(Input, actorUserId, cancellationToken);
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning(ex, "Xung đột khi tạo ô kho: {Message}", ex.Message);
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadLookupsAsync(actorUserId, cancellationToken);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền khi tạo ô kho: {Message}", ex.Message);
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadLookupsAsync(actorUserId, cancellationToken);
            return Page();
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(ex, "Yêu cầu không hợp lệ khi tạo ô kho: {Message}", ex.Message);
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadLookupsAsync(actorUserId, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi tạo ô kho trong CSDL.");
            ErrorMessage = "Đã xảy ra lỗi không mong muốn khi tạo ô kho. Vui lòng thử lại.";
            ModelState.AddModelError(string.Empty, ErrorMessage);
            await ReloadLookupsAsync(actorUserId, cancellationToken);
            return Page();
        }

        // T13-R6-F01: Thao tác DB đã lưu thành công. Xử lý đồng bộ realtime sau commit độc lập.
        try
        {
            await _realtimePublisher.PublishCreatedAsync(created, cancellationToken);
            TempData["SuccessMessage"] = $"Tạo mới ô kho '{created.UnitCode}' tại cơ sở '{created.FacilityName}' thành công.";
            return RedirectToPage("./Index", new { facilityId = created.FacilityId });
        }
        catch (RealtimePublishDegradedException ex)
        {
            _logger.LogWarning(ex, "Đã tạo ô kho '{UnitCode}' (ID: {UnitId}) thành công trong CSDL nhưng đồng bộ realtime bị suy giảm cho cơ sở {FacilityId}.", created.UnitCode, created.Id, created.FacilityId);
            TempData["WarningMessage"] = $"Tạo mới ô kho '{created.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực chưa thể đồng bộ tự động tới các màn hình khác. Vui lòng tải lại trang để xem cập nhật mới nhất.";
            return RedirectToPage("./Index", new { facilityId = created.FacilityId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi ngoài dự kiến khi phát realtime tạo mới cho ô kho '{UnitCode}' (ID: {UnitId}).", created.UnitCode, created.Id);
            TempData["WarningMessage"] = $"Tạo mới ô kho '{created.UnitCode}' thành công trong cơ sở dữ liệu, nhưng thông báo thời gian thực gặp sự cố khi phát tin. Vui lòng tải lại trang để cập nhật.";
            return RedirectToPage("./Index", new { facilityId = created.FacilityId });
        }
    }

    private async Task ReloadLookupsAsync(long actorUserId, CancellationToken cancellationToken)
    {
        try
        {
            var facilityId = Input.FacilityId > 0 ? Input.FacilityId : (long?)null;
            Lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, facilityId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể tải lại lookups.");
            Lookups = new StorageUnitLookupsDto();
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
