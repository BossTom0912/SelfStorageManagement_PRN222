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

[Authorize]
public class IndexModel : PageModel
{
    private readonly IPaymentService _paymentService;
    private readonly IWebRealtimePublisher? _realtimePublisher;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IPaymentService paymentService,
        ILogger<IndexModel> logger,
        IWebRealtimePublisher? realtimePublisher = null)
    {
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _realtimePublisher = realtimePublisher;
    }

    [BindProperty(SupportsGet = true)]
    public long? ReservationId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; } = "reconciliation";

    [BindProperty(SupportsGet = true)]
    public string? RefundStatus { get; set; }

    public bool IsEmployee { get; set; }
    public List<PaymentDetailResponse> ReconciliationPayments { get; set; } = new();
    public List<RefundDetailResponse> Refunds { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        // 1. Nếu có reservationId trong query string, chuyển tiếp ngay tới Checkout
        if (ReservationId.HasValue && ReservationId.Value > 0)
        {
            return RedirectToPage("Checkout", new { reservationId = ReservationId.Value });
        }

        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Challenge();
        }

        IsEmployee = roles.Any(RoleConstants.IsEmployeeRole) || roles.Contains(RoleConstants.SystemAdministrator);

        if (!IsEmployee)
        {
            // Với khách hàng, trang này đóng vai trò trung tâm tài chính hướng dẫn và tra cứu
            return Page();
        }

        // Với nhân sự vận hành/quản lý: Nạp dữ liệu đối soát và hoàn tiền
        try
        {
            ReconciliationPayments = await _paymentService.GetPaymentsRequiringReconciliationAsync(userId, roles, cancellationToken);
            Refunds = await _paymentService.GetRefundsAsync(userId, roles, RefundStatus, cancellationToken);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning("Forbidden when loading reconciliation or refunds: {Message}", ex.Message);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải danh sách đối soát thanh toán hoặc hoàn tiền.");
            ErrorMessage = "Không thể tải danh sách đối soát thanh toán và hoàn tiền.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostReviewRefundAsync(
        long refundId,
        string decision,
        string? reason,
        CancellationToken cancellationToken)
    {
        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Challenge();
        }

        var reviewRequest = new ReviewRefundRequest
        {
            Decision = decision?.Trim().ToLowerInvariant() ?? string.Empty,
            Reason = reason?.Trim()
        };

        try
        {
            var result = await _paymentService.ReviewRefundAsync(userId, roles, refundId, reviewRequest, cancellationToken);
            TempData["SuccessMessage"] = $"Đã {(decision == "approved" ? "phê duyệt" : "từ chối")} yêu cầu hoàn tiền #{refundId} thành công (Trạng thái: {result.Status}).";

            if (_realtimePublisher != null)
            {
                try
                {
                    var status = result.Status == "approved" ? "refunded" : "failed";
                    await _realtimePublisher.PublishPaymentChangedAsync(
                        result.PaymentId,
                        result.ReservationId,
                        result.FacilityId,
                        result.CustomerUserId,
                        status,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (RealtimePublishDegradedException ex)
                {
                    _logger.LogWarning(ex, "Realtime publish degraded in ReviewRefund for refund {RefundId}", refundId);
                    TempData["WarningMessage"] = $"Đã {(decision == "approved" ? "phê duyệt" : "từ chối")} yêu cầu hoàn tiền #{refundId} thành công trong cơ sở dữ liệu, nhưng đồng bộ thời gian thực gặp sự cố tạm thời.";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unexpected realtime publish error in ReviewRefund for refund {RefundId}", refundId);
                    TempData["WarningMessage"] = $"Đã {(decision == "approved" ? "phê duyệt" : "từ chối")} yêu cầu hoàn tiền #{refundId} thành công, nhưng đồng bộ thời gian thực gặp sự cố.";
                }
            }
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning("Review refund conflict: {Message}", ex.Message);
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning("Review refund bad request: {Message}", ex.Message);
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning("Review refund forbidden: {Message}", ex.Message);
            return Forbid();
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Review refund not found: {Message}", ex.Message);
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage(new { tab = "refunds", refundStatus = RefundStatus });
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
