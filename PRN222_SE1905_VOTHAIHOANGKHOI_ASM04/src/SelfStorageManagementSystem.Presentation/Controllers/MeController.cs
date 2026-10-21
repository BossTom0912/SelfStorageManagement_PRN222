using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Presentation.Controllers;

[Route("api/me")]
[Authorize]
public class MeController : BaseController
{
    private readonly ICheckInService _checkInService;

    public MeController(ICheckInService checkInService)
    {
        _checkInService = checkInService ?? throw new ArgumentNullException(nameof(checkInService));
    }

    /// <summary>
    /// Lấy danh sách các ô kho và hợp đồng của khách hàng đang đăng nhập (có phân trang).
    /// </summary>
    [HttpGet("units")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CustomerUnitListItemResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyUnits(
        [FromQuery] PagedRequest pagedRequest,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await _checkInService.GetCustomerUnitsAsync(userId, pagedRequest, cancellationToken);
        return Ok(ApiResponse<PagedResult<CustomerUnitListItemResponse>>.Ok(result, "Lấy danh sách ô kho của khách hàng thành công."));
    }

    /// <summary>
    /// Xem chi tiết biên bản bàn giao của một hợp đồng thuê thuộc về khách hàng đang đăng nhập.
    /// </summary>
    [HttpGet("units/{agreementId:long}/handover")]
    [ProducesResponseType(typeof(ApiResponse<CustomerHandoverResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyHandover(
        long agreementId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await _checkInService.GetCustomerHandoverAsync(userId, agreementId, cancellationToken);
        return Ok(ApiResponse<CustomerHandoverResponse>.Ok(result, "Lấy thông tin biên bản bàn giao thành công."));
    }

    /// <summary>
    /// Lấy danh sách các thông báo in-app của khách hàng đang đăng nhập (có phân trang).
    /// </summary>
    [HttpGet("notifications")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CustomerNotificationResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] PagedRequest pagedRequest,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await _checkInService.GetCustomerNotificationsAsync(userId, pagedRequest, cancellationToken);
        return Ok(ApiResponse<PagedResult<CustomerNotificationResponse>>.Ok(result, "Lấy danh sách thông báo thành công."));
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
}
