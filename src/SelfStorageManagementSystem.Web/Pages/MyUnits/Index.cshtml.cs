using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.MyUnits;

[Authorize(Policy = "CustomerOnly")]
public class IndexModel : PageModel
{
    private readonly ICheckInService _checkInService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ICheckInService checkInService,
        ILogger<IndexModel> logger)
    {
        _checkInService = checkInService ?? throw new ArgumentNullException(nameof(checkInService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PagedResult<CustomerUnitListItemResponse>? Units { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId <= 0)
        {
            return Challenge();
        }

        PageNumber = Math.Max(1, PageNumber);
        PageSize = PageSize < 1 ? 10 : Math.Min(100, PageSize);

        try
        {
            Units = await _checkInService.GetCustomerUnitsAsync(
                currentUserId,
                new PagedRequest { PageNumber = PageNumber, PageSize = PageSize },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi tải danh sách ô kho của khách hàng {CustomerId}", currentUserId);
            ErrorMessage = "Đã xảy ra lỗi khi tải danh sách ô kho của bạn. Vui lòng thử lại sau.";
            Units = new PagedResult<CustomerUnitListItemResponse>(new List<CustomerUnitListItemResponse>(), 0, PageNumber, PageSize);
        }

        return Page();
    }

    private long GetCurrentUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idClaim, out var id) ? id : 0;
    }
}
