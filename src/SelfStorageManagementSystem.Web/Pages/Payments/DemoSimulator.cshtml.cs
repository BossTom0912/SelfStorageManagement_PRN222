using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.Web.Pages.Payments;

/// <summary>
/// Demo Payment Gateway Simulator thao tác trực tiếp trên Web:
/// - Xác thực an toàn bằng phiên Cookie người dùng và Antiforgery token.
/// - Không còn phụ thuộc hoặc hướng dẫn quay về ứng dụng WPF.
/// - Chặn hoàn toàn ở môi trường Production (HTTP 403 Forbidden).
/// - Yêu cầu Payment:AllowDemoSimulator được bật rõ ràng trong Development / Test.
/// </summary>
[AllowAnonymous]
public class DemoSimulatorModel : PageModel
{
    private readonly IPaymentService _paymentService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IWebRealtimePublisher? _realtimePublisher;
    private readonly ILogger<DemoSimulatorModel> _logger;

    public DemoSimulatorModel(
        IPaymentService paymentService,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<DemoSimulatorModel> logger,
        IWebRealtimePublisher? realtimePublisher = null)
    {
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _realtimePublisher = realtimePublisher;
    }

    [BindProperty(SupportsGet = true)]
    public long PaymentId { get; set; }

    [BindProperty]
    public bool IsSuccess { get; set; } = true;

    [BindProperty]
    public string? FailureReason { get; set; }

    [BindProperty]
    public string? ProviderTransactionId { get; set; }

    public PaymentDetailResponse? Payment { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long? paymentId, CancellationToken cancellationToken)
    {
        if (!IsDemoSimulatorAllowed())
        {
            _logger.LogWarning("Truy cập Demo Simulator bị từ chối do chạy trong môi trường Production hoặc Payment:AllowDemoSimulator = false.");
            return StatusCode(StatusCodes.Status403Forbidden, "Demo simulator is disabled in production.");
        }

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

        await LoadPaymentAsync(userId, roles, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCompleteAsync(CancellationToken cancellationToken)
    {
        if (!IsDemoSimulatorAllowed())
        {
            _logger.LogWarning("Yêu cầu hoàn tất Demo Simulator bị từ chối do chạy trong môi trường Production hoặc Payment:AllowDemoSimulator = false.");
            return StatusCode(StatusCodes.Status403Forbidden, "Demo simulator is disabled in production.");
        }

        var (userId, roles) = GetCurrentUserInfo();
        if (userId <= 0)
        {
            return Challenge();
        }

        if (PaymentId <= 0)
        {
            ErrorMessage = "Mã giao dịch thanh toán không hợp lệ.";
            return Page();
        }

        var request = new DemoPaymentCompleteRequest
        {
            IsSuccess = IsSuccess,
            FailureReason = IsSuccess ? null : (string.IsNullOrWhiteSpace(FailureReason) ? "Khách hàng hủy thanh toán tại cổng demo." : FailureReason.Trim()),
            ProviderTransactionId = string.IsNullOrWhiteSpace(ProviderTransactionId) ? null : ProviderTransactionId.Trim()
        };

        try
        {
            var result = await _paymentService.ProcessDemoCompleteAsync(userId, roles, PaymentId, request, cancellationToken);
            _logger.LogInformation("Thao tác giả lập Demo Payment {PaymentId} thành công với IsSuccess: {IsSuccess}, Status: {Status}",
                PaymentId, IsSuccess, result.Status);

            if (_realtimePublisher != null)
            {
                try
                {
                    var facilityId = result.FacilityId ?? 0;
                    var customerUserId = result.CustomerId > 0 ? result.CustomerId : userId;
                    await _realtimePublisher.PublishPaymentChangedAsync(result.PaymentId, result.ReservationId ?? 0, facilityId, customerUserId, result.Status, cancellationToken);
                    if (result.Status == "completed")
                    {
                        if (result.ReservationId.HasValue && result.ReservationId.Value > 0)
                        {
                            await _realtimePublisher.PublishReservationChangedAsync(result.ReservationId.Value, facilityId, customerUserId, "confirmed", cancellationToken);
                        }
                        await _realtimePublisher.PublishCatalogInvalidatedAsync(facilityId, null, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Realtime publish degraded in DemoSimulator for payment {PaymentId}", result.PaymentId);
                }
            }

            return RedirectToPage("Result", new { paymentId = PaymentId });
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning("Demo complete ConflictException: {Message}", ex.Message);
            ErrorMessage = ex.Message;
            await LoadPaymentAsync(userId, roles, cancellationToken);
            return Page();
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning("Demo complete BadRequestException: {Message}", ex.Message);
            ErrorMessage = ex.Message;
            await LoadPaymentAsync(userId, roles, cancellationToken);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning("Demo complete ForbiddenException: {Message}", ex.Message);
            return Forbid();
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Demo complete NotFoundException: {Message}", ex.Message);
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    private async Task LoadPaymentAsync(long userId, IReadOnlyList<string> roles, CancellationToken cancellationToken)
    {
        try
        {
            Payment = await _paymentService.GetPaymentByIdAsync(userId, roles, PaymentId, cancellationToken);
            if (!string.Equals(Payment.Provider, "demo", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = $"Giao dịch này sử dụng nhà cung cấp '{Payment.Provider}', không hỗ trợ Demo Simulator.";
            }
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (ForbiddenException)
        {
            ErrorMessage = "Bạn không có quyền truy cập vào giao dịch thanh toán này.";
        }
    }

    private bool IsDemoSimulatorAllowed()
    {
        var env = _environment.EnvironmentName ??
                  _configuration["ASPNETCORE_ENVIRONMENT"] ??
                  Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                  "Development";

        var allowDemoStr = _configuration["Payment:AllowDemoSimulator"];
        var allowDemo = bool.TryParse(allowDemoStr, out var d) && d;

        var isDevOrTest = env.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
                          env.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
                          env.Equals("Testing", StringComparison.OrdinalIgnoreCase);

        var isProduction = env.Equals("Production", StringComparison.OrdinalIgnoreCase);

        return isDevOrTest && allowDemo && !isProduction;
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
