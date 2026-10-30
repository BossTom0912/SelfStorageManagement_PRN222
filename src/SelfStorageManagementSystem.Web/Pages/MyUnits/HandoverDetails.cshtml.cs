using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.MyUnits;

[Authorize(Policy = "CustomerOnly")]
public class HandoverDetailsModel : PageModel
{
    private readonly ICheckInService _checkInService;
    private readonly ILogger<HandoverDetailsModel> _logger;

    public HandoverDetailsModel(
        ICheckInService checkInService,
        ILogger<HandoverDetailsModel> logger)
    {
        _checkInService = checkInService ?? throw new ArgumentNullException(nameof(checkInService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public long AgreementId { get; set; }

    public CustomerHandoverResponse? Handover { get; set; }

    public bool IsNotFoundOrForbidden { get; set; }

    public string? ErrorMessage { get; set; }

    public const string SafeNotFoundErrorMessage = "Không tìm thấy thông tin bàn giao cho hợp đồng được yêu cầu hoặc hợp đồng không thuộc quyền sở hữu của bạn.";

    public async Task<IActionResult> OnGetAsync(long agreementId, CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId <= 0)
        {
            return Challenge();
        }

        if (agreementId <= 0)
        {
            IsNotFoundOrForbidden = true;
            ErrorMessage = SafeNotFoundErrorMessage;
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        AgreementId = agreementId;

        try
        {
            Handover = await _checkInService.GetCustomerHandoverAsync(currentUserId, agreementId, cancellationToken);
            return Page();
        }
        catch (NotFoundException)
        {
            // Bảo mật sở hữu dữ liệu: Không tiết lộ hợp đồng của khách hàng khác có tồn tại hay không
            IsNotFoundOrForbidden = true;
            ErrorMessage = SafeNotFoundErrorMessage;
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }
        catch (ForbiddenException)
        {
            // Bảo mật sở hữu dữ liệu: Không tiết lộ hợp đồng của khách hàng khác có tồn tại hay không
            IsNotFoundOrForbidden = true;
            ErrorMessage = SafeNotFoundErrorMessage;
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải thông tin bàn giao cho hợp đồng {AgreementId} của khách hàng {CustomerId}", agreementId, currentUserId);
            IsNotFoundOrForbidden = true;
            ErrorMessage = "Đã xảy ra lỗi khi tải biên bản bàn giao. Vui lòng thử lại sau.";
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Page();
        }
    }

    private long GetCurrentUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idClaim, out var id) ? id : 0;
    }
}
