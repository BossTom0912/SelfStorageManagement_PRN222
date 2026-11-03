using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.Web.Controllers;

/// <summary>
/// Callback adapters and API endpoints for payment gateways on Web host (Port 7041):
/// - /api/payments/vnpay/ipn: Server-to-server webhook callback from VNPAY. Source of truth for financial confirmation.
/// - /api/payments/vnpay/return: Browser return URL from VNPAY. Informative display only, does not alter financial state.
/// - /api/payments/demo/{id}/simulator: Adapter redirecting to web-based Demo Simulator. Blocked in Production.
/// - /api/payments/demo/{id}/complete: Endpoint to complete demo payments with Cookie / identity authentication. Blocked in Production.
/// </summary>
[Route("api/payments")]
[ApiController]
public class PaymentsApiController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IWebRealtimePublisher? _realtimePublisher;
    private readonly ILogger<PaymentsApiController> _logger;

    public PaymentsApiController(
        IPaymentService paymentService,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<PaymentsApiController> logger,
        IWebRealtimePublisher? realtimePublisher = null)
    {
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _realtimePublisher = realtimePublisher;
    }

    /// <summary>
    /// VNPAY IPN webhook server-to-server.
    /// Nguồn sự thật cập nhật trạng thái thanh toán từ nhà cung cấp VNPAY.
    /// Yêu cầu chữ ký số HMAC-SHA512 hợp lệ mới xác nhận tài chính.
    /// </summary>
    [HttpGet("vnpay/ipn")]
    [AllowAnonymous]
    public async Task<IActionResult> VnpayIpn(CancellationToken cancellationToken)
    {
        var queryParams = Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString());
        var result = await _paymentService.ProcessVnpayIpnAsync(queryParams, cancellationToken);

        var rspCode = result.IpnResponseCode ?? (result.IsValidSignature ? "00" : "97");
        var rspMsg = result.IpnResponseMessage ?? (result.IsValidSignature ? "Confirm Success" : "Invalid Signature");

        _logger.LogInformation("VNPAY IPN processed. TxnRef: {TxnRef}, SignatureValid: {IsValid}, RspCode: {RspCode}",
            result.TxnRef, result.IsValidSignature, rspCode);

        if (_realtimePublisher != null && result.IsStatusTransitionCommitted)
        {
            try
            {
                var paymentStatus = result.CommittedStatus ?? "completed";
                await _realtimePublisher.PublishPaymentChangedAsync(
                    result.PaymentId,
                    result.ReservationId,
                    result.FacilityId,
                    result.CustomerUserId,
                    paymentStatus,
                    cancellationToken);

                if (string.Equals(paymentStatus, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    await _realtimePublisher.PublishReservationChangedAsync(
                        result.ReservationId,
                        result.FacilityId,
                        result.CustomerUserId,
                        "confirmed",
                        cancellationToken);

                    if (result.FacilityId > 0)
                    {
                        await _realtimePublisher.PublishCatalogInvalidatedAsync(result.FacilityId, null, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Realtime publish degraded in VnpayIpn for payment {TxnRef}", result.TxnRef);
            }
        }

        return Ok(new { RspCode = rspCode, Message = rspMsg });
    }

    /// <summary>
    /// VNPAY ReturnUrl khi khách được chuyển hướng về từ trình duyệt sau thao tác tại cổng.
    /// Chỉ hiển thị thông báo kết quả, KHÔNG xác nhận tài chính (IPN là nguồn sự thật duy nhất).
    /// Tuyệt đối an toàn trước XSS (T12-F01), không chèn query string thô vào DOM.
    /// </summary>
    [HttpGet("vnpay/return")]
    [AllowAnonymous]
    public IActionResult VnpayReturn()
    {
        var rawTxnRef = Request.Query["vnp_TxnRef"].ToString();
        var rawResponseCode = Request.Query["vnp_ResponseCode"].ToString();
        var isSuccessCode = string.Equals(rawResponseCode, "00", StringComparison.Ordinal);

        var isValidPaymentId = long.TryParse(rawTxnRef, out var paymentId) && paymentId > 0;

        // Mã hóa an toàn nội dung hiển thị
        var safeTxnRefDisplay = isValidPaymentId
            ? paymentId.ToString()
            : (string.IsNullOrWhiteSpace(rawTxnRef)
                ? "-"
                : HtmlEncoder.Default.Encode(rawTxnRef.Length > 64 ? rawTxnRef[..64] + "..." : rawTxnRef));

        var detailActionHtml = isValidPaymentId
            ? $"""
               <a href="/Payments/Details?id={paymentId}" class="btn btn-primary fw-semibold py-2">
                   Xem chi tiết giao dịch thanh toán
               </a>
               """
            : """
              <div class="alert alert-warning border text-start small mb-3">
                  <strong>⚠️ Cảnh báo:</strong> Mã tham chiếu giao dịch không đúng định dạng. Không thể mở trang chi tiết trực tiếp.
              </div>
              <a href="/Payments" class="btn btn-secondary fw-semibold py-2">
                  Quay lại danh sách thanh toán
              </a>
              """;

        var html = $$"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>Kết quả chuyển hướng VNPAY - SelfStorage PRN222</title>
                <link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css">
                <style>
                    body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; background: #f8fafc; padding: 40px 15px; color: #1e293b; }
                    .card-return { max-width: 540px; margin: 0 auto; background: white; border-radius: 16px; box-shadow: 0 10px 25px -5px rgba(0,0,0,0.08); padding: 36px; border: 1px solid #e2e8f0; }
                    .icon { font-size: 52px; margin-bottom: 16px; }
                </style>
            </head>
            <body>
                <div class="card-return text-center">
                    <div class="icon">{{(isSuccessCode ? "💳" : "⚠️")}}</div>
                    <h3 class="fw-bold mb-2">{{(isSuccessCode ? "Đã Hoàn Tất Thao Tác Tại VNPAY" : "Thao Tác Tại Cổng VNPAY Chưa Hoàn Tất")}}</h3>
                    <p class="text-muted small mb-4">
                        Mã giao dịch tham chiếu: <strong class="font-monospace text-primary">{{safeTxnRefDisplay}}</strong>
                    </p>

                    <div class="alert alert-info border text-start small mb-4">
                        <strong>📌 Lưu ý kỹ thuật (Architecture Contract):</strong><br>
                        Trang chuyển hướng trình duyệt này chỉ phục vụ hiển thị kết quả cho người dùng. 
                        Theo tiêu chuẩn an toàn thanh toán của hệ thống, kết quả tài chính và quyền khởi tạo hợp đồng 
                        được xác nhận độc lập tại máy chủ thông qua kênh bảo mật <strong>IPN (Instant Payment Notification)</strong>.
                    </div>

                    <div class="d-grid gap-2">
                        {{detailActionHtml}}
                        <a href="/Reservations" class="btn btn-outline-secondary btn-sm">
                            Quay lại danh sách đặt chỗ của tôi
                        </a>
                    </div>
                </div>
            </body>
            </html>
            """;

        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// Endpoint chuyển tiếp tới trang Web Demo Simulator.
    /// Bị chặn hoàn toàn ở môi trường Production (403 Forbidden).
    /// </summary>
    [HttpGet("demo/{id:long}/simulator")]
    [AllowAnonymous]
    public IActionResult DemoSimulator(
        long id,
        [FromQuery] decimal amount,
        [FromQuery] string? holdUntil)
    {
        if (!IsDemoSimulatorAllowed())
        {
            _logger.LogWarning("Truy cập Demo Simulator bị từ chối do chạy trong môi trường Production hoặc AllowDemoSimulator = false.");
            return StatusCode(StatusCodes.Status403Forbidden, "Demo simulator is disabled in production.");
        }

        var redirectUrl = $"/Payments/DemoSimulator?paymentId={id}&amount={amount}&holdUntil={Uri.EscapeDataString(holdUntil ?? string.Empty)}";
        return Redirect(redirectUrl);
    }

    /// <summary>
    /// Giả lập hoàn tất thanh toán Demo (Chỉ kích hoạt trong Development / Test).
    /// Hỗ trợ cả Cookie auth hoặc các fixtures kiểm thử.
    /// </summary>
    [HttpPost("demo/{id:long}/complete")]
    [Authorize]
    public async Task<IActionResult> CompleteDemoPayment(
        long id,
        [FromBody] DemoPaymentCompleteRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsDemoSimulatorAllowed())
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Demo simulator is disabled in production.");
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized("Phiên đăng nhập không hợp lệ.");
        }

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var result = await _paymentService.ProcessDemoCompleteAsync(userId, roles, id, request, cancellationToken);

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
                _logger.LogWarning(ex, "Realtime publish degraded after demo complete for payment {PaymentId}", result.PaymentId);
            }
        }

        return Ok(new
        {
            success = true,
            message = "Cập nhật kết quả thanh toán demo thành công.",
            data = result
        });
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
}
