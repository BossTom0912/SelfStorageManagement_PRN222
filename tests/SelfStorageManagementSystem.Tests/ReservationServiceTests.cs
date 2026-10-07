using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class ReservationServiceTests
{
    private SelfStorageDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SelfStorageDbContext(options);
    }

    private (ReservationService Service, SelfStorageDbContext Context) SetupService(string dbName)
    {
        var context = CreateInMemoryContext(dbName);
        var resRepo = new ReservationRepository(context);
        var userRepo = new UserRepository(context);
        var scopeService = new FacilityScopeService(context, userRepo);
        var logger = NullLogger<ReservationService>.Instance;
        var service = new ReservationService(resRepo, scopeService, logger);
        return (service, context);
    }

    private async Task SeedBasicTestDataAsync(SelfStorageDbContext context)
    {
        var customerUser = new user
        {
            id = 10,
            email = "customer@example.test",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var customerProfile = new customer_profile
        {
            user_id = 10,
            full_name = "Nguyen Van Khach",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var role = new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" };
        var userRole = new user_role { user_id = 10, role_id = 1, role = role, user = customerUser };
        customerUser.user_roleusers.Add(userRole);

        var facility = new facility
        {
            id = 1,
            code = "FAC-HCM",
            name = "Kho Thu Duc",
            address_line = "123 Vo Van Ngan",
            city = "Ho Chi Minh City",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        };

        var unitType = new unit_type
        {
            id = 1,
            code = "S",
            name = "Kho Nho 3m2",
            area_m2 = 3.0m,
            climate_controlled = false,
            is_active = true
        };

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rate = new facility_rate
        {
            id = 1,
            facility_id = 1,
            unit_type_id = 1,
            monthly_rate = 900000m,
            deposit_amount = 900000m, // 100% per BR-FIN-01
            booking_fee = 50000m,
            valid_from = today.AddDays(-30)
        };

        var unit = new storage_unit
        {
            id = 1,
            facility_id = 1,
            unit_type_id = 1,
            unit_code = "A-101",
            physical_status = "available",
            is_listed = true
        };

        context.users.Add(customerUser);
        context.customer_profiles.Add(customerProfile);
        context.roles.Add(role);
        context.user_roles.Add(userRole);
        context.facilities.Add(facility);
        context.unit_types.Add(unitType);
        context.facility_rates.Add(rate);
        context.storage_units.Add(unit);

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateReservationHold_ValidOneMonthTerm_ShouldSucceedAndHoldFor15Minutes()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(2);
        var endDate = startDate.AddMonths(1); // Boundary: exactly 1 month

        var request = new CreateReservationRequest
        {
            FacilityId = 1,
            UnitTypeId = 1,
            RentalStartDate = startDate,
            RentalEndDate = endDate
        };

        var result = await service.CreateReservationHoldAsync(10, request);

        Assert.NotNull(result);
        Assert.StartsWith("RES-FAC-HCM-", result.ReservationCode);
        Assert.Equal("pending", result.Status);
        Assert.Equal(900000m, result.MonthlyRateSnapshot);
        Assert.Equal(900000m, result.DepositSnapshot); // BR-FIN-01
        Assert.Equal(50000m, result.BookingFeeSnapshot);
        Assert.Equal(1850000m, result.QuotedTotal); // 900k + 900k + 50k
        Assert.True(result.IsHoldActive);
        Assert.True(result.HoldUntil > DateTimeOffset.UtcNow);
        Assert.True(result.HoldUntil <= DateTimeOffset.UtcNow.AddMinutes(16));
        Assert.True(result.CanCancel);
    }

    [Fact]
    public async Task CreateReservationHold_ValidTwelveMonthsTerm_ShouldSucceed()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(5);
        var endDate = startDate.AddMonths(12); // Boundary: exactly 12 months

        var request = new CreateReservationRequest
        {
            FacilityId = 1,
            UnitTypeId = 1,
            RentalStartDate = startDate,
            RentalEndDate = endDate
        };

        var result = await service.CreateReservationHoldAsync(10, request);

        Assert.NotNull(result);
        Assert.Equal(12, result.RentalMonths);
        Assert.Equal("pending", result.Status);
    }

    [Fact]
    public async Task CreateReservationHold_LessThanOneMonth_ShouldThrowBadRequestException()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(2);
        var endDate = startDate.AddDays(20); // Less than 1 month

        var request = new CreateReservationRequest
        {
            FacilityId = 1,
            UnitTypeId = 1,
            RentalStartDate = startDate,
            RentalEndDate = endDate
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateReservationHoldAsync(10, request));

        Assert.Contains("BR-RSV-02", ex.Message);
        Assert.Contains("tối thiểu là 1 tháng", ex.Message);
    }

    [Fact]
    public async Task CreateReservationHold_MoreThanTwelveMonths_ShouldThrowBadRequestException()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(2);
        var endDate = startDate.AddMonths(12).AddDays(1); // 12 months + 1 day

        var request = new CreateReservationRequest
        {
            FacilityId = 1,
            UnitTypeId = 1,
            RentalStartDate = startDate,
            RentalEndDate = endDate
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateReservationHoldAsync(10, request));

        Assert.Contains("BR-RSV-02", ex.Message);
        Assert.Contains("tối đa là 12 tháng", ex.Message);
    }

    [Fact]
    public async Task CreateReservationHold_StartDateInPast_ShouldThrowBadRequestException()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-2); // In past
        var endDate = startDate.AddMonths(2);

        var request = new CreateReservationRequest
        {
            FacilityId = 1,
            UnitTypeId = 1,
            RentalStartDate = startDate,
            RentalEndDate = endDate
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateReservationHoldAsync(10, request));

        Assert.Contains("quá khứ", ex.Message);
    }

    [Fact]
    public async Task CreateReservationHold_NoActiveFacilityRate_ShouldThrowBadRequestException()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        // Remove active rates
        context.facility_rates.RemoveRange(context.facility_rates);
        await context.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new CreateReservationRequest
        {
            FacilityId = 1,
            UnitTypeId = 1,
            RentalStartDate = today.AddDays(5),
            RentalEndDate = today.AddDays(5).AddMonths(1)
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateReservationHoldAsync(10, request));

        Assert.Contains("biểu phí hợp lệ", ex.Message);
    }

    [Fact]
    public async Task CreateReservationHold_CapacityExhausted_ShouldThrowConflictException()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(2);
        var endDate = startDate.AddMonths(1);

        // Pre-create an active reservation holding the only unit slot
        var existingReservation = new reservation
        {
            reservation_code = "RES-EXISTING-01",
            customer_id = 10,
            facility_id = 1,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = startDate,
            end_date = endDate,
            monthly_rate_snapshot = 900000m,
            deposit_snapshot = 900000m,
            quoted_total = 1850000m,
            hold_until = DateTimeOffset.UtcNow.AddMinutes(10), // Still active hold
            status = "pending",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.reservations.Add(existingReservation);
        await context.SaveChangesAsync();

        var request = new CreateReservationRequest
        {
            FacilityId = 1,
            UnitTypeId = 1,
            RentalStartDate = startDate,
            RentalEndDate = endDate
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationHoldAsync(10, request));

        Assert.Contains("không còn đủ sức chứa", ex.Message);
    }

    [Fact]
    public async Task CreateReservationHold_ExpiredHoldDoesNotBlockCapacity()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(2);
        var endDate = startDate.AddMonths(1);

        // An expired reservation (hold_until has passed)
        var expiredReservation = new reservation
        {
            reservation_code = "RES-OLD-01",
            customer_id = 10,
            facility_id = 1,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = startDate,
            end_date = endDate,
            monthly_rate_snapshot = 900000m,
            deposit_snapshot = 900000m,
            quoted_total = 1850000m,
            hold_until = DateTimeOffset.UtcNow.AddMinutes(-5), // Expired 5 mins ago
            status = "pending",
            created_at = DateTimeOffset.UtcNow.AddMinutes(-20),
            updated_at = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
        context.reservations.Add(expiredReservation);
        await context.SaveChangesAsync();

        var request = new CreateReservationRequest
        {
            FacilityId = 1,
            UnitTypeId = 1,
            RentalStartDate = startDate,
            RentalEndDate = endDate
        };

        // Should succeed because expired hold doesn't take capacity
        var result = await service.CreateReservationHoldAsync(10, request);

        Assert.NotNull(result);
        Assert.Equal("pending", result.Status);
    }

    [Fact]
    public async Task CancelReservation_Owner_ShouldSucceedAndMarkCancelled()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var r = new reservation
        {
            reservation_code = "RES-CANCEL-01",
            customer_id = 10,
            facility_id = 1,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = today.AddDays(2),
            end_date = today.AddDays(2).AddMonths(1),
            monthly_rate_snapshot = 900000m,
            deposit_snapshot = 900000m,
            quoted_total = 1850000m,
            hold_until = DateTimeOffset.UtcNow.AddMinutes(10),
            status = "pending",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.reservations.Add(r);
        await context.SaveChangesAsync();

        var cancelResult = await service.CancelReservationAsync(
            10,
            new[] { RoleConstants.StorageCustomer },
            r.id,
            new CancelReservationRequest { Reason = "Thay doi ke hoach" });

        Assert.Equal("cancelled", cancelResult.Status);
        Assert.NotNull(cancelResult.CancelledAt);
        Assert.Equal("Thay doi ke hoach", cancelResult.CancellationReason);
    }

    [Fact]
    public async Task CancelReservation_Idempotent_SecondCallShouldReturnCancelledState()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var r = new reservation
        {
            reservation_code = "RES-CANCEL-IDEM",
            customer_id = 10,
            facility_id = 1,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = today.AddDays(2),
            end_date = today.AddDays(2).AddMonths(1),
            monthly_rate_snapshot = 900000m,
            deposit_snapshot = 900000m,
            quoted_total = 1850000m,
            hold_until = DateTimeOffset.UtcNow.AddMinutes(10),
            status = "cancelled",
            cancelled_at = DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellation_reason = "Customer cancelled",
            created_at = DateTimeOffset.UtcNow.AddMinutes(-5),
            updated_at = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        context.reservations.Add(r);
        await context.SaveChangesAsync();

        var cancelResult = await service.CancelReservationAsync(
            10,
            new[] { RoleConstants.StorageCustomer },
            r.id,
            null);

        Assert.Equal("cancelled", cancelResult.Status);
    }

    [Fact]
    public async Task CancelReservation_UnauthorizedUser_ShouldThrowForbiddenException()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var r = new reservation
        {
            reservation_code = "RES-FORBIDDEN",
            customer_id = 10, // Belongs to customer 10
            facility_id = 1,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = today.AddDays(2),
            end_date = today.AddDays(2).AddMonths(1),
            monthly_rate_snapshot = 900000m,
            deposit_snapshot = 900000m,
            quoted_total = 1850000m,
            hold_until = DateTimeOffset.UtcNow.AddMinutes(10),
            status = "pending",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.reservations.Add(r);
        await context.SaveChangesAsync();

        // Customer 99 tries to cancel customer 10's reservation
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CancelReservationAsync(99, new[] { RoleConstants.StorageCustomer }, r.id, null));
    }

    [Fact]
    public async Task ExpireOverdueHolds_ShouldTransitionPastHoldsToExpired()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rPast = new reservation
        {
            reservation_code = "RES-EXPIRED-TEST",
            customer_id = 10,
            facility_id = 1,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = today.AddDays(2),
            end_date = today.AddDays(2).AddMonths(1),
            monthly_rate_snapshot = 900000m,
            deposit_snapshot = 900000m,
            quoted_total = 1850000m,
            hold_until = DateTimeOffset.UtcNow.AddMinutes(-2), // 2 minutes past
            status = "pending",
            created_at = DateTimeOffset.UtcNow.AddMinutes(-17),
            updated_at = DateTimeOffset.UtcNow.AddMinutes(-17)
        };
        var rActive = new reservation
        {
            reservation_code = "RES-ACTIVE-TEST",
            customer_id = 10,
            facility_id = 1,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = today.AddDays(2),
            end_date = today.AddDays(2).AddMonths(1),
            monthly_rate_snapshot = 900000m,
            deposit_snapshot = 900000m,
            quoted_total = 1850000m,
            hold_until = DateTimeOffset.UtcNow.AddMinutes(10), // Still active
            status = "pending",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.reservations.AddRange(rPast, rActive);
        await context.SaveChangesAsync();

        var expiredCount = await service.ExpireOverdueHoldsAsync();

        Assert.Equal(1, expiredCount);

        var reloadedPast = await context.reservations.FindAsync(rPast.id);
        var reloadedActive = await context.reservations.FindAsync(rActive.id);

        Assert.Equal("expired", reloadedPast!.status);
        Assert.Equal("pending", reloadedActive!.status);
    }

    [Fact]
    public async Task CreateReservationHold_UserWithoutCustomerRole_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = SetupService(dbName);
        await SeedBasicTestDataAsync(context);

        // Create a user who has profile but only Staff role
        var staffRole = new role { id = 2, code = RoleConstants.FacilityStaff, display_name = "Staff" };
        var staffUser = new user
        {
            id = 20,
            email = "staff@example.test",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var staffProfile = new customer_profile
        {
            user_id = 20,
            full_name = "Staff Profile",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var staffUserRole = new user_role { user_id = 20, role_id = 2, role = staffRole, user = staffUser };
        staffUser.user_roleusers.Add(staffUserRole);

        context.roles.Add(staffRole);
        context.users.Add(staffUser);
        context.customer_profiles.Add(staffProfile);
        context.user_roles.Add(staffUserRole);
        await context.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new CreateReservationRequest
        {
            FacilityId = 1,
            UnitTypeId = 1,
            RentalStartDate = today.AddDays(2),
            RentalEndDate = today.AddDays(2).AddMonths(1)
        };

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateReservationHoldAsync(20, request));

        Assert.Contains("không có vai trò khách hàng", ex.Message);
    }

    [Fact]
    public async Task CancelReservation_WhenConfirmed_ThrowsConflictException()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var r = new reservation
        {
            reservation_code = "RES-CONFIRMED-CANCEL",
            customer_id = 10,
            facility_id = 1,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = today.AddDays(2),
            end_date = today.AddDays(2).AddMonths(1),
            monthly_rate_snapshot = 900000m,
            deposit_snapshot = 900000m,
            quoted_total = 1850000m,
            hold_until = DateTimeOffset.UtcNow.AddMinutes(10),
            status = "confirmed",
            confirmed_at = DateTimeOffset.UtcNow,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.reservations.Add(r);
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CancelReservationAsync(
                10,
                new[] { RoleConstants.StorageCustomer },
                r.id,
                new CancelReservationRequest { Reason = "Attempt to cancel paid reservation" }));

        Assert.Contains("đã được xác nhận thanh toán", ex.Message);
    }

    [Fact]
    public async Task CancelReservation_WhenExpired_ThrowsConflictException()
    {
        var (service, context) = SetupService(Guid.NewGuid().ToString());
        await SeedBasicTestDataAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var r = new reservation
        {
            reservation_code = "RES-EXPIRED-CANCEL",
            customer_id = 10,
            facility_id = 1,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = today.AddDays(2),
            end_date = today.AddDays(2).AddMonths(1),
            monthly_rate_snapshot = 900000m,
            deposit_snapshot = 900000m,
            quoted_total = 1850000m,
            hold_until = DateTimeOffset.UtcNow.AddMinutes(-5),
            status = "pending",
            created_at = DateTimeOffset.UtcNow.AddMinutes(-20),
            updated_at = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
        context.reservations.Add(r);
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CancelReservationAsync(
                10,
                new[] { RoleConstants.StorageCustomer },
                r.id,
                new CancelReservationRequest { Reason = "Attempt to cancel expired" }));

        Assert.Contains("hết hạn giữ chỗ", ex.Message);
    }

    [Fact]
    public async Task ExpireOverdueHolds_WhenConcurrentConfirmationOccurs_DoesNotOverwriteConfirmed()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service1, context1) = SetupService(dbName);
        await SeedBasicTestDataAsync(context1);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var r = new reservation
        {
            reservation_code = "RES-RACE-CONFIRM",
            customer_id = 10,
            facility_id = 1,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = today.AddDays(2),
            end_date = today.AddDays(2).AddMonths(1),
            monthly_rate_snapshot = 900000m,
            deposit_snapshot = 900000m,
            quoted_total = 1850000m,
            hold_until = DateTimeOffset.UtcNow.AddMinutes(-1), // overdue
            status = "pending",
            created_at = DateTimeOffset.UtcNow.AddMinutes(-16),
            updated_at = DateTimeOffset.UtcNow.AddMinutes(-16)
        };
        context1.reservations.Add(r);
        await context1.SaveChangesAsync();

        // Separate DbContext simulates a concurrent transaction confirming payment
        var context2 = CreateInMemoryContext(dbName);
        var rInTx2 = await context2.reservations.FirstAsync(x => x.id == r.id);
        rInTx2.status = "confirmed";
        rInTx2.confirmed_at = DateTimeOffset.UtcNow;
        rInTx2.updated_at = DateTimeOffset.UtcNow;
        await context2.SaveChangesAsync();

        // Now worker runs on context1/service1
        var expiredCount = await service1.ExpireOverdueHoldsAsync();

        // The reservation was already confirmed, so worker must NOT expire it
        Assert.Equal(0, expiredCount);

        var reloaded = await context1.reservations.AsNoTracking().FirstAsync(x => x.id == r.id);
        Assert.Equal("confirmed", reloaded.status);
    }
}
