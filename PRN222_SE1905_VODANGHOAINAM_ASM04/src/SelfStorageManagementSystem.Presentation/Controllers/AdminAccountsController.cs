using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Presentation.Controllers;

[Route("api/admin")]
[Authorize(Roles = RoleConstants.SystemAdministrator)]
public class AdminAccountsController : BaseController
{
    private readonly IAdminAccountService _adminAccountService;

    public AdminAccountsController(IAdminAccountService adminAccountService)
    {
        _adminAccountService = adminAccountService;
    }

    [HttpGet("accounts")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserAccountDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAccounts(
        [FromQuery] GetAccountsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _adminAccountService.GetAccountsAsync(request, cancellationToken);
        return Ok(ApiResponse<PagedResult<UserAccountDto>>.Ok(result, "Accounts retrieved successfully."));
    }

    [HttpGet("accounts/{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<UserAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccountById(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _adminAccountService.GetAccountByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<UserAccountDto>.Ok(result, "Account details retrieved successfully."));
    }

    [HttpPost("accounts/staff")]
    [ProducesResponseType(typeof(ApiResponse<UserAccountDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateStaffAccount(
        [FromBody] CreateStaffAccountRequest request,
        CancellationToken cancellationToken)
    {
        var actorUserId = GetCurrentUserId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var requestId = HttpContext.TraceIdentifier;

        var result = await _adminAccountService.CreateStaffAccountAsync(
            request,
            actorUserId,
            ipAddress,
            requestId,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<UserAccountDto>.Ok(result, "Staff account created successfully."));
    }

    [HttpPatch("accounts/{id:long}/status")]
    [ProducesResponseType(typeof(ApiResponse<UserAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserStatus(
        long id,
        [FromBody] UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        var actorUserId = GetCurrentUserId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var requestId = HttpContext.TraceIdentifier;

        var result = await _adminAccountService.UpdateUserStatusAsync(
            id,
            request,
            actorUserId,
            ipAddress,
            requestId,
            cancellationToken);

        return Ok(ApiResponse<UserAccountDto>.Ok(result, "User status updated successfully."));
    }

    [HttpPost("accounts/{id:long}/roles")]
    [ProducesResponseType(typeof(ApiResponse<UserAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ManageUserRoles(
        long id,
        [FromBody] ManageUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        var actorUserId = GetCurrentUserId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var requestId = HttpContext.TraceIdentifier;

        var result = await _adminAccountService.ManageUserRolesAsync(
            id,
            request,
            actorUserId,
            ipAddress,
            requestId,
            cancellationToken);

        return Ok(ApiResponse<UserAccountDto>.Ok(result, "User roles updated successfully."));
    }

    [HttpPost("accounts/{id:long}/facility-assignments")]
    [ProducesResponseType(typeof(ApiResponse<FacilityAssignmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignFacility(
        long id,
        [FromBody] CreateFacilityAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var actorUserId = GetCurrentUserId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var requestId = HttpContext.TraceIdentifier;

        var result = await _adminAccountService.AssignFacilityAsync(
            id,
            request,
            actorUserId,
            ipAddress,
            requestId,
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<FacilityAssignmentDto>.Ok(result, "Facility assigned successfully."));
    }

    [HttpPost("accounts/facility-assignments/{assignmentId:long}/terminate")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TerminateFacilityAssignment(
        long assignmentId,
        CancellationToken cancellationToken)
    {
        var actorUserId = GetCurrentUserId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var requestId = HttpContext.TraceIdentifier;

        await _adminAccountService.TerminateFacilityAssignmentAsync(
            assignmentId,
            actorUserId,
            ipAddress,
            requestId,
            cancellationToken);

        return Ok(ApiResponse<object?>.Ok(null, "Facility assignment terminated successfully."));
    }

    [HttpGet("facilities")]
    [ProducesResponseType(typeof(ApiResponse<List<FacilityLookupDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFacilities(CancellationToken cancellationToken)
    {
        var result = await _adminAccountService.GetFacilitiesAsync(cancellationToken);
        return Ok(ApiResponse<List<FacilityLookupDto>>.Ok(result, "Facilities retrieved successfully."));
    }

    private long GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("User identity could not be verified from token.");
        }
        return userId;
    }
}
