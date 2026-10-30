using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Models;

namespace SelfStorageManagementSystem.Web.Controllers;

/// <summary>
/// Controller MVC xử lý báo cáo thống kê đơn đặt chỗ theo khoảng thời gian (StartDate - EndDate) cho ASM 03:
/// - Kế thừa Microsoft.AspNetCore.Mvc.Controller
/// - Sử dụng dependency injection IReservationService & IStorageUnitAdminService (tuyệt đối không gọi DbContext)
/// - Phân quyền StaffOnly
/// </summary>
[Authorize(Policy = "StaffOnly")]
public class ReportsController : Controller
{
    private readonly IReservationService _reservationService;
    private readonly IStorageUnitAdminService _storageUnitAdminService;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        IReservationService reservationService,
        IStorageUnitAdminService storageUnitAdminService,
        ILogger<ReportsController> logger)
    {
        _reservationService = reservationService ?? throw new ArgumentNullException(nameof(reservationService));
        _storageUnitAdminService = storageUnitAdminService ?? throw new ArgumentNullException(nameof(storageUnitAdminService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    public IActionResult Index()
    {
        return RedirectToAction(nameof(Reservations));
    }

    [HttpGet]
    [Route("Reports/Reservations")]
    public async Task<IActionResult> Reservations(
        DateOnly? startDate,
        DateOnly? endDate,
        long? facilityId,
        string? status,
        CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToAction("Login", "Account");
        }

        var roles = User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        var viewModel = new ReservationReportViewModel
        {
            StartDate = startDate,
            EndDate = endDate,
            FacilityId = facilityId,
            Status = status
        };

        try
        {
            var lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, null, cancellationToken);
            viewModel.FacilityOptions = new SelectList(lookups.Facilities, "Id", "Name", facilityId);

            var request = new ReservationReportRequest
            {
                StartDate = startDate,
                EndDate = endDate,
                FacilityId = facilityId,
                Status = status
            };

            viewModel.Report = await _reservationService.GetReservationReportAsync(actorUserId, roles, request, cancellationToken);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Truy cập bị từ chối khi xem báo cáo đặt chỗ đối với user {UserId}", actorUserId);
            return Forbid();
        }
        catch (BadRequestException ex)
        {
            viewModel.ErrorMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (Exception ex)
        {
            viewModel.ErrorMessage = "Đã xảy ra lỗi khi tạo báo cáo đặt chỗ.";
            _logger.LogError(ex, "Lỗi không xác định khi tải báo cáo đặt chỗ đối với user {UserId}", actorUserId);
        }

        return View("~/Views/Reports/Reservations.cshtml", viewModel);
    }

    private long GetActorUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idClaim, out var id) ? id : 0;
    }
}
