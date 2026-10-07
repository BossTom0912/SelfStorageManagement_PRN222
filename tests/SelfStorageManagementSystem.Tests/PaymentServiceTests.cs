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

    private (PaymentService Service, SelfStorageDbContext Context, DemoGateway DemoGw, VnpayGateway VnpayGw) SetupService(string dbName)
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
            ["VnPay:ReturnUrl"] = "http://localhost:5000/api/payments/vnpay/return"
        };
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
}
