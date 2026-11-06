using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.Reports;

[Authorize(Policy = "StaffOnly")]
public class ReservationsModel : PageModel
{
    private readonly IReservationService _reservationService;
    private readonly IStorageUnitAdminService _storageUnitAdminService;
    private readonly ILogger<ReservationsModel> _logger;

    public ReservationsModel(
        IReservationService reservationService,
        IStorageUnitAdminService storageUnitAdminService,
        ILogger<ReservationsModel> logger)
    {
        _reservationService = reservationService ?? throw new ArgumentNullException(nameof(reservationService));
        _storageUnitAdminService = storageUnitAdminService ?? throw new ArgumentNullException(nameof(storageUnitAdminService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? StartDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? EndDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? FacilityId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public ReservationReportResponse Report { get; set; } = new();

    public SelectList? FacilityOptions { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToPage("/Account/Login");
        }

        var roles = User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        try
        {
            // Tải danh sách cơ sở mà người dùng có quyền tiếp cận để hiển thị dropdown filter
            var lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, null, cancellationToken);
            FacilityOptions = new SelectList(lookups.Facilities, "Id", "Name", FacilityId);

            var request = new ReservationReportRequest
            {
                StartDate = StartDate,
                EndDate = EndDate,
                FacilityId = FacilityId,
                Status = Status
            };

            Report = await _reservationService.GetReservationReportAsync(actorUserId, roles, request, cancellationToken);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Truy cập bị từ chối khi xem báo cáo đặt chỗ đối với user {UserId}", actorUserId);
            return Forbid();
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Đã xảy ra lỗi khi tạo báo cáo đặt chỗ.";
            _logger.LogError(ex, "Lỗi không xác định khi tải báo cáo đặt chỗ đối với user {UserId}", actorUserId);
        }

        return Page();
    }

    private long GetActorUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idClaim, out var id) ? id : 0;
    }
}
