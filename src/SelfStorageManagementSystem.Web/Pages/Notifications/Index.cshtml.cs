using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.Notifications;

[Authorize]
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

    public PagedResult<CustomerNotificationResponse>? Notifications { get; set; }

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
            Notifications = await _checkInService.GetCustomerNotificationsAsync(
                currentUserId,
                new PagedRequest { PageNumber = PageNumber, PageSize = PageSize },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi tải thông báo cho người dùng {UserId}", currentUserId);
            ErrorMessage = "Đã xảy ra lỗi khi tải danh sách thông báo. Vui lòng thử lại sau.";
            Notifications = new PagedResult<CustomerNotificationResponse>(new List<CustomerNotificationResponse>(), 0, PageNumber, PageSize);
        }

        return Page();
    }

    private long GetCurrentUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idClaim, out var id) ? id : 0;
    }
}
