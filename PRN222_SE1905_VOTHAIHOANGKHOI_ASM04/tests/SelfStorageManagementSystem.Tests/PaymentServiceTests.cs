using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class PaymentServiceTests
{
    private static readonly string[] CustomerRoles = new[] { RoleConstants.StorageCustomer };

    private SelfStorageDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SelfStorageDbContext(options);
    }

    private (PaymentService Service, SelfStorageDbContext Context, DemoGateway DemoGw, VnpayGateway VnpayGw) SetupService(string dbName, Dictionary<string, string?>? customConfig = null)
    {
        var context = CreateInMemoryContext(dbName);
        var paymentRepo = new PaymentRepository(context);
        var userRepo = new UserRepository(context);
        var scopeService = new FacilityScopeService(context, userRepo);

        var configInMemory = new Dictionary<string, string?>
        {
            ["VnPay:TmnCode"] = "TESTTMNCODE",
            ["VnPay:HashSecret"] = "SECRETSECRETSECRETSECRETSECRET12",
            ["VnPay:BaseUrl"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            ["VnPay:ReturnUrl"] = "http://localhost:5000/api/payments/vnpay/return",
            ["Payment:AllowDemoSimulator"] = "true",
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        };
        if (customConfig != null)
        {
            foreach (var kvp in customConfig)
            {
                configInMemory[kvp.Key] = kvp.Value;
            }
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(configInMemory).Build();

        var demoGateway = new DemoGateway(config, NullLogger<DemoGateway>.Instance);
        var vnpayGateway = new VnpayGateway(config, NullLogger<VnpayGateway>.Instance);

        var service = new PaymentService(
            paymentRepo,
            scopeService,
            demoGateway,
            vnpayGateway,
            config,
            NullLogger<PaymentService>.Instance);

        return (service, context, demoGateway, vnpayGateway);
    }

    private async Task<(user Customer, facility Facility, unit_type UnitType, policy_version PolicyVersion, reservation Reservation)> SeedBaselineAsync(
        SelfStorageDbContext context,
        decimal monthlyRate = 1_000_000m,
        decimal? depositOverride = null,
        int rentalMonths = 3,
        DateOnly? startDate = null)
    {
        var customerUser = new user
        {
            id = 10,
            email = "customer10@example.test",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var customerProfile = new customer_profile
        {
            user_id = 10,
            full_name = "Tran Van Khach",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var role = new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" };
        var userRole = new user_role { user_id = 10, role_id = 1, role = role, user = customerUser };
        customerUser.user_roleusers.Add(userRole);

        var facility = new facility
        {
            id = 1,
            code = "FAC-HN",
            name = "Kho Cau Giay",
            address_line = "99 Xuan Thuy",
            city = "Hanoi",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        };

        var unitType = new unit_type
        {
            id = 1,
            code = "M",
            name = "Kho Vua 6m2",
            area_m2 = 6.0m,
            climate_controlled = false,
            is_active = true
        };

        var policyVersion = new policy_version
        {
            id = 1,
            policy_type = "rental_terms",
            version = "2026.1",
            content = "{\"terms\":\"Standard terms\"}",
            valid_from = new DateOnly(2020, 1, 1),
            created_at = DateTimeOffset.UtcNow
        };

        var start = startDate ?? DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var end = start.AddMonths(rentalMonths);
        var deposit = depositOverride ?? monthlyRate;

        var facilityRate = new facility_rate
        {
            id = 1,
            facility_id = 1,
            unit_type_id = 1,
            monthly_rate = monthlyRate,
            deposit_amount = deposit,
            booking_fee = 50_000m,
            valid_from = DateOnly.FromDateTime(DateTime.Today.AddDays(-30))
        };

        var reservation = new reservation
        {
            id = 100,
            reservation_code = "RES-202610-0001",
            customer_id = 10,
            facility_id = 1,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = start,
            end_date = end,
            monthly_rate_snapshot = monthlyRate,
            deposit_snapshot = deposit,
            booking_fee_snapshot = 50_000m,
            discount_snapshot = 0m,
            quoted_total = deposit + monthlyRate + 50_000m,
            status = "pending",
            hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };

        context.roles.Add(role);
        context.users.Add(customerUser);
        context.customer_profiles.Add(customerProfile);
        context.facilities.Add(facility);
        context.unit_types.Add(unitType);
        context.facility_rates.Add(facilityRate);
        context.policy_versions.Add(policyVersion);
        context.reservations.Add(reservation);
        await context.SaveChangesAsync();

        return (customerUser, facility, unitType, policyVersion, reservation);
    }

    [Fact]
    public async Task GetCheckoutQuoteAsync_ValidReservation_ReturnsAccuratePriceBreakdown()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context, monthlyRate: 1_200_000m);

        var quote = await service.GetCheckoutQuoteAsync(customer.id, CustomerRoles, res.id, promotionCode: null);

        Assert.Equal(res.id, quote.ReservationId);
        Assert.Equal(1_200_000m, quote.MonthlyRateSnapshot);
        Assert.Equal(1_200_000m, quote.DepositSnapshot);
        Assert.Equal(50_000m, quote.BookingFeeSnapshot);
        Assert.Equal(0m, quote.DiscountAmount);
        Assert.Equal(0m, quote.TaxAmount); // Demo tax_amount = 0
        Assert.Equal(1_200_000m + 1_200_000m + 50_000m, quote.QuotedTotal);
        Assert.Equal(policy.id, quote.PolicyVersionId);
        Assert.True(quote.IsHoldActive);
    }

    [Fact]
    public async Task GetCheckoutQuoteAsync_DepositMismatch_ThrowsBadRequestException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, _, res) = await SeedBaselineAsync(context, monthlyRate: 1_000_000m, depositOverride: 500_000m);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetCheckoutQuoteAsync(customer.id, CustomerRoles, res.id, promotionCode: null));

        Assert.Contains("Tiền cọc", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCheckoutQuoteAsync_FixedVoucher_DiscountsRentAndFee_NotDeposit()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, _, res) = await SeedBaselineAsync(context, monthlyRate: 1_000_000m);

        var promo = new promotion
        {
            id = 1,
            code = "GIAM200K",
            name = "Giam 200k",
            discount_type = "fixed",
            discount_value = 200_000m,
            valid_from = DateTimeOffset.UtcNow.AddDays(-1),
            valid_to = DateTimeOffset.UtcNow.AddDays(30),
            is_active = true,
            created_at = DateTimeOffset.UtcNow
        };
        context.promotions.Add(promo);
        await context.SaveChangesAsync();

        var quote = await service.GetCheckoutQuoteAsync(customer.id, CustomerRoles, res.id, promotionCode: "GIAM200K");

        Assert.Equal(200_000m, quote.DiscountAmount);
        // QuotedTotal = Deposit (1,000,000) + Rent (1,000,000) + Fee (50,000) - Discount (200,000) = 1,850,000
        Assert.Equal(1_850_000m, quote.QuotedTotal);
    }

    [Fact]
    public async Task GetCheckoutQuoteAsync_FixedVoucherExceedsRentAndFee_CappedAtRentAndFee()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, _, res) = await SeedBaselineAsync(context, monthlyRate: 1_000_000m);

        var promo = new promotion
        {
            id = 2,
            code = "GIAMKHUNG",
            name = "Giam khung 5 trieu",
            discount_type = "fixed",
            discount_value = 5_000_000m,
            valid_from = DateTimeOffset.UtcNow.AddDays(-1),
            valid_to = DateTimeOffset.UtcNow.AddDays(30),
            is_active = true,
            created_at = DateTimeOffset.UtcNow
        };
        context.promotions.Add(promo);
        await context.SaveChangesAsync();

        var quote = await service.GetCheckoutQuoteAsync(customer.id, CustomerRoles, res.id, promotionCode: "GIAMKHUNG");

        // Max discount base is Rent (1,000,000) + Fee (50,000) = 1,050,000. Deposit is NEVER discounted!
        Assert.Equal(1_050_000m, quote.DiscountAmount);
        Assert.Equal(1_000_000m, quote.QuotedTotal); // Exactly deposit remains
    }

    [Fact]
    public async Task GetCheckoutQuoteAsync_PercentageVoucher_CalculatesAccurately()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, _, res) = await SeedBaselineAsync(context, monthlyRate: 2_000_000m);

        var promo = new promotion
        {
            id = 3,
            code = "SALE10",
            name = "Giam 10%",
            discount_type = "percentage",
            discount_value = 10m,
            valid_from = DateTimeOffset.UtcNow.AddDays(-1),
            valid_to = DateTimeOffset.UtcNow.AddDays(30),
            is_active = true,
            created_at = DateTimeOffset.UtcNow
        };
        context.promotions.Add(promo);
        await context.SaveChangesAsync();

        var quote = await service.GetCheckoutQuoteAsync(customer.id, CustomerRoles, res.id, promotionCode: "SALE10");

        // Rent (2,000,000) + Fee (50,000) = 2,050,000 * 10% = 205,000
        Assert.Equal(205_000m, quote.DiscountAmount);
        Assert.Equal(2_000_000m + 2_050_000m - 205_000m, quote.QuotedTotal);
    }

    [Theory]
    [InlineData(2024, 2, 1, 10, 2_900_000, 1_000_000)] // Leap Feb (29 days), 10 free days: 2,900,000 * 10 / 29 = 1,000,000
    [InlineData(2025, 2, 1, 7, 2_800_000, 700_000)]     // Normal Feb (28 days), 7 free days: 2,800,000 * 7 / 28 = 700,000
    [InlineData(2026, 4, 1, 15, 3_000_000, 1_500_000)]   // April (30 days), 15 free days: 3,000,000 * 15 / 30 = 1,500,000
    [InlineData(2026, 1, 1, 10, 3_100_000, 1_000_000)]   // Jan (31 days), 10 free days: 3,100,000 * 10 / 31 = 1,000,000
    public async Task GetCheckoutQuoteAsync_FreeDaysVoucher_CalculatesAccuratelyForVariousMonthLengths(
        int year, int month, int day, int freeDays, decimal monthlyRate, decimal expectedDiscount)
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var startDate = new DateOnly(year, month, day);
        var (customer, _, _, _, res) = await SeedBaselineAsync(context, monthlyRate: monthlyRate, startDate: startDate);

        var promo = new promotion
        {
            id = 4,
            code = $"FREE{freeDays}",
            name = $"Tang {freeDays} ngay",
            discount_type = "free_days",
            discount_value = freeDays,
            valid_from = DateTimeOffset.UtcNow.AddDays(-1),
            valid_to = DateTimeOffset.UtcNow.AddDays(30),
            is_active = true,
            created_at = DateTimeOffset.UtcNow
        };
        context.promotions.Add(promo);
        await context.SaveChangesAsync();

        var quote = await service.GetCheckoutQuoteAsync(customer.id, CustomerRoles, res.id, promotionCode: promo.code);

        Assert.Equal(expectedDiscount, quote.DiscountAmount);
    }

    [Fact]
    public async Task GetCheckoutQuoteAsync_FreeDaysExceedsDaysInFirstMonth_ThrowsBadRequestException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        // April has 30 days
        var startDate = new DateOnly(2026, 4, 1);
        var (customer, _, _, _, res) = await SeedBaselineAsync(context, monthlyRate: 1_000_000m, startDate: startDate);

        var promo = new promotion
        {
            id = 5,
            code = "FREE35",
            name = "Tang 35 ngay",
            discount_type = "free_days",
            discount_value = 35m, // Exceeds 30 days in April
            valid_from = DateTimeOffset.UtcNow.AddDays(-1),
            valid_to = DateTimeOffset.UtcNow.AddDays(30),
            is_active = true,
            created_at = DateTimeOffset.UtcNow
        };
        context.promotions.Add(promo);
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetCheckoutQuoteAsync(customer.id, CustomerRoles, res.id, promotionCode: "FREE35"));

        Assert.Contains("vượt quá", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCheckoutQuoteAsync_VoucherMinimumMonthsViolation_ThrowsBadRequestException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, _, res) = await SeedBaselineAsync(context, rentalMonths: 1); // 1 month reservation

        var promo = new promotion
        {
            id = 6,
            code = "MIN3M",
            name = "Yeu cau 3 thang",
            discount_type = "fixed",
            discount_value = 100_000m,
            valid_from = DateTimeOffset.UtcNow.AddDays(-1),
            valid_to = DateTimeOffset.UtcNow.AddDays(30),
            is_active = true,
            created_at = DateTimeOffset.UtcNow
        };
        promo.promotion_rules.Add(new promotion_rule
        {
            id = 1,
            promotion_id = 6,
            rule_type = "minimum_months",
            _operator = ">=",
            rule_value = "3"
        });
        context.promotions.Add(promo);
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetCheckoutQuoteAsync(customer.id, CustomerRoles, res.id, promotionCode: "MIN3M"));

        Assert.Contains("thời hạn thuê", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCheckoutQuoteAsync_VoucherFacilityMismatch_ThrowsBadRequestException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, _, res) = await SeedBaselineAsync(context); // Facility 1

        var promo = new promotion
        {
            id = 7,
            code = "FACILITY2_ONLY",
            name = "Chi ap dung co so 2",
            discount_type = "fixed",
            discount_value = 100_000m,
            valid_from = DateTimeOffset.UtcNow.AddDays(-1),
            valid_to = DateTimeOffset.UtcNow.AddDays(30),
            is_active = true,
            created_at = DateTimeOffset.UtcNow
        };
        promo.promotion_rules.Add(new promotion_rule
        {
            id = 2,
            promotion_id = 7,
            rule_type = "facility",
            _operator = "==",
            rule_value = "999"
        });
        context.promotions.Add(promo);
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetCheckoutQuoteAsync(customer.id, CustomerRoles, res.id, promotionCode: "FACILITY2_ONLY"));

        Assert.Contains("cơ sở", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckoutAsync_IdempotencyKeyReplay_ReturnsSamePaymentAttempt()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        var request = new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "demo"
        };
        var idempotencyKey = "IDEM-TEST-12345";

        var firstResp = await service.CheckoutAsync(customer.id, CustomerRoles, request, idempotencyKey, "127.0.0.1");
        Assert.NotNull(firstResp);
        Assert.Equal("pending", firstResp.Status);

        // Replay with identical idempotency key
        var secondResp = await service.CheckoutAsync(customer.id, CustomerRoles, request, idempotencyKey, "127.0.0.1");
        Assert.Equal(firstResp.PaymentId, secondResp.PaymentId);
        Assert.Equal(firstResp.Amount, secondResp.Amount);
    }

    [Fact]
    public async Task CheckoutAsync_ConcurrentAttemptWithDifferentKey_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        var request = new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "demo"
        };

        var firstResp = await service.CheckoutAsync(customer.id, CustomerRoles, request, "IDEM-KEY-1", "127.0.0.1");
        Assert.NotNull(firstResp);

        // Attempt with different idempotency key while previous attempt is still pending
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CheckoutAsync(customer.id, CustomerRoles, request, "IDEM-KEY-2", "127.0.0.1"));

        Assert.Contains("đang chờ xử lý", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DemoPaymentComplete_Success_TransitionsToConfirmedAndScheduled()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        var checkoutResp = await service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "demo"
        }, "IDEM-COMPLETE-1", "127.0.0.1");

        var completeResp = await service.ProcessDemoCompleteAsync(
            customer.id,
            CustomerRoles,
            checkoutResp.PaymentId,
            new DemoPaymentCompleteRequest { IsSuccess = true });

        Assert.Equal("succeeded", completeResp.Status);
        Assert.NotNull(completeResp.PaidAt);

        // Verify Reservation status in DB
        var updatedRes = await context.reservations.FindAsync(res.id);
        Assert.NotNull(updatedRes);
        Assert.Equal("confirmed", updatedRes.status);
        Assert.NotNull(updatedRes.confirmed_at);

        // Verify Invoice status in DB
        var invoice = await context.invoices.FindAsync(checkoutResp.InvoiceId);
        Assert.NotNull(invoice);
        Assert.Equal("paid", invoice.status);
        Assert.Equal(invoice.total_amount, invoice.paid_amount);

        // Verify Rental Agreement created
        var agreement = await context.rental_agreements.FirstOrDefaultAsync(a => a.reservation_id == res.id);
        Assert.NotNull(agreement);
        Assert.Equal("scheduled", agreement.status); // BR-AGR-01: Function 4 creates scheduled agreement
        Assert.Null(agreement.signed_at);            // Not signed in Function 4
        Assert.Equal(res.deposit_snapshot, agreement.deposit_balance);
    }

    [Fact]
    public async Task DemoPaymentComplete_ExpiredHold_FlagsReconciliationAndCreatesRefund()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        var checkoutResp = await service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "demo"
        }, "IDEM-LATE-1", "127.0.0.1");

        // Simulate hold expiring before callback
        res.hold_until = DateTimeOffset.UtcNow.AddMinutes(-5);
        res.status = "expired";
        await context.SaveChangesAsync();

        var completeResp = await service.ProcessDemoCompleteAsync(
            customer.id,
            CustomerRoles,
            checkoutResp.PaymentId,
            new DemoPaymentCompleteRequest { IsSuccess = true });

        Assert.Equal("succeeded", completeResp.Status);
        Assert.True(completeResp.ReconciliationRequired);

        // Verify reservation was NOT confirmed
        var updatedRes = await context.reservations.FindAsync(res.id);
        Assert.NotNull(updatedRes);
        Assert.NotEqual("confirmed", updatedRes.status);

        // Verify rental agreement was NOT created
        var agreement = await context.rental_agreements.FirstOrDefaultAsync(a => a.reservation_id == res.id);
        Assert.Null(agreement);

        // Verify refund record created
        var refund = await context.refunds.FirstOrDefaultAsync(r => r.payment_id == checkoutResp.PaymentId);
        Assert.NotNull(refund);
        Assert.Equal("requested", refund.status);
    }

    [Fact]
    public async Task DemoPaymentComplete_InProduction_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName, new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Payment:AllowDemoSimulator"] = "true"
        });
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        var checkoutResp = await service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "demo"
        }, "IDEM-PROD-1", "127.0.0.1");

        await Assert.ThrowsAsync<ForbiddenException>(() => service.ProcessDemoCompleteAsync(
            customer.id,
            CustomerRoles,
            checkoutResp.PaymentId,
            new DemoPaymentCompleteRequest { IsSuccess = true }));
    }

    [Fact]
    public async Task DemoPaymentComplete_DisabledInConfig_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName, new Dictionary<string, string?>
        {
            ["Payment:AllowDemoSimulator"] = "false"
        });
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        var checkoutResp = await service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "demo"
        }, "IDEM-DIS-1", "127.0.0.1");

        await Assert.ThrowsAsync<ForbiddenException>(() => service.ProcessDemoCompleteAsync(
            customer.id,
            CustomerRoles,
            checkoutResp.PaymentId,
            new DemoPaymentCompleteRequest { IsSuccess = true }));
    }

    [Fact]
    public async Task DemoPaymentComplete_ForNonDemoProvider_ThrowsBadRequestException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        var checkoutResp = await service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "vnpay"
        }, "IDEM-VNPDEMO-1", "127.0.0.1");

        await Assert.ThrowsAsync<BadRequestException>(() => service.ProcessDemoCompleteAsync(
            customer.id,
            CustomerRoles,
            checkoutResp.PaymentId,
            new DemoPaymentCompleteRequest { IsSuccess = true }));
    }

    [Fact]
    public async Task ProcessVnpayIpn_InvalidSignature_ReturnsCode97()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, _, _, _) = SetupService(dbName);

        var queryParams = new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = "12345",
            ["vnp_Amount"] = "100000000",
            ["vnp_SecureHash"] = "invalid_signature"
        };

        var result = await service.ProcessVnpayIpnAsync(queryParams);

        Assert.Equal("97", result.IpnResponseCode);
        Assert.Equal("Invalid Signature", result.IpnResponseMessage);
    }

    [Fact]
    public async Task ProcessVnpayIpn_InvalidTmnCode_ReturnsCode97()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        var checkoutResp = await service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "vnpay"
        }, "IDEM-TMN-1", "127.0.0.1");

        var cbParams = BuildVnpayCallbackParams(checkoutResp.PaymentId, checkoutResp.Amount, "SECRETSECRETSECRETSECRETSECRET12", tmnCode: "WRONGTMN");
        var result = await service.ProcessVnpayIpnAsync(cbParams);

        Assert.Equal("97", result.IpnResponseCode);
        Assert.Equal("Invalid Signature", result.IpnResponseMessage);
    }

    [Fact]
    public async Task ProcessVnpayIpn_InvalidCurrency_ReturnsCode04()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        var checkoutResp = await service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "vnpay"
        }, "IDEM-CURR-1", "127.0.0.1");

        var cbParams = BuildVnpayCallbackParams(checkoutResp.PaymentId, checkoutResp.Amount, "SECRETSECRETSECRETSECRETSECRET12", currency: "USD");
        var result = await service.ProcessVnpayIpnAsync(cbParams);

        Assert.Equal("04", result.IpnResponseCode);
        Assert.Equal("Invalid Amount", result.IpnResponseMessage);
    }

    [Fact]
    public async Task ProcessVnpayIpn_AmountMismatch_ReturnsCode04()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        var checkoutResp = await service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "vnpay"
        }, "IDEM-AMT-1", "127.0.0.1");

        // Callback with half the amount
        var cbParams = BuildVnpayCallbackParams(checkoutResp.PaymentId, checkoutResp.Amount / 2, "SECRETSECRETSECRETSECRETSECRET12");
        var result = await service.ProcessVnpayIpnAsync(cbParams);

        Assert.Equal("04", result.IpnResponseCode);
        Assert.Equal("Invalid Amount", result.IpnResponseMessage);

        // Verify payment is not confirmed
        var payment = await context.payments.FindAsync(checkoutResp.PaymentId);
        Assert.NotNull(payment);
        Assert.Equal("pending", payment.status);
    }

    [Fact]
    public async Task ProcessVnpayIpn_AlreadySucceeded_ReturnsCode02Idempotent()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        var checkoutResp = await service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "vnpay"
        }, "IDEM-SUCC-1", "127.0.0.1");

        var cbParams = BuildVnpayCallbackParams(checkoutResp.PaymentId, checkoutResp.Amount, "SECRETSECRETSECRETSECRETSECRET12");

        // First callback succeeds
        var firstResult = await service.ProcessVnpayIpnAsync(cbParams);
        Assert.True(firstResult.IsSuccess);

        // Second duplicate callback returns 02
        var secondResult = await service.ProcessVnpayIpnAsync(cbParams);
        Assert.Equal("02", secondResult.IpnResponseCode);
        Assert.Equal("Order already confirmed", secondResult.IpnResponseMessage);
    }

    [Fact]
    public async Task Checkout_SameKeyDifferentPayload_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        await service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "demo"
        }, "SAME-IDEM-KEY", "127.0.0.1");

        // Second call with different method ("vnpay" instead of "demo")
        await Assert.ThrowsAsync<ConflictException>(() => service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "vnpay"
        }, "SAME-IDEM-KEY", "127.0.0.1"));
    }

    [Fact]
    public async Task GetCheckoutQuote_FreeDaysDecimal_ThrowsBadRequestException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, _, res) = await SeedBaselineAsync(context);

        var promo = new promotion
        {
            id = 99,
            code = "FREE1POINT5",
            name = "Free 1.5 Days",
            discount_type = "free_days",
            discount_value = 1.5m, // Fractional days invalid!
            is_active = true,
            valid_from = DateTimeOffset.UtcNow.AddDays(-1),
            valid_to = DateTimeOffset.UtcNow.AddDays(10),
            created_at = DateTimeOffset.UtcNow
        };
        context.promotions.Add(promo);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<BadRequestException>(() => service.GetCheckoutQuoteAsync(
            customer.id,
            CustomerRoles,
            res.id,
            "FREE1POINT5"));
    }

    [Fact]
    public async Task ProcessPayment_SecondPaymentOnAlreadyPaidInvoice_FlagsReconciliationAndCreatesRefund()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, res) = await SeedBaselineAsync(context);

        // Attempt 1
        var checkout1 = await service.CheckoutAsync(customer.id, CustomerRoles, new CheckoutRequest
        {
            ReservationId = res.id,
            AcceptedPolicyVersionId = policy.id,
            PaymentMethod = "vnpay"
        }, "IDEM-ATTEMPT-1", "127.0.0.1");

        // Attempt 1 succeeds and pays invoice
        var cbParams1 = BuildVnpayCallbackParams(checkout1.PaymentId, checkout1.Amount, "SECRETSECRETSECRETSECRETSECRET12");
        var res1 = await service.ProcessVnpayIpnAsync(cbParams1);
        Assert.True(res1.IsSuccess);

        // Attempt 2 was created before or concurrently
        var p2 = new payment
        {
            id = 2002,
            customer_id = customer.id,
            target_invoice_id = checkout1.InvoiceId,
            amount = checkout1.Amount,
            currency = "VND",
            method = "vnpay",
            provider = "vnpay",
            idempotency_key = "IDEM-ATTEMPT-2",
            status = "pending",
            metadata = "{}",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.payments.Add(p2);
        await context.SaveChangesAsync();

        // Attempt 2 callback arrives after invoice is already paid
        var cbParams2 = BuildVnpayCallbackParams(p2.id, p2.amount, "SECRETSECRETSECRETSECRETSECRET12");
        var res2 = await service.ProcessVnpayIpnAsync(cbParams2);

        // Should finalize attempt 2 as succeeded + reconciliation_required, creating refund
        var payment2Refreshed = await context.payments.FindAsync(p2.id);
        Assert.NotNull(payment2Refreshed);
        Assert.Equal("succeeded", payment2Refreshed.status);
        Assert.Contains("reconciliation_required", payment2Refreshed.metadata);

        var paymentDetail = await service.GetPaymentByIdAsync(customer.id, CustomerRoles, p2.id);
        Assert.True(paymentDetail.ReconciliationRequired);

        var refund = await context.refunds.FirstOrDefaultAsync(r => r.payment_id == p2.id);
        Assert.NotNull(refund);
        Assert.Equal("requested", refund.status);
        Assert.Equal(p2.amount, refund.amount);
    }

    [Fact]
    public async Task ReviewRefund_ApprovesRefund_CreatesApprovalRecord()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, facility, _, _, reservation) = await SeedBaselineAsync(context);

        var staffUser = new user
        {
            id = 999,
            email = "manager@example.test",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var mgrRole = new role { id = 3, code = RoleConstants.FacilityManager, display_name = "Facility Manager" };
        staffUser.user_roleusers.Add(new user_role { user_id = 999, role_id = 3, role = mgrRole, user = staffUser });
        context.users.Add(staffUser);
        context.employee_profiles.Add(new employee_profile
        {
            user_id = staffUser.id,
            employee_code = "EMP-001",
            full_name = "Manager Name",
            employment_status = "active",
            hire_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });
        context.staff_facility_assignments.Add(new staff_facility_assignment
        {
            employee_id = staffUser.id,
            facility_id = facility.id,
            assignment_role = RoleConstants.FacilityManager,
            starts_at = DateTimeOffset.UtcNow.AddDays(-10)
        });

        var invoice = new invoice
        {
            id = 2001,
            invoice_no = "INV-TEST-001",
            customer_id = customer.id,
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
        context.invoices.Add(invoice);

        var p = new payment
        {
            id = 3001,
            customer_id = customer.id,
            target_invoice_id = invoice.id,
            amount = 1_000_000m,
            currency = "VND",
            method = "vnpay",
            provider = "vnpay",
            idempotency_key = "IDEM-REFUND-1",
            status = "succeeded",
            metadata = "{\"reconciliation_required\":true}",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.payments.Add(p);

        var refund = new refund
        {
            id = 5001,
            payment_id = 3001,
            amount = 1_000_000m,
            currency = "VND",
            reason = "Late payment reconciliation",
            provider = "vnpay",
            idempotency_key = "REF-IDEM-5001",
            status = "requested",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.refunds.Add(refund);
        await context.SaveChangesAsync();

        var managerRoles = new[] { RoleConstants.FacilityManager };
        var reviewResp = await service.ReviewRefundAsync(staffUser.id, managerRoles, 5001, new ReviewRefundRequest
        {
            Decision = "approved",
            Reason = "Late payment confirmed, refunding customer."
        });

        Assert.Equal("approved", reviewResp.Status);
        Assert.Equal("approved", reviewResp.Decision);
        Assert.Equal(staffUser.id, reviewResp.DecidedBy);

        var approvalInDb = await context.refund_approvals.FirstOrDefaultAsync(a => a.refund_id == 5001);
        Assert.NotNull(approvalInDb);
        Assert.Equal("approved", approvalInDb.decision);
        Assert.Equal(staffUser.id, approvalInDb.decided_by);
    }

    [Fact]
    public async Task Refund_FacilityScope_StaffOnlySeesAndReviewsAssignedFacility()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, facA, _, _, resA) = await SeedBaselineAsync(context);

        // Facility B
        var facB = new facility
        {
            id = 2,
            code = "FAC-HCM",
            name = "Kho Quan 7",
            address_line = "12 Nguyen Huu Tho",
            city = "Ho Chi Minh City",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        };
        context.facilities.Add(facB);

        var resB = new reservation
        {
            id = 200,
            reservation_code = "RES-202610-0002",
            customer_id = customer.id,
            facility_id = 2,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = DateOnly.FromDateTime(DateTime.Today.AddDays(2)),
            end_date = DateOnly.FromDateTime(DateTime.Today.AddDays(2)).AddMonths(3),
            monthly_rate_snapshot = 1_000_000m,
            deposit_snapshot = 1_000_000m,
            booking_fee_snapshot = 50_000m,
            discount_snapshot = 0m,
            quoted_total = 2_050_000m,
            status = "confirmed",
            hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.reservations.Add(resB);

        // Invoices and Payments for Fac A and Fac B
        var invA = new invoice
        {
            id = 1001,
            invoice_no = "INV-FACA-001",
            customer_id = customer.id,
            reservation_id = resA.id,
            issue_date = DateOnly.FromDateTime(DateTime.Today),
            due_date = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            currency = "VND",
            subtotal_amount = 1_000_000m,
            total_amount = 1_000_000m,
            status = "paid",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var invB = new invoice
        {
            id = 1002,
            invoice_no = "INV-FACB-001",
            customer_id = customer.id,
            reservation_id = resB.id,
            issue_date = DateOnly.FromDateTime(DateTime.Today),
            due_date = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            currency = "VND",
            subtotal_amount = 1_000_000m,
            total_amount = 1_000_000m,
            status = "paid",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.invoices.AddRange(invA, invB);

        var pA = new payment
        {
            id = 2001,
            customer_id = customer.id,
            target_invoice_id = invA.id,
            amount = 1_000_000m,
            currency = "VND",
            method = "vnpay",
            provider = "vnpay",
            idempotency_key = "IDEM-REF-A",
            status = "succeeded",
            metadata = "{\"reconciliation_required\":true}",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var pB = new payment
        {
            id = 2002,
            customer_id = customer.id,
            target_invoice_id = invB.id,
            amount = 1_000_000m,
            currency = "VND",
            method = "vnpay",
            provider = "vnpay",
            idempotency_key = "IDEM-REF-B",
            status = "succeeded",
            metadata = "{\"reconciliation_required\":true}",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.payments.AddRange(pA, pB);

        var refA = new refund
        {
            id = 3001,
            payment_id = pA.id,
            amount = 1_000_000m,
            currency = "VND",
            reason = "Refund for Fac A",
            provider = "vnpay",
            idempotency_key = "REF-A-3001",
            status = "requested",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var refB = new refund
        {
            id = 3002,
            payment_id = pB.id,
            amount = 1_000_000m,
            currency = "VND",
            reason = "Refund for Fac B",
            provider = "vnpay",
            idempotency_key = "REF-B-3002",
            status = "requested",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.refunds.AddRange(refA, refB);

        // Staff assigned ONLY to Facility A
        var staffA = new user
        {
            id = 555,
            email = "staff.a@example.test",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var staffRole = new role { id = 2, code = RoleConstants.FacilityStaff, display_name = "Facility Staff" };
        staffA.user_roleusers.Add(new user_role { user_id = 555, role_id = 2, role = staffRole, user = staffA });
        context.users.Add(staffA);
        context.employee_profiles.Add(new employee_profile
        {
            user_id = 555,
            employee_code = "STAFF-A",
            full_name = "Staff A",
            employment_status = "active",
            hire_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });
        context.staff_facility_assignments.Add(new staff_facility_assignment
        {
            employee_id = 555,
            facility_id = facA.id,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = DateTimeOffset.UtcNow.AddDays(-10)
        });

        // Operations Manager (system-wide)
        var opsUser = new user
        {
            id = 777,
            email = "ops@example.test",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var opsRole = new role { id = 4, code = RoleConstants.BusinessOperationsManager, display_name = "Operations Manager" };
        opsUser.user_roleusers.Add(new user_role { user_id = 777, role_id = 4, role = opsRole, user = opsUser });
        context.users.Add(opsUser);

        await context.SaveChangesAsync();

        var staffRoles = new[] { RoleConstants.FacilityStaff };
        var opsRoles = new[] { RoleConstants.BusinessOperationsManager };

        // Staff A only sees Refund A in DB query
        var staffRefunds = await service.GetRefundsAsync(staffA.id, staffRoles, null);
        Assert.Single(staffRefunds);
        Assert.Equal(refA.id, staffRefunds[0].Id);

        // Staff A trying to review Refund B throws ForbiddenException (403)
        var forbiddenEx = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ReviewRefundAsync(staffA.id, staffRoles, refB.id, new ReviewRefundRequest { Decision = "approved" }));
        Assert.Contains("không có quyền", forbiddenEx.Message);

        // Verify DB: Refund B remains requested with NO approval record
        var refBInDb = await context.refunds.Include(r => r.refund_approval).FirstAsync(r => r.id == refB.id);
        Assert.Equal("requested", refBInDb.status);
        Assert.Null(refBInDb.refund_approval);

        // Staff A can review Refund A (Facility A)
        var approvedA = await service.ReviewRefundAsync(staffA.id, staffRoles, refA.id, new ReviewRefundRequest { Decision = "approved", Reason = "Staff approved" });
        Assert.Equal("approved", approvedA.Status);

        // Operations Manager sees both refunds and can review Refund B
        var opsRefunds = await service.GetRefundsAsync(opsUser.id, opsRoles, null);
        Assert.Equal(2, opsRefunds.Count);

        var approvedB = await service.ReviewRefundAsync(opsUser.id, opsRoles, refB.id, new ReviewRefundRequest { Decision = "approved", Reason = "Ops approved" });
        Assert.Equal("approved", approvedB.Status);
    }

    [Fact]
    public async Task ReviewRefund_WhenAdminWithoutEmployeeProfile_SucceedsAndCreatesApproval()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, _, reservation) = await SeedBaselineAsync(context);

        // Admin User 6 with NO employee profile
        var adminUser = new user
        {
            id = 6,
            email = "admin@example.test",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var adminRole = new role { id = 5, code = RoleConstants.SystemAdministrator, display_name = "Admin" };
        adminUser.user_roleusers.Add(new user_role { user_id = 6, role_id = 5, role = adminRole, user = adminUser });
        context.users.Add(adminUser);

        var invoice = new invoice
        {
            id = 4001,
            invoice_no = "INV-ADMIN-001",
            customer_id = customer.id,
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
        context.invoices.Add(invoice);

        var payment = new payment
        {
            id = 5001,
            customer_id = customer.id,
            target_invoice_id = invoice.id,
            amount = 1_000_000m,
            currency = "VND",
            method = "vnpay",
            provider = "vnpay",
            idempotency_key = "IDEM-ADMIN-REF",
            status = "succeeded",
            metadata = "{\"reconciliation_required\":true}",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.payments.Add(payment);

        var refund = new refund
        {
            id = 6001,
            payment_id = payment.id,
            amount = 1_000_000m,
            currency = "VND",
            reason = "Admin refund test",
            provider = "vnpay",
            idempotency_key = "REF-ADMIN-6001",
            status = "requested",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.refunds.Add(refund);
        await context.SaveChangesAsync();

        var adminRoles = new[] { RoleConstants.SystemAdministrator };
        var result = await service.ReviewRefundAsync(adminUser.id, adminRoles, refund.id, new ReviewRefundRequest
        {
            Decision = "approved",
            Reason = "Admin approved directly"
        });

        Assert.Equal("approved", result.Status);
        Assert.Equal(adminUser.id, result.DecidedBy);

        var approval = await context.refund_approvals.FirstOrDefaultAsync(a => a.refund_id == refund.id);
        Assert.NotNull(approval);
        Assert.Equal(adminUser.id, approval.decided_by);
    }

    [Fact]
    public async Task ReviewRefund_WhenNotFound_ThrowsNotFoundException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, _, _, _) = SetupService(dbName);

        var adminRoles = new[] { RoleConstants.SystemAdministrator };
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ReviewRefundAsync(6, adminRoles, 999999, new ReviewRefundRequest { Decision = "approved" }));
    }

    [Fact]
    public async Task ReviewRefund_WhenAlreadyDecided_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, _, reservation) = await SeedBaselineAsync(context);

        var invoice = new invoice
        {
            id = 7001,
            invoice_no = "INV-DECIDED-001",
            customer_id = customer.id,
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
        context.invoices.Add(invoice);

        var payment = new payment
        {
            id = 7002,
            customer_id = customer.id,
            target_invoice_id = invoice.id,
            amount = 1_000_000m,
            currency = "VND",
            method = "vnpay",
            provider = "vnpay",
            idempotency_key = "IDEM-DECIDED-REF",
            status = "succeeded",
            metadata = "{\"reconciliation_required\":true}",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.payments.Add(payment);

        var refund = new refund
        {
            id = 7003,
            payment_id = payment.id,
            amount = 1_000_000m,
            currency = "VND",
            reason = "Already decided test",
            provider = "vnpay",
            idempotency_key = "REF-DECIDED-7003",
            status = "approved", // Already decided
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.refunds.Add(refund);
        await context.SaveChangesAsync();

        var adminRoles = new[] { RoleConstants.SystemAdministrator };
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.ReviewRefundAsync(6, adminRoles, refund.id, new ReviewRefundRequest { Decision = "approved" }));
    }

    [Fact]
    public async Task Checkout_IdempotencyReplay_WhenVoucherLimitReached_SucceedsWithoutRevalidatingVoucher()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, reservation) = await SeedBaselineAsync(context);

        // Voucher with usage_limit = 1
        var promo = new promotion
        {
            id = 1,
            code = "LIMIT1VOUCHER",
            name = "Giảm 200k",
            discount_type = "fixed",
            discount_value = 200_000m,
            usage_limit = 1,
            per_customer_limit = 1,
            is_active = true,
            valid_from = DateTimeOffset.UtcNow.AddDays(-10),
            valid_to = DateTimeOffset.UtcNow.AddDays(10),
            created_at = DateTimeOffset.UtcNow
        };
        context.promotions.Add(promo);
        await context.SaveChangesAsync();

        var customerRoles = new[] { RoleConstants.StorageCustomer };
        var key = "IDEM-VOUCHER-LIMIT-1";
        var req = new CheckoutRequest
        {
            ReservationId = reservation.id,
            PaymentMethod = "demo",
            PromotionCode = "LIMIT1VOUCHER",
            AcceptedPolicyVersionId = policy.id
        };

        // First checkout succeeds, consuming the 1 usage limit
        var resp1 = await service.CheckoutAsync(customer.id, customerRoles, req, key, "127.0.0.1");
        Assert.NotNull(resp1);
        Assert.Equal(1_850_000m, resp1.Amount); // 2.05M - 200k

        // Idempotent replay with same key and same payload: must succeed without failing usage_limit check!
        var resp2 = await service.CheckoutAsync(customer.id, customerRoles, req, key, "127.0.0.1");
        Assert.NotNull(resp2);
        Assert.Equal(resp1.PaymentId, resp2.PaymentId);
        Assert.Equal(resp1.Amount, resp2.Amount);
    }

    [Fact]
    public async Task Checkout_IdempotencyReplay_WhenPayloadMismatched_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, reservation) = await SeedBaselineAsync(context);

        var customerRoles = new[] { RoleConstants.StorageCustomer };
        var key = "IDEM-PAYLOAD-MISMATCH";
        var req1 = new CheckoutRequest
        {
            ReservationId = reservation.id,
            PaymentMethod = "demo",
            AcceptedPolicyVersionId = policy.id
        };

        var resp1 = await service.CheckoutAsync(customer.id, customerRoles, req1, key, "127.0.0.1");
        Assert.NotNull(resp1);

        // Send same idempotency key but with different PromotionCode
        var reqMismatch = new CheckoutRequest
        {
            ReservationId = reservation.id,
            PaymentMethod = "demo",
            PromotionCode = "SOMEVOUCHER",
            AcceptedPolicyVersionId = policy.id
        };

        var conflictEx = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CheckoutAsync(customer.id, customerRoles, reqMismatch, key, "127.0.0.1"));
        Assert.Contains("Idempotency-Key", conflictEx.Message);
    }

    [Fact]
    public async Task Checkout_RetryWithoutVoucher_ReleasesExistingReservedRedemption()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, reservation) = await SeedBaselineAsync(context);

        var promo = new promotion
        {
            id = 1,
            code = "TESTVOUCHER",
            name = "Giảm 200k",
            discount_type = "fixed",
            discount_value = 200_000m,
            usage_limit = 10,
            per_customer_limit = 5,
            is_active = true,
            valid_from = DateTimeOffset.UtcNow.AddDays(-10),
            valid_to = DateTimeOffset.UtcNow.AddDays(10),
            created_at = DateTimeOffset.UtcNow
        };
        context.promotions.Add(promo);
        await context.SaveChangesAsync();

        var customerRoles = new[] { RoleConstants.StorageCustomer };

        // Attempt 1 with voucher
        var req1 = new CheckoutRequest
        {
            ReservationId = reservation.id,
            PaymentMethod = "demo",
            PromotionCode = "TESTVOUCHER",
            AcceptedPolicyVersionId = policy.id
        };
        var resp1 = await service.CheckoutAsync(customer.id, customerRoles, req1, "KEY-ATTEMPT-1", "127.0.0.1");
        Assert.Equal(1_850_000m, resp1.Amount);

        // Attempt 1 fails/cancels (e.g., user cancels on portal), allowing retry
        var repo = new PaymentRepository(context);
        await repo.ExecuteFinalizeFailedPaymentTransactionAsync(new FinalizeFailedPaymentParams
        {
            PaymentId = resp1.PaymentId,
            Provider = "demo",
            FailureReason = "USER_CANCELLED",
            FailedAt = DateTimeOffset.UtcNow,
            ExternalEventId = "EVT-FAIL-1"
        });

        // Attempt 2 WITHOUT voucher (different key)
        var req2 = new CheckoutRequest
        {
            ReservationId = reservation.id,
            PaymentMethod = "demo",
            PromotionCode = null,
            AcceptedPolicyVersionId = policy.id
        };
        var resp2 = await service.CheckoutAsync(customer.id, customerRoles, req2, "KEY-ATTEMPT-2", "127.0.0.1");
        Assert.Equal(2_050_000m, resp2.Amount);

        // Verify previous redemption is RELEASED and reservation.discount_snapshot is 0
        var redUpdated = await context.promotion_redemptions.FirstOrDefaultAsync(pr => pr.reservation_id == reservation.id);
        Assert.NotNull(redUpdated);
        Assert.Equal("released", redUpdated.status);

        var resInDb = await context.reservations.FirstAsync(r => r.id == reservation.id);
        Assert.Equal(0m, resInDb.discount_snapshot);
    }

    [Fact]
    public async Task Checkout_RetryWithDifferentQuote_VoidsOldInvoice_AndLatePaymentRoutesToReconciliation()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, reservation) = await SeedBaselineAsync(context);

        var promo = new promotion
        {
            id = 1,
            code = "TESTVOUCHER",
            name = "Giảm 200k",
            discount_type = "fixed",
            discount_value = 200_000m,
            usage_limit = 10,
            per_customer_limit = 5,
            is_active = true,
            valid_from = DateTimeOffset.UtcNow.AddDays(-10),
            valid_to = DateTimeOffset.UtcNow.AddDays(10),
            created_at = DateTimeOffset.UtcNow
        };
        context.promotions.Add(promo);
        await context.SaveChangesAsync();

        var customerRoles = new[] { RoleConstants.StorageCustomer };

        // Attempt 1 without voucher (total = 2.05M)
        var req1 = new CheckoutRequest
        {
            ReservationId = reservation.id,
            PaymentMethod = "demo",
            AcceptedPolicyVersionId = policy.id
        };
        var resp1 = await service.CheckoutAsync(customer.id, customerRoles, req1, "KEY-A1", "127.0.0.1");
        var invoice1Id = resp1.InvoiceId;

        // Attempt 1 fails/cancels so user retries with a voucher
        var repo = new PaymentRepository(context);
        await repo.ExecuteFinalizeFailedPaymentTransactionAsync(new FinalizeFailedPaymentParams
        {
            PaymentId = resp1.PaymentId,
            Provider = "demo",
            FailureReason = "USER_RETRY",
            FailedAt = DateTimeOffset.UtcNow,
            ExternalEventId = "EVT-RETRY-1"
        });

        // Attempt 2 with voucher (total = 1.85M)
        var req2 = new CheckoutRequest
        {
            ReservationId = reservation.id,
            PaymentMethod = "demo",
            PromotionCode = "TESTVOUCHER",
            AcceptedPolicyVersionId = policy.id
        };
        var resp2 = await service.CheckoutAsync(customer.id, customerRoles, req2, "KEY-A2", "127.0.0.1");
        var invoice2Id = resp2.InvoiceId;

        // Verify: invoice1 is VOIDED, and invoice2 is a brand new invoice with open status!
        Assert.NotEqual(invoice1Id, invoice2Id);
        var inv1 = await context.invoices.FirstAsync(i => i.id == invoice1Id);
        var inv2 = await context.invoices.FirstAsync(i => i.id == invoice2Id);
        Assert.Equal("voided", inv1.status);
        Assert.NotNull(inv1.voided_at);
        Assert.Equal("open", inv2.status);

        // If Attempt 1 finishes now (payment arrives pointing to voided Invoice 1):
        repo = new PaymentRepository(context);
        var finParams = new FinalizePaymentParams
        {
            PaymentId = resp1.PaymentId,
            Provider = "demo",
            ProviderTransactionId = "TX-OLD",
            Amount = 2_050_000m,
            ExternalEventId = "EVT-OLD-1",
            PaidAt = DateTimeOffset.UtcNow
        };
        var finResult = await repo.ExecuteFinalizePaymentTransactionAsync(finParams);

        // Must route to reconciliation! No agreement created!
        Assert.True(finResult.ReconciliationRequired);
        Assert.Null(finResult.Agreement);

        var p1InDb = await context.payments.FirstAsync(p => p.id == resp1.PaymentId);
        Assert.Equal("succeeded", p1InDb.status);
        Assert.Equal("INVOICE_VOIDED", p1InDb.failure_reason);

        // A refund record was created for staff review
        var refundInDb = await context.refunds.FirstOrDefaultAsync(r => r.payment_id == resp1.PaymentId);
        Assert.NotNull(refundInDb);
        Assert.Equal("requested", refundInDb.status);
        Assert.Equal(2_050_000m, refundInDb.amount);
    }

    [Fact]
    public async Task FinalizePayment_WhenInvoicePartiallyPaid_PersistsReconciliationAndReflectsInSubsequentGet()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, reservation) = await SeedBaselineAsync(context);

        var customerRoles = new[] { RoleConstants.StorageCustomer };
        var req = new CheckoutRequest
        {
            ReservationId = reservation.id,
            PaymentMethod = "demo",
            AcceptedPolicyVersionId = policy.id
        };
        var resp = await service.CheckoutAsync(customer.id, customerRoles, req, "KEY-PARTIAL", "127.0.0.1");

        // Simulate payment that only pays 1.0M instead of full 2.05M
        var repo = new PaymentRepository(context);
        var finParams = new FinalizePaymentParams
        {
            PaymentId = resp.PaymentId,
            Provider = "demo",
            ProviderTransactionId = "TX-PARTIAL",
            Amount = 1_000_000m, // Only 1.0M
            ExternalEventId = "EVT-PARTIAL-1",
            PaidAt = DateTimeOffset.UtcNow
        };
        var finResult = await repo.ExecuteFinalizePaymentTransactionAsync(finParams);

        Assert.True(finResult.ReconciliationRequired);
        Assert.NotNull(finResult.ReconciliationReason);
        Assert.Null(finResult.Agreement);

        var resInDb = await context.reservations.FirstAsync(r => r.id == reservation.id);
        Assert.NotEqual("confirmed", resInDb.status);

        var noAgreement = await context.rental_agreements.AnyAsync(a => a.reservation_id == reservation.id);
        Assert.False(noAgreement);

        // Verify durable persistence: failure_reason and metadata in DB (Item 3)
        var pInDb = await context.payments.FirstAsync(p => p.id == resp.PaymentId);
        Assert.Equal("PARTIAL_PAYMENT_UNDERPAID", pInDb.failure_reason);
        Assert.Contains("\"reconciliation_required\":true", pInDb.metadata);

        // Verify refund record was durably created for staff review
        var refundInDb = await context.refunds.FirstOrDefaultAsync(r => r.payment_id == resp.PaymentId);
        Assert.NotNull(refundInDb);
        Assert.Equal("requested", refundInDb.status);
        Assert.Equal(1_000_000m, refundInDb.amount);

        // Verify subsequent GET payment by customer reflects ReconciliationRequired = true
        var detail = await service.GetPaymentByIdAsync(customer.id, customerRoles, resp.PaymentId);
        Assert.NotNull(detail);
        Assert.True(detail.ReconciliationRequired);
        Assert.Equal("PARTIAL_PAYMENT_UNDERPAID", detail.FailureReason);

        // Verify subsequent GET reconciliation list by admin includes this payment
        var adminRoles = new[] { RoleConstants.SystemAdministrator };
        var reconList = await service.GetPaymentsRequiringReconciliationAsync(999, adminRoles);
        Assert.Contains(reconList, p => p.PaymentId == resp.PaymentId);
    }

    [Fact]
    public async Task Checkout_ConcurrentSameKey_DifferentVoucherOrPolicy_ThrowsConflictExceptionUnderLock()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, reservation) = await SeedBaselineAsync(context);

        // Seed 2 policies (policy2 has older valid_from so policy remains the active one)
        var policy2 = new policy_version
        {
            id = 20,
            policy_type = "rental_terms",
            version = "2019.1",
            content = "{}",
            valid_from = new DateOnly(2019, 1, 1),
            created_at = DateTimeOffset.UtcNow
        };
        context.policy_versions.Add(policy2);
        await context.SaveChangesAsync();

        var customerRoles = new[] { RoleConstants.StorageCustomer };
        var commonKey = "RACE-SAME-KEY-1";

        // Request 1 uses policy 1
        var req1 = new CheckoutRequest
        {
            ReservationId = reservation.id,
            PaymentMethod = "demo",
            AcceptedPolicyVersionId = policy.id
        };

        var resp1 = await service.CheckoutAsync(customer.id, customerRoles, req1, commonKey, "127.0.0.1");
        Assert.NotNull(resp1);

        // Request 2 tries with SAME key directly into repository under lock with different policy or method
        var repo = new PaymentRepository(context);
        var txParamsDifferentPolicy = new CheckoutTransactionParams
        {
            ReservationId = reservation.id,
            CustomerId = customer.id,
            PolicyVersionId = policy2.id, // Different policy version!
            PolicyVersionNumber = policy2.version,
            IdempotencyKey = commonKey,
            PaymentMethod = "demo",
            PaymentProvider = "demo",
            ComputedDeposit = reservation.deposit_snapshot,
            ComputedRent = reservation.monthly_rate_snapshot,
            ComputedBookingFee = reservation.booking_fee_snapshot,
            ComputedDiscount = 0m,
            ComputedTotal = reservation.deposit_snapshot + reservation.monthly_rate_snapshot + reservation.booking_fee_snapshot,
            NowUtc = DateTimeOffset.UtcNow,
            EvidenceMetadataJson = "{}"
        };

        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.ExecuteCheckoutTransactionAsync(txParamsDifferentPolicy));
        Assert.Equal("IDEMPOTENCY_PAYLOAD_MISMATCH", ex1.Message);

        // Also test different promotion code under lock
        var txParamsDifferentVoucher = new CheckoutTransactionParams
        {
            ReservationId = reservation.id,
            CustomerId = customer.id,
            PolicyVersionId = policy.id,
            PolicyVersionNumber = policy.version,
            AppliedPromotion = new promotion { code = "DIFFERENT_VOUCHER" },
            IdempotencyKey = commonKey,
            PaymentMethod = "demo",
            PaymentProvider = "demo",
            ComputedDeposit = reservation.deposit_snapshot,
            ComputedRent = reservation.monthly_rate_snapshot,
            ComputedBookingFee = reservation.booking_fee_snapshot,
            ComputedDiscount = 0m,
            ComputedTotal = reservation.deposit_snapshot + reservation.monthly_rate_snapshot + reservation.booking_fee_snapshot,
            NowUtc = DateTimeOffset.UtcNow,
            EvidenceMetadataJson = "{}"
        };

        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.ExecuteCheckoutTransactionAsync(txParamsDifferentVoucher));
        Assert.Equal("IDEMPOTENCY_PAYLOAD_MISMATCH", ex2.Message);
    }

    [Fact]
    public async Task Checkout_ReplayFailedOrCancelledPayment_ThrowsConflictException_DoesNotGenerateUrl()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, _, _, policy, reservation) = await SeedBaselineAsync(context);

        var customerRoles = new[] { RoleConstants.StorageCustomer };
        var key = "KEY-FAILED-REPLAY";
        var req = new CheckoutRequest
        {
            ReservationId = reservation.id,
            PaymentMethod = "demo",
            AcceptedPolicyVersionId = policy.id
        };

        var resp1 = await service.CheckoutAsync(customer.id, customerRoles, req, key, "127.0.0.1");
        Assert.NotNull(resp1);

        // Mark payment as failed (e.g. gateway failed or cancelled)
        var repo = new PaymentRepository(context);
        await repo.ExecuteFinalizeFailedPaymentTransactionAsync(new FinalizeFailedPaymentParams
        {
            PaymentId = resp1.PaymentId,
            Provider = "demo",
            FailureReason = "INSUFFICIENT_FUNDS",
            FailedAt = DateTimeOffset.UtcNow,
            ExternalEventId = "EVT-FAIL-TERMINAL"
        });

        // Replaying with the same idempotency key must be blocked (Item 5)
        var conflictEx = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CheckoutAsync(customer.id, customerRoles, req, key, "127.0.0.1"));

        Assert.Contains("trạng thái 'failed'", conflictEx.Message);
        Assert.Contains("Idempotency-Key mới", conflictEx.Message);
    }

    [Fact]
    public async Task ReviewRefund_ConcurrentReviews_ExactlyOneSucceedsAndOtherReceivesConflict()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context, _, _) = SetupService(dbName);
        var (customer, facility, unitType, policy, reservation) = await SeedBaselineAsync(context);

        // Seed a refund at status requested
        var inv = new invoice
        {
            id = 100,
            invoice_no = "INV-REF-CONC-1",
            customer_id = customer.id,
            reservation_id = reservation.id,
            currency = "VND",
            subtotal_amount = 2_050_000m,
            discount_amount = 0m,
            tax_amount = 0m,
            total_amount = 2_050_000m,
            paid_amount = 2_050_000m,
            status = "paid",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.invoices.Add(inv);

        var payment = new payment
        {
            id = 100,
            customer_id = customer.id,
            target_invoice_id = inv.id,
            amount = 2_050_000m,
            currency = "VND",
            method = "demo",
            provider = "demo",
            status = "succeeded",
            idempotency_key = "IDEM-REF-CONC",
            metadata = "{}",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.payments.Add(payment);

        var refund = new refund
        {
            id = 200,
            payment_id = payment.id,
            amount = 2_050_000m,
            currency = "VND",
            reason = "Test concurrent review",
            provider = "demo",
            idempotency_key = "REF-CONC-200",
            status = "requested",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.refunds.Add(refund);
        await context.SaveChangesAsync();

        var adminRoles = new[] { RoleConstants.SystemAdministrator };

        // Two staff members call ReviewRefundAsync concurrently
        var task1 = service.ReviewRefundAsync(1, adminRoles, refund.id, new ReviewRefundRequest { Decision = "approved", Reason = "Duyet 1" });
        var task2 = service.ReviewRefundAsync(2, adminRoles, refund.id, new ReviewRefundRequest { Decision = "approved", Reason = "Duyet 2" });

        var results = await Task.WhenAll(
            task1.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Error: t.Exception?.InnerException)),
            task2.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Error: t.Exception?.InnerException)));

        var successCount = results.Count(r => r.Success);
        var conflictCount = results.Count(r => !r.Success && r.Error is ConflictException);

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);
    }

    private static Dictionary<string, string> BuildVnpayCallbackParams(
        long paymentId,
        decimal amount,
        string hashSecret,
        string tmnCode = "TESTTMNCODE",
        string currency = "VND",
        string responseCode = "00")
    {
        var rawParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = ((long)(amount * 100m)).ToString(),
            ["vnp_BankCode"] = "NCB",
            ["vnp_CardType"] = "ATM",
            ["vnp_CurrCode"] = currency,
            ["vnp_OrderInfo"] = $"Thanh toan test #{paymentId}",
            ["vnp_PayDate"] = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_TmnCode"] = tmnCode,
            ["vnp_TransactionNo"] = "12345678",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = paymentId.ToString()
        };

        var hashData = new System.Text.StringBuilder();
        foreach (var (k, v) in rawParams)
        {
            if (hashData.Length > 0) hashData.Append('&');
            hashData.Append(System.Net.WebUtility.UrlEncode(k)).Append('=').Append(System.Net.WebUtility.UrlEncode(v));
        }

        var secureHash = VnpayGateway.ComputeHmacSha512(hashSecret, hashData.ToString());
        var dict = new Dictionary<string, string>(rawParams)
        {
            ["vnp_SecureHash"] = secureHash
        };
        return dict;
    }
}
