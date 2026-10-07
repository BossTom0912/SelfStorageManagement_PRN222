using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Presentation.Controllers;

[Route("api/[controller]")]
[Authorize]
public class PaymentsController : BaseController
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
    }

    /// <summary>
    /// Khởi tạo phiên thanh toán cọc và kỳ đầu cho reservation đang được giữ 15 phút.
    /// Bắt buộc kèm theo header Idempotency-Key và phiên bản điều khoản hợp đồng đã chấp thuận.
    /// </summary>
    [HttpPost("checkout")]
    [Authorize(Roles = RoleConstants.StorageCustomer)]
    [ProducesResponseType(typeof(ApiResponse<CheckoutResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BadRequestException("Header 'Idempotency-Key' là bắt buộc khi gọi checkout.");
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _paymentService.CheckoutAsync(
            userId,
            roles,
            request,
            idempotencyKey,
            clientIp,
            cancellationToken);

        return Ok(ApiResponse<CheckoutResponse>.Ok(result, "Khởi tạo phiên thanh toán thành công."));
    }

    /// <summary>
    /// Xem chi tiết giao dịch thanh toán theo ID.
    /// </summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<PaymentDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentById(
        long id,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _paymentService.GetPaymentByIdAsync(userId, roles, id, cancellationToken);
        return Ok(ApiResponse<PaymentDetailResponse>.Ok(result, "Lấy thông tin giao dịch thành công."));
    }

    /// <summary>
    /// Tra cứu danh sách các khoản thanh toán cần đối soát (ví dụ: tiền đến sau khi hold đã hết hạn/hủy).
    /// Chỉ dành cho nhân viên cơ sở hoặc quản lý.
    /// </summary>
    [HttpGet("reconciliation")]
    [Authorize(Roles = "facility_staff,facility_manager,business_operations_manager,system_administrator")]
    [ProducesResponseType(typeof(ApiResponse<List<PaymentDetailResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPaymentsRequiringReconciliation(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _paymentService.GetPaymentsRequiringReconciliationAsync(userId, roles, cancellationToken);
        return Ok(ApiResponse<List<PaymentDetailResponse>>.Ok(result, "Danh sách thanh toán cần đối soát đã được tải thành công."));
    }

    /// <summary>
    /// VNPAY IPN webhook server-to-server.
    /// Nguồn sự thật cập nhật trạng thái thanh toán từ nhà cung cấp VNPAY.
    /// </summary>
    [HttpGet("vnpay/ipn")]
    [AllowAnonymous]
    public async Task<IActionResult> VnpayIpn(CancellationToken cancellationToken)
    {
        var queryParams = Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString());
        var result = await _paymentService.ProcessVnpayIpnAsync(queryParams, cancellationToken);

        if (!result.IsValidSignature)
        {
            return Ok(new { RspCode = "97", Message = "Invalid Signature" });
        }

        if (result.FailureReason == "Invalid payment TxnRef.")
        {
            return Ok(new { RspCode = "01", Message = "Order not found" });
        }

        return Ok(new { RspCode = "00", Message = "Confirm Success" });
    }

    /// <summary>
    /// VNPAY ReturnUrl khi khách được chuyển hướng về từ trình duyệt.
    /// Chỉ hiển thị thông báo, không xác nhận thanh toán tài chính (IPN là nguồn sự thật).
    /// </summary>
    [HttpGet("vnpay/return")]
    [AllowAnonymous]
    public IActionResult VnpayReturn()
    {
        var html = """
            <!DOCTYPE html>
            <html lang="vi">
            <head>
                <meta charset="utf-8">
                <title>Kết quả thanh toán VNPAY - Self-Storage System</title>
                <style>
                    body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; background: #f8fafc; padding: 40px 20px; text-align: center; color: #1e293b; }
                    .card { max-width: 500px; margin: 0 auto; background: white; border-radius: 12px; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1); padding: 32px; border: 1px solid #e2e8f0; }
                    .icon { font-size: 48px; margin-bottom: 16px; }
                    h2 { margin: 0 0 12px 0; color: #0f172a; }
                    p { color: #64748b; line-height: 1.6; margin-bottom: 24px; font-size: 14px; }
                    .notice { background: #eff6ff; border: 1px solid #bfdbfe; border-radius: 8px; padding: 12px; font-size: 12px; color: #1e40af; text-align: left; }
                </style>
            </head>
            <body>
                <div class="card">
                    <div class="icon">💳</div>
                    <h2>Đã hoàn tất thao tác tại VNPAY</h2>
                    <p>Giao dịch của bạn đang được hệ thống đồng bộ và xác nhận tự động qua kênh an toàn (IPN). Bạn có thể quay lại ứng dụng <strong>SelfStorageManagementSystem</strong> để theo dõi trạng thái biên nhận và hợp đồng.</p>
                    <div class="notice">
                        <strong>📌 Lưu ý:</strong> Trang này chỉ phục vụ hiển thị chuyển hướng. Kết quả tài chính và quyền nhận kho được xác nhận độc lập tại máy chủ theo chuẩn an toàn VNPAY.
                    </div>
                </div>
            </body>
            </html>
            """;

        return Content(html, "text/html");
    }

    /// <summary>
    /// Giả lập hoàn tất thanh toán Demo (Chỉ kích hoạt trong Development / Test).
    /// </summary>
    [HttpPost("demo/{id:long}/complete")]
    [ProducesResponseType(typeof(ApiResponse<PaymentDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteDemoPayment(
        long id,
        [FromBody] DemoPaymentCompleteRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var roles = GetCurrentUserRoles();

        var result = await _paymentService.ProcessDemoCompleteAsync(userId, roles, id, request, cancellationToken);
        return Ok(ApiResponse<PaymentDetailResponse>.Ok(result, "Cập nhật kết quả thanh toán demo thành công."));
    }

    /// <summary>
    /// Trang giả lập giao diện cổng thanh toán demo khi mở trong trình duyệt.
    /// </summary>
    [HttpGet("demo/{id:long}/simulator")]
    [AllowAnonymous]
    public IActionResult DemoSimulator(
        long id,
        [FromQuery] decimal amount,
        [FromQuery] string? holdUntil)
    {
        var html = $$"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
                <meta charset="utf-8">
                <title>Cổng Thanh Toán Giả Lập (Demo Gateway)</title>
                <style>
                    body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; background: #0f172a; padding: 40px 20px; text-align: center; color: #f8fafc; }
                    .card { max-width: 480px; margin: 0 auto; background: #1e293b; border-radius: 12px; padding: 32px; border: 1px solid #334155; box-shadow: 0 10px 25px -5px rgba(0,0,0,0.5); }
                    .amount { font-size: 32px; font-weight: bold; color: #38bdf8; margin: 16px 0; }
                    .btn { display: block; width: 100%; padding: 14px; margin: 10px 0; border: none; border-radius: 8px; font-weight: bold; font-size: 15px; cursor: pointer; transition: 0.2s; }
                    .btn-success { background: #10b981; color: white; }
                    .btn-success:hover { background: #059669; }
                    .btn-danger { background: #ef4444; color: white; }
                    .btn-danger:hover { background: #dc2626; }
                    .info { color: #94a3b8; font-size: 13px; line-height: 1.6; margin-bottom: 20px; }
                </style>
            </head>
            <body>
                <div class="card">
                    <div style="font-size: 40px;">🏦</div>
                    <h2 style="margin: 8px 0;">Demo Payment Gateway</h2>
                    <p class="info">Môi trường giả lập đồ án PRN222<br>Mã giao dịch: <strong>{{id}}</strong></p>
                    <div class="amount">{{amount:N0}} đ</div>
                    <p class="info">Hạn giữ chỗ đến: <strong>{{holdUntil ?? "-"}}</strong></p>
                    <p class="info">Chọn kết quả giả lập bạn muốn gửi về máy chủ:</p>
                    <button class="btn btn-success" onclick="completePayment(true)">✅ Giả lập Thanh Toán Thành Công</button>
                    <button class="btn btn-danger" onclick="completePayment(false)">❌ Giả lập Thanh Toán Thất Bại</button>
                    <div id="status" style="margin-top: 16px; font-size: 14px; color: #facc15;"></div>
                </div>

                <script>
                    async function completePayment(isSuccess) {
                        const statusDiv = document.getElementById('status');
                        statusDiv.innerText = 'Đang gửi kết quả về hệ thống...';
                        try {
                            const res = await fetch('/api/payments/demo/{{id}}/complete', {
                                method: 'POST',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({ isSuccess: isSuccess, failureReason: isSuccess ? null : 'Simulated card declined' })
                            });
                            if (res.ok) {
                                statusDiv.style.color = '#34d399';
                                statusDiv.innerText = 'Giao dịch đã được ghi nhận! Bạn có thể quay lại ứng dụng WPF.';
                            } else {
                                statusDiv.style.color = '#f87171';
                                statusDiv.innerText = 'Máy chủ phản hồi lỗi hoặc yêu cầu đăng nhập nhân viên/khách.';
                            }
                        } catch (err) {
                            statusDiv.style.color = '#f87171';
                            statusDiv.innerText = 'Lỗi kết nối: ' + err.message;
                        }
                    }
                </script>
            </body>
            </html>
            """;

        return Content(html, "text/html");
    }

    private long GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Phiên đăng nhập không hợp lệ hoặc thông tin người dùng không được tìm thấy.");
        }
        return userId;
    }

    private IReadOnlyList<string> GetCurrentUserRoles()
    {
        return User.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();
    }
}
