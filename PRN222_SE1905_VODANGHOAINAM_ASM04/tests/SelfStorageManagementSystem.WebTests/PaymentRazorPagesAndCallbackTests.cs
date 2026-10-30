using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.WebTests.Fixtures;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Bộ kiểm thử tích hợp toàn diện cho Task T12:
/// 1. Checkout: Báo giá từ server, áp dụng voucher, chấp thuận điều khoản, Idempotency-Key ổn định, chống double submit.
/// 2. Demo Simulator: Thao tác web hoàn toàn bằng Cookie + Antiforgery, không có hướng dẫn quay về WPF.
/// 3. Demo Simulator Security: Chặn hoàn toàn ở Production (403) và khi AllowDemoSimulator = false.
/// 4. VNPAY Callback Adapters: Cấu hình URL port 7041, IPN là nguồn sự thật duy nhất, kiểm tra chữ ký HMAC-SHA512, chống replay, kiểm tra số tiền.
/// 5. VNPAY Return: Chỉ hiển thị, không thay đổi trạng thái tài chính.
/// 6. Xử lý quá hạn & Late payment: Hold hết hạn từ chối checkout, IPN muộn kích hoạt đối soát (reconciliation_required).
/// 7. Phân quyền & Sở hữu (Ownership & Scope): Khách hàng không xem được giao dịch/hóa đơn/hợp đồng của người khác; Nhân viên đối soát và duyệt hoàn tiền theo cơ sở phân công.
/// 8. Chi tiết Hóa đơn & Hợp đồng: Hiển thị đúng phân rã chi phí và thông báo trạng thái 'scheduled' chờ nhận kho tại quầy.
/// </summary>
public class PaymentRazorPagesAndCallbackTests : IClassFixture<PaymentWebTestFixture>
{
    private readonly PaymentWebTestFixture _factory;

