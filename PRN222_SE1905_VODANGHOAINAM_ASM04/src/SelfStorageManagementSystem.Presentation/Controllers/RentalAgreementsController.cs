using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Agreements;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Presentation.Controllers;

[Route("api/agreements")]
[Authorize]
public class RentalAgreementsController : BaseController
{
    private readonly IRentalAgreementService _rentalAgreementService;

    public RentalAgreementsController(IRentalAgreementService rentalAgreementService)
    {
        _rentalAgreementService = rentalAgreementService ?? throw new ArgumentNullException(nameof(rentalAgreementService));
    }

    /// <summary>
    /// Xem chi tiết hợp đồng thuê theo ID (Trạng thái scheduled chờ bàn giao kho).
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<RentalAgreementDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAgreementById(
        long id,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _rentalAgreementService.GetAgreementByIdAsync(userId, roles, id, cancellationToken);
        return Ok(ApiResponse<RentalAgreementDetailResponse>.Ok(result, "Lấy thông tin hợp đồng thuê thành công."));
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
