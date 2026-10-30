using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Presentation.Controllers;

[Route("api/staff/check-in")]
[Authorize(Roles = $"{RoleConstants.FacilityStaff},{RoleConstants.FacilityManager}")]
public class CheckInController : BaseController
{
    private readonly ICheckInService _checkInService;

    public CheckInController(ICheckInService checkInService)
    {
        _checkInService = checkInService ?? throw new ArgumentNullException(nameof(checkInService));
    }

    /// <summary>
    /// Tra cứu thông tin đặt chỗ và tiến độ check-in tại quầy theo mã đặt chỗ hoặc hợp đồng.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(ApiResponse<CheckInSearchResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SearchCheckIn(
        [FromQuery] string? code,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _checkInService.SearchCheckInAsync(code ?? string.Empty, userId, roles, cancellationToken);
        return Ok(ApiResponse<CheckInSearchResponse>.Ok(result, "Tra cứu thông tin check-in thành công."));
    }

    /// <summary>
    /// Lấy danh sách các ô kho khả dụng và hợp lệ để gán cho đơn đặt chỗ.
    /// </summary>
    [HttpGet("{reservationId:long}/eligible-units")]
    [ProducesResponseType(typeof(ApiResponse<List<EligibleUnitResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEligibleUnits(
        long reservationId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _checkInService.GetEligibleUnitsAsync(reservationId, userId, roles, cancellationToken);
        return Ok(ApiResponse<List<EligibleUnitResponse>>.Ok(result, "Lấy danh sách ô kho khả dụng thành công."));
    }

    /// <summary>
    /// Xác minh giấy tờ tùy thân của khách hàng tại quầy (government_id hoặc passport).
    /// </summary>
    [HttpPost("{reservationId:long}/verify-id")]
    [ProducesResponseType(typeof(ApiResponse<IdentityVerificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyIdentity(
        long reservationId,
        [FromBody] VerifyIdentityRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _checkInService.VerifyIdentityAsync(reservationId, request, userId, roles, cancellationToken);
        return Ok(ApiResponse<IdentityVerificationResponse>.Ok(result, "Ghi nhận kết quả xác minh giấy tờ thành công."));
    }

    /// <summary>
    /// Gán ô kho vật lý cụ thể cho hợp đồng thuê.
    /// </summary>
    [HttpPost("{reservationId:long}/assign-unit")]
    [ProducesResponseType(typeof(ApiResponse<UnitAllocationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignUnit(
        long reservationId,
        [FromBody] AssignUnitRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _checkInService.AssignUnitAsync(reservationId, request, userId, roles, cancellationToken);
        return Ok(ApiResponse<UnitAllocationResponse>.Ok(result, "Gán ô kho vật lý thành công."));
    }

    /// <summary>
    /// Giải phóng ô kho đã gán trước khi bàn giao để chọn ô khác.
    /// </summary>
    [HttpPost("{reservationId:long}/release-unit")]
    [ProducesResponseType(typeof(ApiResponse<UnitAllocationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReleaseUnit(
        long reservationId,
        [FromBody] ReleaseUnitRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _checkInService.ReleaseUnitAsync(reservationId, request, userId, roles, cancellationToken);
        return Ok(ApiResponse<UnitAllocationResponse>.Ok(result, "Giải phóng ô kho thành công."));
    }

    /// <summary>
    /// Thực hiện kiểm tra tình trạng ô kho (Checklist: Cửa, Khóa, Đèn, Vệ sinh).
    /// </summary>
    [HttpPost("{reservationId:long}/inspection")]
    [ProducesResponseType(typeof(ApiResponse<InspectionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PerformInspection(
        long reservationId,
        [FromBody] UnitInspectionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _checkInService.PerformInspectionAsync(reservationId, request, userId, roles, cancellationToken);
        return Ok(ApiResponse<InspectionResponse>.Ok(result, "Ghi nhận kết quả kiểm tra ô kho thành công."));
    }

    /// <summary>
    /// Hoàn tất check-in, kích hoạt hợp đồng, cấp mã PIN số hóa và gửi thông báo in-app.
    /// </summary>
    [HttpPost("{reservationId:long}/complete")]
    [ProducesResponseType(typeof(ApiResponse<HandoverCompleteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CompleteCheckIn(
        long reservationId,
        [FromBody] CompleteCheckInRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _checkInService.CompleteCheckInAsync(reservationId, request, userId, roles, cancellationToken);
        return Ok(ApiResponse<HandoverCompleteResponse>.Ok(result, "Hoàn tất bàn giao kho thành công."));
    }

    /// <summary>
    /// Cấp lại mã PIN mới tại quầy khi khách hàng quên mã hoặc cần đổi mã.
    /// </summary>
    [HttpPost("{reservationId:long}/reissue-pin")]
    [ProducesResponseType(typeof(ApiResponse<PinReissueResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReissuePin(
        long reservationId,
        [FromBody] ReissuePinRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _checkInService.ReissuePinAsync(reservationId, request, userId, roles, cancellationToken);
        return Ok(ApiResponse<PinReissueResponse>.Ok(result, "Cấp lại mã PIN thành công."));
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
