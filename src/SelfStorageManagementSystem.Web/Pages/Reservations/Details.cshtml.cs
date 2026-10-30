using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.Web.Pages.Reservations;

[Authorize]
public class DetailsModel : PageModel
{
    private readonly IReservationService _reservationService;
    private readonly IWebRealtimePublisher? _realtimePublisher;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(
        IReservationService reservationService,
        ILogger<DetailsModel> logger,
        IWebRealtimePublisher? realtimePublisher = null)
    {
        _reservationService = reservationService ?? throw new ArgumentNullException(nameof(reservationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _realtimePublisher = realtimePublisher;
    }

    public ReservationDetailResponse? Reservation { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long? id, CancellationToken cancellationToken)
    {
        var targetId = id.HasValue && id.Value > 0 ? id.Value : (long.TryParse(Request.Query["id"], out var qId) ? qId : 0);
        if (targetId <= 0)
        {
            return RedirectToPage("Index");
        }

        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Challenge();
        }

        try
        {
            Reservation = await _reservationService.GetReservationByIdAsync(userId, roles, targetId, cancellationToken);
            return Page();
        }
        catch (NotFoundException)
        {
            ErrorMessage = $"Không tìm thấy đơn đặt chỗ với mã ID #{targetId}.";
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }
        catch (ForbiddenException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi tải chi tiết đơn đặt chỗ {ReservationId}.", targetId);
            ErrorMessage = "Đã xảy ra lỗi hệ thống khi tải thông tin đơn đặt chỗ. Vui lòng thử lại sau.";
            return Page();
        }
    }

    /// <summary>
    /// Endpoint AJAX kiểm tra trạng thái thật từ máy chủ khi đồng hồ đếm ngược chạm 00:00.
    /// Máy chủ là nguồn trạng thái duy nhất (Source of Truth), tự động thực hiện dynamic expiration check.
    /// </summary>
    public async Task<IActionResult> OnGetStatusAsync(long? id, CancellationToken cancellationToken)
    {
        var targetId = id.HasValue && id.Value > 0 ? id.Value : (long.TryParse(Request.Query["id"], out var qId) ? qId : 0);
        if (targetId <= 0)
        {
            return BadRequest(new { success = false, message = "Mã đơn không hợp lệ." });
        }

        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Unauthorized();
        }

        try
        {
            var res = await _reservationService.GetReservationByIdAsync(userId, roles, targetId, cancellationToken);
            return new JsonResult(new
            {
                success = true,
                reservationId = res.Id,
                status = res.Status,
                statusDisplayName = res.StatusDisplayName,
                isHoldActive = res.IsHoldActive,
                remainingSeconds = res.RemainingSeconds,
                canCancel = res.CanCancel,
                serverNow = res.ServerNow.ToString("o")
            });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi kiểm tra trạng thái AJAX cho đơn đặt chỗ {ReservationId}.", targetId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Lỗi máy chủ." });
        }
    }

    /// <summary>
    /// Thao tác hủy đơn đặt chỗ trực tiếp từ trang chi tiết hoặc Modal xác nhận.
    /// </summary>
    public async Task<IActionResult> OnPostCancelAsync(
        long? id,
        string? reason,
        CancellationToken cancellationToken)
    {
        var targetId = id.HasValue && id.Value > 0 
            ? id.Value 
            : (long.TryParse(Request.Form["id"], out var fId) ? fId : (long.TryParse(Request.Query["id"], out var qId) ? qId : 0));

        if (targetId <= 0)
        {
            return RedirectToPage("Index");
        }

        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Challenge();
        }

        try
        {
            var request = new CancelReservationRequest { Reason = reason };
            var cancelled = await _reservationService.CancelReservationAsync(userId, roles, targetId, request, cancellationToken);

            if (_realtimePublisher != null)
            {
                try
                {
                    await _realtimePublisher.PublishReservationChangedAsync(cancelled.Id, cancelled.FacilityId, cancelled.CustomerId, "cancelled", cancellationToken);
                    await _realtimePublisher.PublishCatalogInvalidatedAsync(cancelled.FacilityId, cancelled.UnitTypeId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (RealtimePublishDegradedException ex)
                {
                    _logger.LogWarning(ex, "Realtime publish degraded after cancelling reservation {ReservationId}", cancelled.Id);
                    TempData["WarningMessage"] = "Đơn đặt chỗ đã được hủy thành công trong hệ thống. Tuy nhiên, đồng bộ thời gian thực gặp sự cố tạm thời; thông tin sẽ tự động làm mới khi tải lại trang.";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unexpected realtime publish error after cancelling reservation {ReservationId}", cancelled.Id);
                    TempData["WarningMessage"] = "Đơn đặt chỗ đã được hủy thành công trong hệ thống, nhưng đồng bộ thời gian thực gặp sự cố.";
                }
            }

            TempData["SuccessMessage"] = $"Đơn đặt chỗ {cancelled.ReservationCode} đã được hủy thành công.";
            return RedirectToPage("Details", new { id = targetId });
        }
        catch (ConflictException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("Details", new { id });
        }
        catch (ForbiddenException)
        {
            return Forbid();
        }
        catch (NotFoundException)
        {
            TempData["ErrorMessage"] = "Không tìm thấy đơn đặt chỗ cần hủy.";
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi hủy đơn đặt chỗ {ReservationId}.", id);
            TempData["ErrorMessage"] = "Đã xảy ra lỗi khi hủy đơn đặt chỗ. Vui lòng thử lại.";
            return RedirectToPage("Details", new { id });
        }
    }

    private (long UserId, IReadOnlyList<string> Roles) GetCurrentUserInfo()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdClaim, out var userId))
        {
            return (0, Array.Empty<string>());
        }

        var roles = User.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        return (userId, roles);
    }
}
