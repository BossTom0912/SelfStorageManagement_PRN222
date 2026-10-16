using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.Reservations;

[Authorize(Policy = "CustomerOnly")]
public class CancelModel : PageModel
{
    private readonly IReservationService _reservationService;
    private readonly ILogger<CancelModel> _logger;

    public CancelModel(
        IReservationService reservationService,
        ILogger<CancelModel> logger)
    {
        _reservationService = reservationService ?? throw new ArgumentNullException(nameof(reservationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public long Id { get; set; }

    [BindProperty]
    [MaxLength(255, ErrorMessage = "Lý do hủy không được vượt quá 255 ký tự.")]
    public string? Reason { get; set; }

    public ReservationDetailResponse? Reservation { get; set; }

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
            Id = targetId;

            if (Reservation.Status == "cancelled")
            {
                TempData["InfoMessage"] = $"Đơn đặt chỗ {Reservation.ReservationCode} đã được hủy trước đó.";
                return RedirectToPage("Details", new { id = targetId });
            }

            if (!Reservation.CanCancel)
            {
                TempData["ErrorMessage"] = "Đơn đặt chỗ không ở trạng thái cho phép hủy (đã xác nhận thanh toán hoặc đã hết hạn giữ chỗ).";
                return RedirectToPage("Details", new { id = targetId });
            }

            return Page();
        }
        catch (NotFoundException)
        {
            TempData["ErrorMessage"] = $"Không tìm thấy đơn đặt chỗ #{targetId}.";
            return RedirectToPage("Index");
        }
        catch (ForbiddenException)
        {
            return Forbid();
        }
    }

    public async Task<IActionResult> OnPostAsync(long? id, CancellationToken cancellationToken)
    {
        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Challenge();
        }

        var targetId = id.HasValue && id.Value > 0 
            ? id.Value 
            : (Id > 0 ? Id : (long.TryParse(Request.Form["Id"], out var fId) ? fId : (long.TryParse(Request.Query["id"], out var qId) ? qId : 0)));

        if (targetId <= 0)
        {
            return RedirectToPage("Index");
        }
        Id = targetId;

        if (!ModelState.IsValid)
        {
            try
            {
                Reservation = await _reservationService.GetReservationByIdAsync(userId, roles, Id, cancellationToken);
                return Page();
            }
            catch
            {
                return RedirectToPage("Index");
            }
        }

        try
        {
            var request = new CancelReservationRequest { Reason = Reason };
            var cancelled = await _reservationService.CancelReservationAsync(userId, roles, Id, request, cancellationToken);

            TempData["SuccessMessage"] = $"Đơn đặt chỗ {cancelled.ReservationCode} đã được hủy thành công.";
            return RedirectToPage("Details", new { id = Id });
        }
        catch (ConflictException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("Details", new { id = Id });
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
            _logger.LogError(ex, "Lỗi xảy ra khi hủy đơn đặt chỗ {ReservationId}.", Id);
            TempData["ErrorMessage"] = "Đã xảy ra lỗi khi hủy đơn đặt chỗ. Vui lòng thử lại.";
            return RedirectToPage("Details", new { id = Id });
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
