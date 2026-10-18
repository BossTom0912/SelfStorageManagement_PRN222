using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.Web.Pages.Payments;

[Authorize(Policy = "CustomerOnly")]
public class CheckoutModel : PageModel
{
    private readonly IPaymentService _paymentService;
    private readonly IWebRealtimePublisher? _realtimePublisher;
    private readonly ILogger<CheckoutModel> _logger;

    public CheckoutModel(
        IPaymentService paymentService,
        ILogger<CheckoutModel> logger,
        IWebRealtimePublisher? realtimePublisher = null)
    {
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _realtimePublisher = realtimePublisher;
    }

    [BindProperty(SupportsGet = true)]
    public long ReservationId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PromotionCode { get; set; }

    [BindProperty]
    public long AcceptedPolicyVersionId { get; set; }

    [BindProperty]
    public bool AcceptPolicy { get; set; }

    [BindProperty]
    public string PaymentMethod { get; set; } = "demo";

    [BindProperty]
    public string IdempotencyKey { get; set; } = string.Empty;

    public CheckoutQuoteResponse? Quote { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public bool IsExpired { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (ReservationId <= 0)
        {
            ErrorMessage = "Mã đơn đặt chỗ không hợp lệ.";
            return Page();
        }

        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Challenge();
        }

        // Tạo hoặc duy trì Idempotency-Key ổn định cho cùng lần thử thanh toán
        if (string.IsNullOrWhiteSpace(IdempotencyKey))
        {
            IdempotencyKey = $"IDEM-CUST{userId}-RES{ReservationId}-{Guid.NewGuid():N}";
        }

        await LoadQuoteAsync(userId, roles, PromotionCode, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostApplyPromoAsync(CancellationToken cancellationToken)
    {
        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Challenge();
        }

        if (ReservationId <= 0)
        {
            ErrorMessage = "Mã đơn đặt chỗ không hợp lệ.";
            return Page();
        }

        // Duy trì IdempotencyKey hiện tại
        if (string.IsNullOrWhiteSpace(IdempotencyKey))
        {
            IdempotencyKey = $"IDEM-CUST{userId}-RES{ReservationId}-{Guid.NewGuid():N}";
        }

        try
        {
            Quote = await _paymentService.GetCheckoutQuoteAsync(userId, roles, ReservationId, PromotionCode?.Trim(), cancellationToken);
            AcceptedPolicyVersionId = Quote.PolicyVersionId;

            if (Quote.DiscountAmount > 0)
            {
                SuccessMessage = $"Đã áp dụng mã ưu đãi '{Quote.PromotionCode}' ({Quote.PromotionName}): giảm {Quote.DiscountAmount:N0} đ.";
            }
            else if (!string.IsNullOrWhiteSpace(PromotionCode))
            {
                ErrorMessage = "Mã giảm giá không áp dụng được cho đơn này.";
            }
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            await LoadQuoteAsync(userId, roles, null, cancellationToken);
        }
        catch (ConflictException ex)
        {
            ErrorMessage = ex.Message;
            IsExpired = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi áp dụng mã giảm giá cho reservation {ReservationId}", ReservationId);
            ErrorMessage = "Không thể áp dụng mã giảm giá. Vui lòng kiểm tra lại.";
            await LoadQuoteAsync(userId, roles, null, cancellationToken);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Challenge();
        }

        if (!AcceptPolicy)
        {
            ModelState.AddModelError(nameof(AcceptPolicy), "Bạn phải đọc và đồng ý với điều khoản hợp đồng thuê kho trước khi thanh toán.");
        }

        if (string.IsNullOrWhiteSpace(IdempotencyKey))
        {
            IdempotencyKey = $"IDEM-CUST{userId}-RES{ReservationId}-{Guid.NewGuid():N}";
        }

        if (!ModelState.IsValid)
        {
            await LoadQuoteAsync(userId, roles, PromotionCode, cancellationToken);
            return Page();
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var checkoutRequest = new CheckoutRequest
        {
            ReservationId = ReservationId,
            PromotionCode = PromotionCode?.Trim(),
            AcceptedPolicyVersionId = AcceptedPolicyVersionId,
            PaymentMethod = PaymentMethod
        };

        try
        {
            var checkoutResponse = await _paymentService.CheckoutAsync(
                userId,
                roles,
                checkoutRequest,
                IdempotencyKey,
                clientIp,
                cancellationToken);

            _logger.LogInformation("Khởi tạo checkout thành công cho đơn {ReservationId}, PaymentId: {PaymentId}, Provider: {Provider}",
                ReservationId, checkoutResponse.PaymentId, checkoutResponse.Provider);

            if (_realtimePublisher != null)
            {
                try
                {
                    await _realtimePublisher.PublishPaymentChangedAsync(checkoutResponse.PaymentId, ReservationId, 0, userId, "pending", cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Realtime publish degraded after checkout for reservation {ReservationId}", ReservationId);
                }
            }

            if (string.Equals(checkoutResponse.Provider, "demo", StringComparison.OrdinalIgnoreCase))
            {
                // Chuyển hướng trực tiếp tới Demo Simulator trên web
                return RedirectToPage("DemoSimulator", new { paymentId = checkoutResponse.PaymentId });
            }

            // Chuyển hướng tới cổng thanh toán thật (ví dụ: VNPAY)
            return Redirect(checkoutResponse.CheckoutUrl);
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning("Checkout ConflictException: {Message}", ex.Message);
            ErrorMessage = ex.Message;
            IsExpired = ex.Message.Contains("hết hạn", StringComparison.OrdinalIgnoreCase);
            await LoadQuoteAsync(userId, roles, PromotionCode, cancellationToken);
            return Page();
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning("Checkout BadRequestException: {Message}", ex.Message);
            ErrorMessage = ex.Message;
            await LoadQuoteAsync(userId, roles, PromotionCode, cancellationToken);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning("Checkout ForbiddenException: {Message}", ex.Message);
            return Forbid();
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Checkout NotFoundException: {Message}", ex.Message);
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    private async Task LoadQuoteAsync(long userId, IReadOnlyList<string> roles, string? promoCode, CancellationToken cancellationToken)
    {
        try
        {
            Quote = await _paymentService.GetCheckoutQuoteAsync(userId, roles, ReservationId, promoCode?.Trim(), cancellationToken);
            AcceptedPolicyVersionId = Quote.PolicyVersionId;
            IsExpired = !Quote.IsHoldActive;
        }
        catch (ConflictException ex)
        {
            ErrorMessage = ex.Message;
            IsExpired = true;
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (ForbiddenException)
        {
            ErrorMessage = "Bạn không có quyền truy cập vào đơn đặt chỗ này.";
        }
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
