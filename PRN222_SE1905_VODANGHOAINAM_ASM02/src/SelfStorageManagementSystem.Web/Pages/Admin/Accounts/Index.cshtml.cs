using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.Admin.Accounts;

[Authorize(Policy = "AdminOnly")]
public class IndexModel : PageModel
{
    private readonly IAdminAccountService _adminAccountService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IAdminAccountService adminAccountService,
        ILogger<IndexModel> logger)
    {
        _adminAccountService = adminAccountService ?? throw new ArgumentNullException(nameof(adminAccountService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? RoleCode { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;

    public PagedResult<UserAccountDto>? Accounts { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken = default)
    {
        PageNumber = Math.Max(1, PageNumber);
        PageSize = PageSize < 1 ? 10 : Math.Min(100, PageSize);

        var request = new GetAccountsRequest
        {
            SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm.Trim(),
            Status = string.IsNullOrWhiteSpace(Status) ? null : Status.Trim(),
            RoleCode = string.IsNullOrWhiteSpace(RoleCode) ? null : RoleCode.Trim(),
            PageNumber = PageNumber,
            PageSize = PageSize
        };

        try
        {
            Accounts = await _adminAccountService.GetAccountsAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi tải danh sách tài khoản.");
            ErrorMessage = "Đã xảy ra lỗi khi tải danh sách tài khoản. Vui lòng thử lại sau.";
            Accounts = new PagedResult<UserAccountDto>(new List<UserAccountDto>(), 0, PageNumber, PageSize);
        }

        return Page();
    }
}
