using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Models;

namespace SelfStorageManagementSystem.Web.Controllers;

/// <summary>
/// Controller MVC xử lý quản lý đặt chỗ kho (Reservations) cho ASM 03:
/// - Kế thừa Microsoft.AspNetCore.Mvc.Controller
/// - Sử dụng Dependency Injection IReservationService (không gọi DbContext)
/// - Phân quyền đăng nhập Cookie Authentication
/// </summary>
[Authorize]
public class ReservationsController : Controller
{
    private readonly IReservationService _reservationService;
    private readonly ILogger<ReservationsController> _logger;

    public ReservationsController(
        IReservationService reservationService,
        ILogger<ReservationsController> logger)
    {
        _reservationService = reservationService ?? throw new ArgumentNullException(nameof(reservationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    [Route("Reservations")]
    public async Task<IActionResult> Index(
        string? status,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToAction("Login", "Account");
        }

        pageNumber = Math.Max(1, pageNumber);

        var request = new GetMyReservationsRequest
        {
            Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        PagedResult<ReservationListItemResponse> reservations;
        try
        {
            reservations = await _reservationService.GetMyReservationsAsync(actorUserId, request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi tải danh sách đơn đặt chỗ của user {UserId}.", actorUserId);
            reservations = new PagedResult<ReservationListItemResponse>(new List<ReservationListItemResponse>(), 0, pageNumber, pageSize);
        }

        ViewData["CurrentStatus"] = status;
        return View("~/Views/Reservations/Index.cshtml", reservations);
    }

    [HttpGet]
    [Route("Reservations/Details/{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken = default)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToAction("Login", "Account");
        }

        var roles = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

        try
        {
            var detail = await _reservationService.GetReservationByIdAsync(actorUserId, roles, id, cancellationToken);
            return View("~/Views/Reservations/Details.cshtml", detail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể tải chi tiết đặt chỗ ID {ReservationId}", id);
            return NotFound();
        }
    }

    private long GetActorUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idClaim, out var id) ? id : 0;
    }
}
