using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Invoices;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.Invoices;

[Authorize]
public class DetailsModel : PageModel
{
    private readonly IInvoiceService _invoiceService;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(
        IInvoiceService invoiceService,
        ILogger<DetailsModel> logger)
    {
        _invoiceService = invoiceService ?? throw new ArgumentNullException(nameof(invoiceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    public InvoiceDetailResponse? Invoice { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long? id, CancellationToken cancellationToken)
    {
        if (id.HasValue && id.Value > 0)
        {
            Id = id.Value;
        }

        if (Id <= 0 && Request.Query.TryGetValue("id", out var qVal) && long.TryParse(qVal, out var qId) && qId > 0)
        {
            Id = qId;
        }

        if (Id <= 0)
        {
            ErrorMessage = "Mã hóa đơn không hợp lệ.";
            return Page();
        }

        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Challenge();
        }

        try
        {
            Invoice = await _invoiceService.GetInvoiceByIdAsync(userId, roles, Id, cancellationToken);
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning("Invoice Details Forbidden: {Message}", ex.Message);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy thông tin hóa đơn {InvoiceId}", Id);
            ErrorMessage = "Không thể tải thông tin hóa đơn.";
        }

        return Page();
    }

    private (long UserId, IReadOnlyList<string> Roles) GetCurrentUserInfo()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdClaim, out var userId))
        {
            return (0, Array.Empty<string>());
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return (userId, roles);
    }
}