    public PaymentRazorPagesAndCallbackTests(PaymentWebTestFixture factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(bool allowAutoRedirect = false)
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    #region 1. Checkout Tests

    [Fact]
    public async Task CheckoutPage_Get_RendersQuoteAndStableIdempotencyKey()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        var response = await client.GetAsync($"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        // Kiểm tra hiển thị báo giá từ server
        Assert.Contains(seed.ActiveReservation.reservation_code, html);
        Assert.Contains("2.000.000", html); // Tiền cọc (1tr) + Tiền thuê tháng đầu (1tr)
        Assert.Contains(seed.PolicyVersion.version, html); // Phiên bản điều khoản hợp đồng
        Assert.Contains("IdempotencyKey", html);
        Assert.Contains("methodDemo", html);
        Assert.Contains("methodVnpay", html);
    }

    [Fact]
    public async Task CheckoutPage_ApplyPromo_UpdatesDiscountAndQuotedTotal()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var applyPromoReq = new HttpRequestMessage(HttpMethod.Post, $"/Payments/Checkout?handler=ApplyPromo");
        applyPromoReq.Headers.Add("Cookie", cookieHeader);
        applyPromoReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["PromotionCode"] = seed.Promotion.code,
            ["IdempotencyKey"] = $"IDEM-{suffix}",
            ["__RequestVerificationToken"] = token
        });

        var promoResp = await client.SendAsync(applyPromoReq);
        Assert.Equal(HttpStatusCode.OK, promoResp.StatusCode);

        var html = await promoResp.Content.ReadAsStringAsync();
        Assert.Contains("Đã áp dụng mã ưu đãi", html);
        Assert.Contains("1.800.000", html); // 2,000,000 - 200,000 = 1,800,000
    }

    [Fact]
    public async Task CheckoutPage_PostWithoutPolicyAgreement_ReturnsValidationError()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cookieHeader);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "false", // Chưa chấp thuận điều khoản
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = $"IDEM-{suffix}",
            ["__RequestVerificationToken"] = token
        });

        var resp = await client.SendAsync(checkoutReq);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Bạn phải đọc và đồng ý với điều khoản hợp đồng", html);
    }

    [Fact]
    public async Task CheckoutPage_PostDemo_CreatesPaymentAndRedirectsToDemoSimulator()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-DEMO-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cookieHeader);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = token
        });

        var resp = await client.SendAsync(checkoutReq);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        var location = resp.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Contains("/Payments/DemoSimulator?paymentId=", location);

        // Kiểm tra Payment được tạo trong DB
        using var db = _factory.CreateDbContext();
        var payment = await db.payments.FirstOrDefaultAsync(p => p.idempotency_key == idempotencyKey);
        Assert.NotNull(payment);
        Assert.Equal("demo", payment.provider);
        Assert.Equal("pending", payment.status);
    }

    [Fact]
    public async Task CheckoutPage_IdempotentReplay_ReturnsSamePaymentWithoutDuplicates()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-REPLAY-{suffix}";

        // Submit 1st time
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        req1.Headers.Add("Cookie", cookieHeader);
        req1.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = token
        });
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Redirect, resp1.StatusCode);
        var location1 = resp1.Headers.Location?.ToString();

        // Submit 2nd time (simulate duplicate click or network retry)
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        req2.Headers.Add("Cookie", cookieHeader);
        req2.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = token
        });
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Redirect, resp2.StatusCode);
        var location2 = resp2.Headers.Location?.ToString();

        // Cả 2 lần đều redirect về cùng 1 Payment ID
        Assert.Equal(location1, location2);

        using var db = _factory.CreateDbContext();
        var paymentCount = await db.payments.CountAsync(p => p.idempotency_key == idempotencyKey);
        Assert.Equal(1, paymentCount); // Không bị tạo trùng
    }

    #endregion

    #region 2. Demo Simulator Web Execution Tests

    [Fact]
    public async Task DemoSimulator_Get_RendersPaymentDetailsAndZeroWpfInstructions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // Khởi tạo payment qua checkout
        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-NOWPF-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cookieHeader);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = token
        });
        var checkoutResp = await client.SendAsync(checkoutReq);
        var redirectUrl = checkoutResp.Headers.Location!.ToString();

        // Truy cập trang DemoSimulator trực tiếp trên web
        var simResp = await client.GetAsync(redirectUrl);
        Assert.Equal(HttpStatusCode.OK, simResp.StatusCode);

        var html = await simResp.Content.ReadAsStringAsync();
        Assert.Contains("Cổng Thanh Toán Giả Lập", html);
        Assert.Contains("Giả Lập Thành Công", html);
        Assert.Contains("Giả Lập Thất Bại", html);

        // Kiểm tra loại bỏ hoàn toàn hướng dẫn quay về ứng dụng WPF
        Assert.DoesNotContain("WPF", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("máy bàn", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("desktop app", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DemoSimulator_PostComplete_Success_ConfirmsPaymentAndCreatesAgreement()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // 1. Checkout
        var (cToken, cCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-SUCC-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cCookie);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = cToken
        });
        var checkoutResp = await client.SendAsync(checkoutReq);
        var redirectUrl = checkoutResp.Headers.Location!.ToString();

        // Lấy PaymentId từ URL
        var match = Regex.Match(redirectUrl, @"paymentId=(\d+)");
        Assert.True(match.Success);
        var paymentId = long.Parse(match.Groups[1].Value);

        // 2. Demo Complete Form Submit
        var (sToken, sCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, redirectUrl);
        var completeReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/DemoSimulator?handler=Complete");
        completeReq.Headers.Add("Cookie", sCookie);
        completeReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PaymentId"] = paymentId.ToString(),
            ["IsSuccess"] = "true",
            ["ProviderTransactionId"] = $"DEMO-TEST-{suffix}",
            ["__RequestVerificationToken"] = sToken
        });

        var completeResp = await client.SendAsync(completeReq);
        Assert.Equal(HttpStatusCode.Redirect, completeResp.StatusCode);
        Assert.Contains($"/Payments/Result?paymentId={paymentId}", completeResp.Headers.Location!.ToString());

        // 3. Kiểm tra DB: Payment Succeeded, Invoice Paid, Agreement Scheduled
        using var db = _factory.CreateDbContext();
        var payment = await db.payments.Include(p => p.target_invoice).FirstAsync(p => p.id == paymentId);
        Assert.Equal("succeeded", payment.status);

        var invoice = payment.target_invoice;
        Assert.NotNull(invoice);
        Assert.Equal("paid", invoice.status);

        var agreement = await db.rental_agreements.FirstOrDefaultAsync(a => a.reservation_id == seed.ActiveReservation.id);
        Assert.NotNull(agreement);
        Assert.Equal("scheduled", agreement.status); // Theo BR-AGR: Đã trả tiền, chờ nhận kho tại quầy
    }

    [Fact]
    public async Task DemoSimulator_PostComplete_Failure_RecordsFailureAndAllowsRetry()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // 1. Checkout
        var (cToken, cCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-FAIL-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cCookie);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = cToken
        });
        var checkoutResp = await client.SendAsync(checkoutReq);
        var redirectUrl = checkoutResp.Headers.Location!.ToString();
        var match = Regex.Match(redirectUrl, @"paymentId=(\d+)");
        var paymentId = long.Parse(match.Groups[1].Value);

        // 2. Demo Complete Failure
        var (sToken, sCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, redirectUrl);
        var failureReason = "Tài khoản không đủ số dư để thanh toán.";
        var completeReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/DemoSimulator?handler=Complete");
        completeReq.Headers.Add("Cookie", sCookie);
        completeReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PaymentId"] = paymentId.ToString(),
            ["IsSuccess"] = "false",
            ["FailureReason"] = failureReason,
            ["__RequestVerificationToken"] = sToken
        });

        var completeResp = await client.SendAsync(completeReq);
        Assert.Equal(HttpStatusCode.Redirect, completeResp.StatusCode);

        // 3. Kiểm tra DB: Payment Failed
        using var db = _factory.CreateDbContext();
        var payment = await db.payments.FirstAsync(p => p.id == paymentId);
        Assert.Equal("failed", payment.status);
        Assert.Equal(failureReason, payment.failure_reason);

        // 4. Thử lại (Retry): Khách có thể quay lại Checkout và tạo payment mới với Idempotency-Key mới
        var (rToken, rCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");
        var retryKey = $"IDEM-RETRY-{suffix}";
        var retryReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        retryReq.Headers.Add("Cookie", rCookie);
        retryReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = retryKey,
            ["__RequestVerificationToken"] = rToken
        });
        var retryResp = await client.SendAsync(retryReq);
        Assert.Equal(HttpStatusCode.Redirect, retryResp.StatusCode);
        Assert.Contains("/Payments/DemoSimulator", retryResp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task DemoSimulator_BlockedInProduction_Returns403Forbidden()
    {
        // Tạo factory chạy môi trường Production
        using var prodFactory = new PaymentWebTestFixture
        {
            EnvironmentName = "Production",
            AllowDemoSimulatorConfig = false
        };

        var client = prodFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Endpoint GET /api/payments/demo/1/simulator bị chặn 403
        var apiResp = await client.GetAsync("/api/payments/demo/1/simulator");
        Assert.Equal(HttpStatusCode.Forbidden, apiResp.StatusCode);

        // Endpoint Razor Page /Payments/DemoSimulator?paymentId=1 bị chặn 403
        var pageResp = await client.GetAsync("/Payments/DemoSimulator?paymentId=1");
        // Sẽ trả về 403 Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, pageResp.StatusCode);
    }

    #endregion

    #region 3. VNPAY Callback & Signature Verification Tests

    [Fact]
    public async Task VnpayCheckout_Post_GeneratesCheckoutUrlPointingTo7041()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-VNP-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cookieHeader);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "vnpay",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = token
        });

        var resp = await client.SendAsync(checkoutReq);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        var checkoutUrl = resp.Headers.Location!.ToString();
        Assert.Contains("vnpay", checkoutUrl.ToLowerInvariant());
        Assert.Contains("vnp_SecureHash=", checkoutUrl);
        // Cấu hình URL Return trỏ về 7041
        Assert.Contains("7041", checkoutUrl);
        Assert.Contains(Uri.EscapeDataString("/api/payments/vnpay/return"), checkoutUrl);
    }

    [Fact]
    public async Task VnpayIpn_ValidSignature_ConfirmsPaymentAndInvoice()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // 1. Checkout VNPAY
        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-IPN-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cookieHeader);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "vnpay",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = token
        });
        await client.SendAsync(checkoutReq);

        using var db = _factory.CreateDbContext();
        var payment = await db.payments.FirstAsync(p => p.idempotency_key == idempotencyKey);

        // 2. Gọi IPN với chữ ký hợp lệ
        var ipnParams = PaymentWebTestFixture.GenerateVnpayIpnParameters(payment.id, payment.amount, responseCode: "00");
        var queryString = string.Join("&", ipnParams.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));

        var anonymousClient = CreateClient();
        var ipnResp = await anonymousClient.GetAsync($"/api/payments/vnpay/ipn?{queryString}");
        Assert.Equal(HttpStatusCode.OK, ipnResp.StatusCode);

        var ipnJson = await ipnResp.Content.ReadAsStringAsync();
        Assert.True(ipnJson.Contains("\"00\"") && ipnJson.Contains("Confirm Success"), $"IPN response mismatch: {ipnJson}");

        // 3. Kiểm tra DB đã được xác nhận tài chính
        using var verifyDb = _factory.CreateDbContext();
        var verifiedPayment = await verifyDb.payments.Include(p => p.target_invoice).FirstAsync(p => p.id == payment.id);
        Assert.Equal("succeeded", verifiedPayment.status);
        Assert.Equal("paid", verifiedPayment.target_invoice?.status);

        var agreement = await verifyDb.rental_agreements.FirstOrDefaultAsync(a => a.reservation_id == seed.ActiveReservation.id);
        Assert.NotNull(agreement);
        Assert.Equal("scheduled", agreement.status);
    }

    [Fact]
    public async Task VnpayIpn_InvalidSignature_RejectsFinancialUpdate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // Checkout
        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-INVSIG-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cookieHeader);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "vnpay",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = token
        });
        await client.SendAsync(checkoutReq);

        using var db = _factory.CreateDbContext();
        var payment = await db.payments.FirstAsync(p => p.idempotency_key == idempotencyKey);

        // Gọi IPN với chữ ký bị sửa đổi (tampered)
        var ipnParams = PaymentWebTestFixture.GenerateVnpayIpnParameters(payment.id, payment.amount, tamperSignature: true);
        var queryString = string.Join("&", ipnParams.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));

        var anonymousClient = CreateClient();
        var ipnResp = await anonymousClient.GetAsync($"/api/payments/vnpay/ipn?{queryString}");
        Assert.Equal(HttpStatusCode.OK, ipnResp.StatusCode);

        var ipnJson = await ipnResp.Content.ReadAsStringAsync();
        Assert.True(ipnJson.Contains("\"97\"") && ipnJson.Contains("Invalid Signature"), $"IPN response mismatch: {ipnJson}");

        // Xác nhận trạng thái trong DB KHÔNG bị thay đổi (vẫn giữ pending)
        using var verifyDb = _factory.CreateDbContext();
        var unchangedPayment = await verifyDb.payments.FirstAsync(p => p.id == payment.id);
        Assert.Equal("pending", unchangedPayment.status);
    }

    [Fact]
    public async Task VnpayIpn_Replay_ReturnsCode02AlreadyConfirmed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // Checkout
        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-IPNREPLAY-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cookieHeader);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "vnpay",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = token
        });
        await client.SendAsync(checkoutReq);

        using var db = _factory.CreateDbContext();
        var payment = await db.payments.FirstAsync(p => p.idempotency_key == idempotencyKey);

        var ipnParams = PaymentWebTestFixture.GenerateVnpayIpnParameters(payment.id, payment.amount, responseCode: "00");
        var queryString = string.Join("&", ipnParams.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));

        var anonymousClient = CreateClient();
        // Lần 1: Thành công
        await anonymousClient.GetAsync($"/api/payments/vnpay/ipn?{queryString}");

        // Lần 2 (Replay): Phản hồi 02 Order already confirmed
        var replayResp = await anonymousClient.GetAsync($"/api/payments/vnpay/ipn?{queryString}");
        var replayJson = await replayResp.Content.ReadAsStringAsync();
        Assert.True(replayJson.Contains("\"02\"") && replayJson.Contains("Order already confirmed"), $"Replay IPN response mismatch: {replayJson}");
    }

    [Fact]
    public async Task VnpayIpn_AmountMismatch_ReturnsCode04InvalidAmount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // Checkout
        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-AMTMIS-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cookieHeader);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "vnpay",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = token
        });
        await client.SendAsync(checkoutReq);

        using var db = _factory.CreateDbContext();
        var payment = await db.payments.FirstAsync(p => p.idempotency_key == idempotencyKey);

        // Gửi IPN với số tiền sai lệch (amount * 2)
        var ipnParams = PaymentWebTestFixture.GenerateVnpayIpnParameters(payment.id, payment.amount * 2m, responseCode: "00");
        var queryString = string.Join("&", ipnParams.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));

        var anonymousClient = CreateClient();
        var ipnResp = await anonymousClient.GetAsync($"/api/payments/vnpay/ipn?{queryString}");
        var ipnJson = await ipnResp.Content.ReadAsStringAsync();
        Assert.True(ipnJson.Contains("\"04\"") && ipnJson.Contains("Invalid Amount"), $"Amount mismatch IPN response: {ipnJson}");
        Assert.Contains("Invalid Amount", ipnJson);
    }

    [Fact]
    public async Task VnpayReturn_DisplayOnly_DoesNotMutateFinancialState()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // Checkout để tạo payment có trạng thái 'pending' trong DB
        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-RET-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cookieHeader);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "vnpay",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = token
        });
        await client.SendAsync(checkoutReq);

        using var dbBefore = _factory.CreateDbContext();
        var payment = await dbBefore.payments.FirstAsync(p => p.idempotency_key == idempotencyKey);
        Assert.Equal("pending", payment.status);
        Assert.Null(payment.paid_at);

        // Khách hàng được chuyển hướng về trang Return với mã thành công 00
        var anonymousClient = CreateClient();
        var returnResp = await anonymousClient.GetAsync($"/api/payments/vnpay/return?vnp_TxnRef={payment.id}&vnp_ResponseCode=00");
        Assert.Equal(HttpStatusCode.OK, returnResp.StatusCode);

        var html = await returnResp.Content.ReadAsStringAsync();
        Assert.Contains("Đã Hoàn Tất Thao Tác Tại VNPAY", html);
        Assert.Contains("Mã giao dịch tham chiếu:", html);
        Assert.Contains(payment.id.ToString(), html);
        Assert.Contains($"/Payments/Details?id={payment.id}", html);
        // Kiểm tra lưu ý kiến trúc: Trang Return chỉ phục vụ hiển thị
        Assert.Contains("IPN (Instant Payment Notification)", html);

        // Nguồn sự thật tài chính: DB tuyệt đối KHÔNG bị cập nhật trạng thái qua Return URL
        using var dbAfter = _factory.CreateDbContext();
        var paymentAfter = await dbAfter.payments.FirstAsync(p => p.id == payment.id);
        Assert.Equal("pending", paymentAfter.status);
        Assert.Null(paymentAfter.paid_at);

        // Không tạo hóa đơn hoặc hợp đồng thuê qua Return URL (chỉ IPN mới có quyền tạo)
        var agreementExists = await dbAfter.rental_agreements.AnyAsync(a => a.reservation_id == seed.ActiveReservation.id);
        Assert.False(agreementExists, "Return URL không được tạo hợp đồng thuê.");
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("\"><img src=x onerror=alert(1)>")]
    [InlineData("\" onmouseover=\"alert('xss')\"")]
    public async Task VnpayReturn_XssPayloadInTxnRef_SanitizedAndDoesNotInjectScriptOrAttribute(string xssPayload)
    {
        var anonymousClient = CreateClient();
        var returnResp = await anonymousClient.GetAsync($"/api/payments/vnpay/return?vnp_TxnRef={WebUtility.UrlEncode(xssPayload)}&vnp_ResponseCode=00");
        Assert.Equal(HttpStatusCode.OK, returnResp.StatusCode);

        var html = await returnResp.Content.ReadAsStringAsync();

        // 1. Tuyệt đối không chứa thẻ script chưa mã hóa
        Assert.DoesNotContain("<script>alert('xss')</script>", html);
        Assert.DoesNotContain("<img src=x onerror=alert(1)>", html);
        Assert.DoesNotContain("onmouseover=\"alert('xss')\"", html);

        // 2. Không sinh href chứa payload độc hại
        Assert.DoesNotContain($"href=\"/Payments/Details?id={xssPayload}\"", html);

        // 3. Hiển thị cảnh báo mã tham chiếu không hợp lệ và cung cấp nút điều hướng an toàn
        Assert.Contains("Mã tham chiếu giao dịch không đúng định dạng", html);
        Assert.Contains("/Payments", html);
    }

    [Fact]
    public async Task VnpayReturn_InvalidFormatTxnRef_RendersWarningAndSafeFallbackLink()
    {
        var anonymousClient = CreateClient();
        var returnResp = await anonymousClient.GetAsync("/api/payments/vnpay/return?vnp_TxnRef=MALFORMED_NON_NUMERIC&vnp_ResponseCode=00");
        Assert.Equal(HttpStatusCode.OK, returnResp.StatusCode);

        var html = await returnResp.Content.ReadAsStringAsync();
        Assert.Contains("Mã tham chiếu giao dịch không đúng định dạng", html);
        Assert.DoesNotContain("/Payments/Details?id=MALFORMED_NON_NUMERIC", html);
        Assert.Contains("/Payments", html);
    }

    #endregion

    #region 4. Hold Expiration & Late Payment Tests

    [Fact]
    public async Task Checkout_ExpiredHold_RejectedWithErrorMessage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // Truy cập checkout của đơn đặt chỗ đã quá hạn 15 phút
        var response = await client.GetAsync($"/Payments/Checkout?reservationId={seed.ExpiredReservation.id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("hết hạn", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BR-RSV-01", html);
    }

    [Fact]
    public async Task LatePayment_IpnAfterHoldExpiration_MarksReconciliationRequired()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // 1. Checkout hợp lệ ban đầu
        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-LATE-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cookieHeader);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "vnpay",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = token
        });
        await client.SendAsync(checkoutReq);

        using (var db = _factory.CreateDbContext())
        {
            // Giả lập worker chạy hủy đơn do hết hạn 15 phút trước khi VNPAY gọi IPN
            var rsv = await db.reservations.FirstAsync(r => r.id == seed.ActiveReservation.id);
            rsv.hold_until = DateTimeOffset.UtcNow.AddMinutes(-5);
            rsv.status = "cancelled";
            await db.SaveChangesAsync();
        }

        using var dbQuery = _factory.CreateDbContext();
        var payment = await dbQuery.payments.FirstAsync(p => p.idempotency_key == idempotencyKey);

        // 2. IPN muộn từ VNPAY báo thành công
        var ipnParams = PaymentWebTestFixture.GenerateVnpayIpnParameters(payment.id, payment.amount, responseCode: "00");
        var queryString = string.Join("&", ipnParams.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));

        var anonymousClient = CreateClient();
        var ipnResp = await anonymousClient.GetAsync($"/api/payments/vnpay/ipn?{queryString}");
        Assert.Equal(HttpStatusCode.OK, ipnResp.StatusCode);

        // 3. Kiểm tra DB: Tiền đã trừ nhưng reservation đã cancelled -> Đánh dấu reconciliation_required
        using var verifyDb = _factory.CreateDbContext();
        var latePayment = await verifyDb.payments.FirstAsync(p => p.id == payment.id);
        Assert.Equal("succeeded", latePayment.status);
        Assert.True(latePayment.failure_reason == "LATE_PAYMENT_HOLD_EXPIRED" ||
                    latePayment.metadata.Contains("\"reconciliation_required\":true") ||
                    latePayment.metadata.Contains("\"reconciliation_required\": true"));
    }

    #endregion

    #region 5. Ownership & Cross-Customer Protection Tests

    [Fact]
    public async Task Checkout_OtherCustomerReservation_ReturnsForbiddenOrError()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);

        // Customer A đăng nhập nhưng truy cập đơn của Customer B
        var clientA = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);
        var resp = await clientA.GetAsync($"/Payments/Checkout?reservationId={seed.OtherCustomerReservation.id}");

        // Không được phép truy cập
        var html = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.StatusCode == HttpStatusCode.Forbidden || html.Contains("không có quyền"), "Phải chặn khách hàng truy cập đơn đặt chỗ của người khác.");
    }

    [Fact]
    public async Task PaymentAndInvoiceAndAgreement_Details_CrossCustomer_Forbidden()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var clientA = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // Hoàn tất 1 payment và hợp đồng cho Customer A
        var (cToken, cCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            clientA, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-OWNER-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cCookie);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = cToken
        });
        var checkoutResp = await clientA.SendAsync(checkoutReq);
        var redirectUrl = checkoutResp.Headers.Location!.ToString();
        var match = Regex.Match(redirectUrl, @"paymentId=(\d+)");
        var paymentId = long.Parse(match.Groups[1].Value);

        // Hoàn tất demo payment
        var (sToken, sCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(clientA, redirectUrl);
        var completeReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/DemoSimulator?handler=Complete");
        completeReq.Headers.Add("Cookie", sCookie);
        completeReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PaymentId"] = paymentId.ToString(),
            ["IsSuccess"] = "true",
            ["__RequestVerificationToken"] = sToken
        });
        await clientA.SendAsync(completeReq);

        using var db = _factory.CreateDbContext();
        var agreement = await db.rental_agreements.FirstAsync(a => a.reservation_id == seed.ActiveReservation.id);
        var invoice = await db.invoices.FirstAsync(i => i.reservation_id == seed.ActiveReservation.id);

        // Khách hàng B đăng nhập
        var clientB = await _factory.CreateAuthenticatedClientAsync(seed.OtherCustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // Khách hàng B không được xem Payment của A
        var payResp = await clientB.GetAsync($"/Payments/Details?id={paymentId}");
        Assert.True(payResp.StatusCode == HttpStatusCode.Forbidden ||
                    (payResp.StatusCode == HttpStatusCode.Redirect && payResp.Headers.Location!.ToString().Contains("Forbidden")),
                    "Payment Details của người khác phải bị chặn.");

        // Khách hàng B không được xem Invoice của A
        var invResp = await clientB.GetAsync($"/Invoices/Details?id={invoice.id}");
        Assert.True(invResp.StatusCode == HttpStatusCode.Forbidden ||
                    (invResp.StatusCode == HttpStatusCode.Redirect && invResp.Headers.Location!.ToString().Contains("Forbidden")),
                    "Invoice Details của người khác phải bị chặn.");

        // Khách hàng B không được xem Agreement của A
        var agrResp = await clientB.GetAsync($"/Agreements/Details?id={agreement.id}");
        Assert.True(agrResp.StatusCode == HttpStatusCode.Forbidden ||
                    (agrResp.StatusCode == HttpStatusCode.Redirect && agrResp.Headers.Location!.ToString().Contains("Forbidden")),
                    "Agreement Details của người khác phải bị chặn.");
    }

    #endregion

    #region 6. Reconciliation & Refund Review Tests

    [Fact]
    public async Task Manager_ViewsReconciliationListAndReviewsRefund_WithinScope()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);

        // 1. Tạo thanh toán và hợp đồng hoàn chỉnh cho đơn đặt chỗ
        var custClient = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);
        var (cToken, cCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            custClient, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-REFUNDFLOW-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cCookie);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = cToken
        });
        var checkoutResp = await custClient.SendAsync(checkoutReq);
        var match = Regex.Match(checkoutResp.Headers.Location!.ToString(), @"paymentId=(\d+)");
        var paymentId = long.Parse(match.Groups[1].Value);

        // Hoàn tất demo payment
        var (sToken, sCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(custClient, checkoutResp.Headers.Location!.ToString());
        var completeReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/DemoSimulator?handler=Complete");
        completeReq.Headers.Add("Cookie", sCookie);
        completeReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PaymentId"] = paymentId.ToString(),
            ["IsSuccess"] = "true",
            ["__RequestVerificationToken"] = sToken
        });
        await custClient.SendAsync(completeReq);

        // 2. Tạo 1 yêu cầu hoàn tiền (refund request) trong DB cho hợp đồng/thanh toán này
        long refundId;
        using (var db = _factory.CreateDbContext())
        {
            var p = await db.payments.FirstAsync(pay => pay.id == paymentId);
            var agr = await db.rental_agreements.FirstAsync(a => a.reservation_id == seed.ActiveReservation.id);

            var rf = new refund
            {
                payment_id = p.id,
                payment = p,
                agreement_id = agr.id,
                agreement = agr,
                amount = p.amount,
                currency = "VND",
                reason = "Khách hủy đơn và yêu cầu hoàn tiền",
                provider = "demo",
                idempotency_key = $"IDEM-REFUND-{suffix}",
                status = "requested",
                requested_by = seed.CustomerUser.id,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.refunds.Add(rf);
            await db.SaveChangesAsync();
            refundId = rf.id;
        }

        // Manager đăng nhập
        var managerClient = await _factory.CreateAuthenticatedClientAsync(seed.ManagerUser.email, PaymentWebTestFixture.DefaultPassword);

        // Truy cập /Payments
        var listResp = await managerClient.GetAsync("/Payments");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listHtml = await listResp.Content.ReadAsStringAsync();
        Assert.Contains("Đối Soát", listHtml);
        Assert.Contains("Yêu Cầu Hoàn Tiền", listHtml);

        // Duyệt hoàn tiền (POST ReviewRefund)
        var (token, cookieHeader) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(managerClient, "/Payments");
        var reviewReq = new HttpRequestMessage(HttpMethod.Post, "/Payments?handler=ReviewRefund");
        reviewReq.Headers.Add("Cookie", cookieHeader);
        reviewReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["refundId"] = refundId.ToString(),
            ["decision"] = "approved",
            ["reason"] = "Đồng ý hoàn tiền theo chính sách",
            ["__RequestVerificationToken"] = token
        });

        var reviewResp = await managerClient.SendAsync(reviewReq);
        Assert.Equal(HttpStatusCode.Redirect, reviewResp.StatusCode);

        // Kiểm tra DB: Refund đã approved
        using var verifyDb = _factory.CreateDbContext();
        var updatedRefund = await verifyDb.refunds.Include(r => r.refund_approval).FirstAsync(r => r.id == refundId);
        Assert.Equal("approved", updatedRefund.status);
        Assert.NotNull(updatedRefund.refund_approval);
        Assert.Equal("approved", updatedRefund.refund_approval.decision);
    }

    #endregion

    #region 7. Invoice & Agreement Itemized Display Tests

    [Fact]
    public async Task InvoiceDetails_RendersLineItemsAndFinancialTotals()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // Hoàn tất demo checkout để sinh invoice và agreement
        var (cToken, cCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-INVVIEW-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cCookie);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = cToken
        });
        var checkoutResp = await client.SendAsync(checkoutReq);
        var match = Regex.Match(checkoutResp.Headers.Location!.ToString(), @"paymentId=(\d+)");
        var paymentId = long.Parse(match.Groups[1].Value);

        // Hoàn tất
        var (sToken, sCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, checkoutResp.Headers.Location!.ToString());
        var completeReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/DemoSimulator?handler=Complete");
        completeReq.Headers.Add("Cookie", sCookie);
        completeReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PaymentId"] = paymentId.ToString(),
            ["IsSuccess"] = "true",
            ["__RequestVerificationToken"] = sToken
        });
        await client.SendAsync(completeReq);

        using var db = _factory.CreateDbContext();
        var invoice = await db.invoices.FirstAsync(i => i.reservation_id == seed.ActiveReservation.id);

        // Truy cập /Invoices/Details
        var invResp = await client.GetAsync($"/Invoices/Details?id={invoice.id}");
        Assert.Equal(HttpStatusCode.OK, invResp.StatusCode);

        var html = await invResp.Content.ReadAsStringAsync();
        Assert.Contains(invoice.invoice_no, html);
        Assert.Contains("Chi Tiết Hóa Đơn", html);
        Assert.Contains("2.000.000", html); // Tổng tiền
        Assert.Contains("Đã thanh toán", html);
    }

    [Fact]
    public async Task AgreementDetails_RendersScheduledNoticeAndTerms()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seed = await _factory.SeedPaymentEnvironmentAsync(suffix);
        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, PaymentWebTestFixture.DefaultPassword);

        // Hoàn tất demo checkout
        var (cToken, cCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(
            client, $"/Payments/Checkout?reservationId={seed.ActiveReservation.id}");

        var idempotencyKey = $"IDEM-AGRVIEW-{suffix}";
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/Checkout");
        checkoutReq.Headers.Add("Cookie", cCookie);
        checkoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ReservationId"] = seed.ActiveReservation.id.ToString(),
            ["AcceptedPolicyVersionId"] = seed.PolicyVersion.id.ToString(),
            ["AcceptPolicy"] = "true",
            ["PaymentMethod"] = "demo",
            ["IdempotencyKey"] = idempotencyKey,
            ["__RequestVerificationToken"] = cToken
        });
        var checkoutResp = await client.SendAsync(checkoutReq);
        var match = Regex.Match(checkoutResp.Headers.Location!.ToString(), @"paymentId=(\d+)");
        var paymentId = long.Parse(match.Groups[1].Value);

        var (sToken, sCookie) = await PaymentWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, checkoutResp.Headers.Location!.ToString());
        var completeReq = new HttpRequestMessage(HttpMethod.Post, "/Payments/DemoSimulator?handler=Complete");
        completeReq.Headers.Add("Cookie", sCookie);
        completeReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PaymentId"] = paymentId.ToString(),
            ["IsSuccess"] = "true",
            ["__RequestVerificationToken"] = sToken
        });
        await client.SendAsync(completeReq);

        using var db = _factory.CreateDbContext();
        var agreement = await db.rental_agreements.FirstAsync(a => a.reservation_id == seed.ActiveReservation.id);

        // Truy cập /Agreements/Details
        var agrResp = await client.GetAsync($"/Agreements/Details?id={agreement.id}");
        Assert.Equal(HttpStatusCode.OK, agrResp.StatusCode);

        var html = await agrResp.Content.ReadAsStringAsync();
        Assert.Contains(agreement.agreement_no, html);
        Assert.Contains("Hợp đồng dịch vụ kho lưu trữ tự quản", html);
        Assert.Contains("SCHEDULED", html); // Trạng thái đã lên lịch chờ quầy check-in
        Assert.Contains("Tiền đặt cọc", html);
    }

    #endregion
}
