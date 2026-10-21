using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Presentation.Controllers;

[Route("api/[controller]")]
[Authorize]
public class ReservationsController : BaseController
{
    private readonly IReservationService _reservationService;

    public ReservationsController(IReservationService reservationService)
    {
        _reservationService = reservationService ?? throw new ArgumentNullException(nameof(reservationService));
    }

    /// <summary>
    /// Tạo đơn đặt chỗ mới theo Loại Kho và tạm giữ sức chứa trong tối đa 15 phút (BR-RSV-01, BR-RSV-03).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = RoleConstants.StorageCustomer)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateReservation(
        [FromBody] CreateReservationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await _reservationService.CreateReservationHoldAsync(userId, request, cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<ReservationDetailResponse>.Ok(
                result,
                "Đã khởi tạo đơn đặt chỗ và tạm giữ sức chứa thành công trong 15 phút."));
    }

    /// <summary>
    /// Xem chi tiết đơn đặt chỗ và đồng hồ đếm ngược thời gian giữ chỗ.
    /// Quyền xem: Chủ đơn (Customer) hoặc Nhân viên/Quản lý có phân công tại cơ sở, hoặc Quản trị viên.
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<ReservationDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReservationById(
        long id,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _reservationService.GetReservationByIdAsync(userId, roles, id, cancellationToken);
        return Ok(ApiResponse<ReservationDetailResponse>.Ok(result, "Lấy thông tin chi tiết đơn đặt chỗ thành công."));
    }

    /// <summary>
    /// Xem chi tiết báo giá thanh toán kỳ đầu và mã ưu đãi trước khi checkout (Function 4.1).
    /// </summary>
    [HttpGet("{id:long}/checkout-quote")]
    [ProducesResponseType(typeof(ApiResponse<CheckoutQuoteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetCheckoutQuote(
        long id,
        [FromQuery] string? promotionCode,
        [FromServices] IPaymentService paymentService,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await paymentService.GetCheckoutQuoteAsync(userId, roles, id, promotionCode, cancellationToken);
        return Ok(ApiResponse<CheckoutQuoteResponse>.Ok(result, "Tải thông tin báo giá thanh toán thành công."));
    }

    /// <summary>
    /// Tra cứu danh sách các đơn đặt chỗ của chính khách hàng đang đăng nhập (phân trang).
    /// </summary>
    [HttpGet("mine")]
    [Authorize(Roles = RoleConstants.StorageCustomer)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ReservationListItemResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyReservations(
        [FromQuery] GetMyReservationsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await _reservationService.GetMyReservationsAsync(userId, request, cancellationToken);
        return Ok(ApiResponse<PagedResult<ReservationListItemResponse>>.Ok(result, "Danh sách đơn đặt chỗ của bạn đã được tải thành công."));
    }

    /// <summary>
    /// Khách hàng chủ động hủy đơn giữ chỗ khi chưa hoàn tất thanh toán hoặc hủy trước thời hạn.
    /// Thao tác an toàn lặp lại (idempotent).
    /// </summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<ReservationDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelReservation(
        long id,
        [FromBody] CancelReservationRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _reservationService.CancelReservationAsync(userId, roles, id, request, cancellationToken);
        return Ok(ApiResponse<ReservationDetailResponse>.Ok(result, "Hủy đơn đặt chỗ thành công."));
    }

    private long GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Phiên đăng nhập không hợp lệ hoặc thông tin người dùng không được tìm thấy.");
        }
        return userId;
    }

    private IReadOnlyList<string> GetCurrentUserRoles()
    {
        return User.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();
    }
}
