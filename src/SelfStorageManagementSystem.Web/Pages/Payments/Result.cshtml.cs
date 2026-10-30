using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.Payments;

[Authorize]
public class ResultModel : PageModel
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<ResultModel> _logger;

    public ResultModel(
        IPaymentService paymentService,
        ILogger<ResultModel> logger)
    {
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public long PaymentId { get; set; }

    public PaymentDetailResponse? Payment { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long? paymentId, CancellationToken cancellationToken)
    {
        if (paymentId.HasValue && paymentId.Value > 0)
        {
            PaymentId = paymentId.Value;
        }

        if (PaymentId <= 0)
        {
            ErrorMessage = "Mã giao dịch thanh toán không hợp lệ.";
            return Page();
        }

        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Challenge();
        }

        try
        {
            Payment = await _paymentService.GetPaymentByIdAsync(userId, roles, PaymentId, cancellationToken);
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning("Result ForbiddenException: {Message}", ex.Message);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải kết quả thanh toán {PaymentId}", PaymentId);
            ErrorMessage = "Không thể tải kết quả giao dịch thanh toán.";
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
