using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Agreements;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Invoices;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class PaymentIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public PaymentIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private string GenerateToken(user user, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var tokenGen = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var (token, _) = tokenGen.GenerateToken(user, new[] { role });
        return token;
    }

    private async Task<(user Owner, user OtherCustomer, policy_version Policy, reservation Reservation)> SeedDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        if (!await db.roles.AnyAsync(r => r.code == RoleConstants.StorageCustomer))
        {
            db.roles.Add(new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" });
        }

        // Owner Customer 50
        var u50 = await db.users.FirstOrDefaultAsync(u => u.id == 50);
        if (u50 == null)
        {
            u50 = new user
            {
                id = 50,
                email = "cust50@test.com",
                password_hash = hasher.HashPassword("TestPass123!"),
                status = UserStatusConstants.Active,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.users.Add(u50);
            db.user_roles.Add(new user_role { user_id = 50, role_id = 1, granted_at = DateTimeOffset.UtcNow });
            db.customer_profiles.Add(new customer_profile
            {
                user_id = 50,
                full_name = "Customer Fifty",
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
        }

        // Other Customer 60
        var u60 = await db.users.FirstOrDefaultAsync(u => u.id == 60);
        if (u60 == null)
        {
            u60 = new user
            {
                id = 60,
                email = "cust60@test.com",
                password_hash = hasher.HashPassword("TestPass123!"),
                status = UserStatusConstants.Active,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.users.Add(u60);
            db.user_roles.Add(new user_role { user_id = 60, role_id = 1, granted_at = DateTimeOffset.UtcNow });
            db.customer_profiles.Add(new customer_profile
            {
                user_id = 60,
                full_name = "Customer Sixty",
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
        }

        // Facility 501
        var fac = await db.facilities.FirstOrDefaultAsync(f => f.id == 501);
        if (fac == null)
        {
            fac = new facility
            {
                id = 501,
                code = "FAC-501",
                name = "Kho Integration 501",
                address_line = "501 Nguyen Trai",
                city = "Hanoi",
                status = "active",
                timezone = "Asia/Ho_Chi_Minh"
            };
            db.facilities.Add(fac);
        }

        // Unit Type 501
        var ut = await db.unit_types.FirstOrDefaultAsync(u => u.id == 501);
        if (ut == null)
        {
            ut = new unit_type
            {
                id = 501,
                code = "UT-501",
                name = "Kho Standard 501",
                area_m2 = 5.0m,
                climate_controlled = false,
                is_active = true
            };
            db.unit_types.Add(ut);
        }

        // Facility Rate 501
        var rate = await db.facility_rates.FirstOrDefaultAsync(r => r.id == 501);
        if (rate == null)
        {
            rate = new facility_rate
            {
                id = 501,
                facility_id = 501,
                unit_type_id = 501,
                monthly_rate = 1_000_000m,
                deposit_amount = 1_000_000m,
                booking_fee = 50_000m,
                valid_from = new DateOnly(2020, 1, 1)
            };
            db.facility_rates.Add(rate);
        }

        // Policy Version
        var policy = await db.policy_versions.FirstOrDefaultAsync(p => p.id == 501);
        if (policy == null)
        {
            policy = new policy_version
            {
                id = 501,
                policy_type = "rental_terms",
                version = "2026.1-TEST",
                content = "{\"terms\":\"Test Policy Terms\"}",
                valid_from = new DateOnly(2020, 1, 1),
                created_at = DateTimeOffset.UtcNow
            };
            db.policy_versions.Add(policy);
        }

        // Reservation
        var resId = (long)Random.Shared.Next(10000, 999999);

        var res = new reservation
        {
            id = resId,
            reservation_code = $"RES-INTEG-{resId}",
            customer_id = 50,
            facility_id = 501,
            unit_type_id = 501,
            facility_rate_id = 501,
            start_date = DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
            end_date = DateOnly.FromDateTime(DateTime.Today.AddDays(5)).AddMonths(3),
            monthly_rate_snapshot = 1_000_000m,
            deposit_snapshot = 1_000_000m,
            booking_fee_snapshot = 50_000m,
            discount_snapshot = 0m,
            quoted_total = 2_050_000m,
            status = "pending",
            hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.reservations.Add(res);

        await db.SaveChangesAsync();

        return (u50, u60, policy, res);
    }

    [Fact]
    public async Task GetCheckoutQuote_OwnerCustomer_ReturnsOkWithAccuratePricing()
    {
        var (owner, _, policy, res) = await SeedDataAsync();
        var token = GenerateToken(owner, RoleConstants.StorageCustomer);

        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/reservations/{res.id}/checkout-quote");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse<CheckoutQuoteResponse>>(JsonOpts);
        Assert.NotNull(apiResponse);
        Assert.True(apiResponse.Success);
        var quote = apiResponse.Data;
        Assert.NotNull(quote);
        Assert.Equal(res.id, quote.ReservationId);
        Assert.Equal(1_000_000m, quote.DepositSnapshot);
        Assert.Equal(1_000_000m, quote.MonthlyRateSnapshot);
        Assert.Equal(50_000m, quote.BookingFeeSnapshot);
        Assert.Equal(0m, quote.DiscountAmount);
        Assert.Equal(0m, quote.TaxAmount); // Demo tax_amount = 0
        Assert.Equal(2_050_000m, quote.QuotedTotal);
        Assert.Equal(policy.id, quote.PolicyVersionId);
    }

    [Fact]
    public async Task GetCheckoutQuote_OtherCustomer_ReturnsForbidden()
    {
        var (_, otherCustomer, _, res) = await SeedDataAsync();
        var token = GenerateToken(otherCustomer, RoleConstants.StorageCustomer);

        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/reservations/{res.id}/checkout-quote");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_MissingIdempotencyHeader_ReturnsBadRequest()
    {
        var (owner, _, policy, res) = await SeedDataAsync();
        var token = GenerateToken(owner, RoleConstants.StorageCustomer);

        var checkoutBody = new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "demo"
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/payments/checkout")
        {
            Content = JsonContent.Create(checkoutBody)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Header Idempotency-Key is intentionally omitted

        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_ValidRequest_CreatesPaymentAndAllowsCompletion()
    {
        var (owner, _, policy, res) = await SeedDataAsync();
        var token = GenerateToken(owner, RoleConstants.StorageCustomer);

        var checkoutBody = new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "demo"
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/payments/checkout")
        {
            Content = JsonContent.Create(checkoutBody)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("Idempotency-Key", $"IDEM-INTEG-{Guid.NewGuid():N}");

        var checkoutResponse = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, checkoutResponse.StatusCode);

        var checkoutApiResp = await checkoutResponse.Content.ReadFromJsonAsync<ApiResponse<CheckoutResponse>>(JsonOpts);
        Assert.NotNull(checkoutApiResp);
        Assert.True(checkoutApiResp.Success);
        var checkoutResult = checkoutApiResp.Data;
        Assert.NotNull(checkoutResult);
        Assert.Equal("pending", checkoutResult.Status);
        Assert.True(checkoutResult.PaymentId > 0);
        Assert.True(checkoutResult.InvoiceId > 0);

        // Complete the demo payment
        var completeBody = new DemoPaymentCompleteRequest
        {
            IsSuccess = true
        };
        var completeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/demo/{checkoutResult.PaymentId}/complete")
        {
            Content = JsonContent.Create(completeBody)
        };
        completeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var completeResponse = await _client.SendAsync(completeReq);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        var completeApiResp = await completeResponse.Content.ReadFromJsonAsync<ApiResponse<PaymentDetailResponse>>(JsonOpts);
        Assert.NotNull(completeApiResp);
        Assert.True(completeApiResp.Success);
        var paymentDetail = completeApiResp.Data;
        Assert.NotNull(paymentDetail);
        Assert.Equal("succeeded", paymentDetail.Status);
        Assert.NotNull(paymentDetail.PaidAt);

        // Verify Invoice via GET /api/invoices/{id}
        var invReq = new HttpRequestMessage(HttpMethod.Get, $"/api/invoices/{checkoutResult.InvoiceId}");
        invReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var invResponse = await _client.SendAsync(invReq);
        Assert.Equal(HttpStatusCode.OK, invResponse.StatusCode);

        var invApiResp = await invResponse.Content.ReadFromJsonAsync<ApiResponse<InvoiceDetailResponse>>(JsonOpts);
        Assert.NotNull(invApiResp);
        Assert.True(invApiResp.Success);
        var invDetail = invApiResp.Data;
        Assert.NotNull(invDetail);
        Assert.Equal("paid", invDetail.Status);
        Assert.True(invDetail.Lines.Count >= 2); // Deposit and Rent at least

        // Verify Rental Agreement via GET /api/agreements/{id}
        Assert.NotNull(paymentDetail.AgreementId);
        var agrReq = new HttpRequestMessage(HttpMethod.Get, $"/api/agreements/{paymentDetail.AgreementId.Value}");
        agrReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var agrResponse = await _client.SendAsync(agrReq);
        Assert.Equal(HttpStatusCode.OK, agrResponse.StatusCode);

        var agrApiResp = await agrResponse.Content.ReadFromJsonAsync<ApiResponse<RentalAgreementDetailResponse>>(JsonOpts);
        Assert.NotNull(agrApiResp);
        Assert.True(agrApiResp.Success);
        var agrDetail = agrApiResp.Data;
        Assert.NotNull(agrDetail);
        Assert.Equal("scheduled", agrDetail.Status); // BR-AGR-01: Scheduled status
        Assert.Null(agrDetail.SignedAt);             // Not signed yet
        Assert.Equal(res.deposit_snapshot, agrDetail.DepositBalance);
    }

    [Fact]
    public async Task VnpayIpn_InvalidSignature_ReturnsCode97()
    {
        var response = await _client.GetAsync("/api/payments/vnpay/ipn?vnp_TxnRef=123&vnp_SecureHash=invalidsignature");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(JsonOpts);
        Assert.NotNull(doc);
        var rspCode = doc.RootElement.GetProperty("rspCode").GetString();
        Assert.Equal("97", rspCode);
    }

    [Fact]
    public async Task VnpayIpn_ValidSignatureUnknownTxnRef_ReturnsCode01()
    {
        var rawParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = "100000000",
            ["vnp_ResponseCode"] = "00",
            ["vnp_TmnCode"] = "DEMOTMN01",
            ["vnp_TransactionNo"] = "999999",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = "PAY-UNKNOWN-99999"
        };

        var hashDataBuilder = new System.Text.StringBuilder();
        foreach (var (key, value) in rawParams)
        {
            if (hashDataBuilder.Length > 0) hashDataBuilder.Append('&');
            hashDataBuilder.Append(System.Net.WebUtility.UrlEncode(key)).Append('=').Append(System.Net.WebUtility.UrlEncode(value));
        }
        var validHash = SelfStorageManagementSystem.BusinessLogic.Services.Implementations.VnpayGateway.ComputeHmacSha512("SECRETTESTKEY1234567890ABCDEF12", hashDataBuilder.ToString());

        var queryString = string.Join("&", rawParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}")) + $"&vnp_SecureHash={validHash}";
        var response = await _client.GetAsync($"/api/payments/vnpay/ipn?{queryString}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(JsonOpts);
        Assert.NotNull(doc);
        var rspCode = doc.RootElement.GetProperty("rspCode").GetString();
        Assert.Equal("01", rspCode); // Order not found
    }

    [Fact]
    public async Task VnpayIpn_AmountMismatch_ReturnsCode04()
    {
        var (owner, _, policy, res) = await SeedDataAsync();
        var token = GenerateToken(owner, RoleConstants.StorageCustomer);

        var checkoutBody = new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "vnpay"
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/payments/checkout")
        {
            Content = JsonContent.Create(checkoutBody)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("Idempotency-Key", $"IDEM-VNP-AMT-{Guid.NewGuid():N}");
        var checkoutResp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, checkoutResp.StatusCode);

        var checkoutData = (await checkoutResp.Content.ReadFromJsonAsync<ApiResponse<CheckoutResponse>>(JsonOpts))!.Data!;

        var rawParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = "50000000", // Wrong amount!
            ["vnp_ResponseCode"] = "00",
            ["vnp_TmnCode"] = "DEMOTMN01",
            ["vnp_TransactionNo"] = "888888",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = checkoutData.PaymentId.ToString()
        };

        var hashDataBuilder = new System.Text.StringBuilder();
        foreach (var (key, value) in rawParams)
        {
            if (hashDataBuilder.Length > 0) hashDataBuilder.Append('&');
            hashDataBuilder.Append(System.Net.WebUtility.UrlEncode(key)).Append('=').Append(System.Net.WebUtility.UrlEncode(value));
        }
        var validHash = SelfStorageManagementSystem.BusinessLogic.Services.Implementations.VnpayGateway.ComputeHmacSha512("SECRETTESTKEY1234567890ABCDEF12", hashDataBuilder.ToString());

        var queryString = string.Join("&", rawParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}")) + $"&vnp_SecureHash={validHash}";
        var response = await _client.GetAsync($"/api/payments/vnpay/ipn?{queryString}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(JsonOpts);
        Assert.NotNull(doc);
        Assert.Equal("04", doc.RootElement.GetProperty("rspCode").GetString());
    }

    [Fact]
    public async Task VnpayIpn_WrongCurrency_ReturnsCode04()
    {
        var (owner, _, policy, res) = await SeedDataAsync();
        var token = GenerateToken(owner, RoleConstants.StorageCustomer);

        var checkoutBody = new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "vnpay"
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/payments/checkout")
        {
            Content = JsonContent.Create(checkoutBody)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("Idempotency-Key", $"IDEM-VNP-CURR-{Guid.NewGuid():N}");
        var checkoutResp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, checkoutResp.StatusCode);

        var checkoutData = (await checkoutResp.Content.ReadFromJsonAsync<ApiResponse<CheckoutResponse>>(JsonOpts))!.Data!;

        var rawParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = ((long)(checkoutData.Amount * 100m)).ToString(),
            ["vnp_CurrCode"] = "USD", // Wrong currency!
            ["vnp_ResponseCode"] = "00",
            ["vnp_TmnCode"] = "DEMOTMN01",
            ["vnp_TransactionNo"] = "888889",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = checkoutData.PaymentId.ToString()
        };

        var hashDataBuilder = new System.Text.StringBuilder();
        foreach (var (key, value) in rawParams)
        {
            if (hashDataBuilder.Length > 0) hashDataBuilder.Append('&');
            hashDataBuilder.Append(System.Net.WebUtility.UrlEncode(key)).Append('=').Append(System.Net.WebUtility.UrlEncode(value));
        }
        var validHash = SelfStorageManagementSystem.BusinessLogic.Services.Implementations.VnpayGateway.ComputeHmacSha512("SECRETTESTKEY1234567890ABCDEF12", hashDataBuilder.ToString());

        var queryString = string.Join("&", rawParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}")) + $"&vnp_SecureHash={validHash}";
        var response = await _client.GetAsync($"/api/payments/vnpay/ipn?{queryString}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(JsonOpts);
        Assert.NotNull(doc);
        Assert.Equal("04", doc.RootElement.GetProperty("rspCode").GetString());
    }

    [Fact]
    public async Task DemoPaymentComplete_RejectedForVnpayPayment()
    {
        var (owner, _, policy, res) = await SeedDataAsync();
        var token = GenerateToken(owner, RoleConstants.StorageCustomer);

        var checkoutBody = new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "vnpay"
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/payments/checkout")
        {
            Content = JsonContent.Create(checkoutBody)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("Idempotency-Key", $"IDEM-VNP-REJ-{Guid.NewGuid():N}");
        var checkoutResp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, checkoutResp.StatusCode);

        var checkoutData = (await checkoutResp.Content.ReadFromJsonAsync<ApiResponse<CheckoutResponse>>(JsonOpts))!.Data!;

        // Attempting to simulate completion for a VNPAY payment must be rejected with 400 Bad Request
        var completeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/demo/{checkoutData.PaymentId}/complete")
        {
            Content = JsonContent.Create(new DemoPaymentCompleteRequest { IsSuccess = true })
        };
        completeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var completeResp = await _client.SendAsync(completeReq);
        Assert.Equal(HttpStatusCode.BadRequest, completeResp.StatusCode);
    }

    [Fact]
    public async Task RefundEndpoints_CustomerForbidden_ManagerAllowed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (owner, _, _, reservation) = await SeedDataAsync();
        var customerToken = GenerateToken(owner, RoleConstants.StorageCustomer);

        var mgrRole = await db.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.FacilityManager);
        if (mgrRole == null)
        {
            mgrRole = new role { id = 2, code = RoleConstants.FacilityManager, display_name = "Manager" };
            db.roles.Add(mgrRole);
            await db.SaveChangesAsync();
        }

        var mgr = await db.users.FirstOrDefaultAsync(u => u.id == 99);
        if (mgr == null)
        {
            mgr = new user
            {
                id = 99,
                email = "manager99@test.com",
                password_hash = hasher.HashPassword("TestPass123!"),
                status = UserStatusConstants.Active,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.users.Add(mgr);
            db.user_roles.Add(new user_role { user_id = 99, role_id = mgrRole.id, granted_at = DateTimeOffset.UtcNow });
            db.employee_profiles.Add(new employee_profile
            {
                user_id = 99,
                employee_code = "EMP-MGR-99",
                full_name = "Facility Manager 99",
                employment_status = "active",
                hire_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = 99,
                facility_id = 501,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-10)
            });
            await db.SaveChangesAsync();
        }
        else
        {
            if (!await db.user_roles.AnyAsync(ur => ur.user_id == 99 && ur.role_id == mgrRole.id))
            {
                db.user_roles.Add(new user_role { user_id = 99, role_id = mgrRole.id, granted_at = DateTimeOffset.UtcNow });
            }
            if (!await db.employee_profiles.AnyAsync(ep => ep.user_id == 99))
            {
                db.employee_profiles.Add(new employee_profile
                {
                    user_id = 99,
                    employee_code = "EMP-MGR-99",
                    full_name = "Facility Manager 99",
                    employment_status = "active",
                    hire_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                });
            }
            if (!await db.staff_facility_assignments.AnyAsync(sfa => sfa.employee_id == 99 && sfa.facility_id == 501))
            {
                db.staff_facility_assignments.Add(new staff_facility_assignment
                {
                    employee_id = 99,
                    facility_id = 501,
                    assignment_role = RoleConstants.FacilityManager,
                    starts_at = DateTimeOffset.UtcNow.AddDays(-10)
                });
            }
            await db.SaveChangesAsync();
        }
        var mgrToken = GenerateToken(mgr, RoleConstants.FacilityManager);

        // Seed a refund
        var paymentId = (long)Random.Shared.Next(70000, 79999);
        var refundId = (long)Random.Shared.Next(80000, 89999);

        var invoice = new invoice
        {
            id = (long)Random.Shared.Next(60000, 69999),
            invoice_no = $"INV-TEST-{paymentId}",
            customer_id = owner.id,
            reservation_id = reservation.id,
            issue_date = DateOnly.FromDateTime(DateTime.Today),
            due_date = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            currency = "VND",
            subtotal_amount = 1_000_000m,
            total_amount = 1_000_000m,
            status = "paid",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.invoices.Add(invoice);

        var payment = new payment
        {
            id = paymentId,
            customer_id = owner.id,
            target_invoice_id = invoice.id,
            amount = 1_000_000m,
            currency = "VND",
            method = "vnpay",
            provider = "vnpay",
            idempotency_key = $"IDEM-REF-{paymentId}",
            status = "succeeded",
            metadata = "{\"reconciliation_required\":true}",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.payments.Add(payment);

        var refundRecord = new refund
        {
            id = refundId,
            payment_id = paymentId,
            amount = 1_000_000m,
            currency = "VND",
            reason = "Late payment reconciliation",
            provider = "vnpay",
            idempotency_key = $"REF-{refundId}",
            status = "requested",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.refunds.Add(refundRecord);
        await db.SaveChangesAsync();

        // 1. Customer cannot list refunds (403 Forbidden)
        var custGetReq = new HttpRequestMessage(HttpMethod.Get, "/api/payments/refunds");
        custGetReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        var custGetResp = await _client.SendAsync(custGetReq);
        Assert.Equal(HttpStatusCode.Forbidden, custGetResp.StatusCode);

        // 2. Manager can list refunds (200 OK)
        var mgrGetReq = new HttpRequestMessage(HttpMethod.Get, "/api/payments/refunds");
        mgrGetReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var mgrGetResp = await _client.SendAsync(mgrGetReq);
        Assert.Equal(HttpStatusCode.OK, mgrGetResp.StatusCode);

        // 3. Customer cannot review refund (403 Forbidden)
        var custReviewReq = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/refunds/{refundId}/review")
        {
            Content = JsonContent.Create(new ReviewRefundRequest { Decision = "approved", Reason = "Test" })
        };
        custReviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        var custReviewResp = await _client.SendAsync(custReviewReq);
        Assert.Equal(HttpStatusCode.Forbidden, custReviewResp.StatusCode);

        // 4. Manager can review refund (200 OK)
        var mgrReviewReq = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/refunds/{refundId}/review")
        {
            Content = JsonContent.Create(new ReviewRefundRequest { Decision = "approved", Reason = "Manager approved refund" })
        };
        mgrReviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mgrToken);
        var mgrReviewResp = await _client.SendAsync(mgrReviewReq);
        Assert.Equal(HttpStatusCode.OK, mgrReviewResp.StatusCode);

        var reviewApiResp = await mgrReviewResp.Content.ReadFromJsonAsync<ApiResponse<RefundDetailResponse>>(JsonOpts);
        Assert.NotNull(reviewApiResp);
        Assert.True(reviewApiResp.Success);
        Assert.Equal("approved", reviewApiResp.Data!.Status);
        Assert.Equal("approved", reviewApiResp.Data.Decision);
    }
}
