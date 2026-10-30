using System.Diagnostics.CodeAnalysis;
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

public class ReservationReportServiceTests
{
    private SelfStorageDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SelfStorageDbContext(options);
    }

    private (ReservationServiceKhoiVTH Service, SelfStorageDbContext Context) SetupService(string dbName)
    {
        var context = CreateInMemoryContext(dbName);
        var resRepo = new ReservationRepositoryKhoiVTH(context);
        var userRepo = new UserRepositoryKhoiVTH(context);
        var scopeService = new FacilityScopeServiceKhoiVTH(context, userRepo);
        var logger = NullLogger<ReservationServiceKhoiVTH>.Instance;
        var service = new ReservationServiceKhoiVTH(resRepo, scopeService, logger);
        return (service, context);
    }

    [Fact]
    public async Task GetReservationReport_WhenCustomerRoleOnly_ThrowsForbiddenException()
    {
        var (service, _) = SetupService(nameof(GetReservationReport_WhenCustomerRoleOnly_ThrowsForbiddenException));

        var request = new ReservationReportRequest
        {
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 31)
        };

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetReservationReportAsync(
                actorUserId: 10,
                actorRoles: new[] { RoleConstants.StorageCustomer },
                request: request));

        Assert.Contains("Chỉ nhân viên hoặc quản trị viên", ex.Message);
    }

    [Fact]
    public async Task GetReservationReport_WhenStartDateGreaterThanEndDate_ThrowsBadRequestException()
    {
        var (service, _) = SetupService(nameof(GetReservationReport_WhenStartDateGreaterThanEndDate_ThrowsBadRequestException));

        var request = new ReservationReportRequest
        {
            StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 1, 1)
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.GetReservationReportAsync(
                actorUserId: 1,
                actorRoles: new[] { RoleConstants.SystemAdministrator },
                request: request));

        Assert.Contains("StartDate không thể lớn hơn EndDate", ex.Message);
    }

    [Fact]
    public async Task GetReservationReport_FiltersByDateRange_AndComputesCountsAccurately()
    {
        var (service, context) = SetupService(nameof(GetReservationReport_FiltersByDateRange_AndComputesCountsAccurately));

        // Seed Admin user for scope service
        var adminUser = new user
        {
            id = 1,
            email = "admin@test.com",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var adminRole = new role { id = 1, code = RoleConstants.SystemAdministrator, display_name = "Admin" };
        var adminUserRole = new user_role { user_id = 1, role_id = 1, role = adminRole, user = adminUser };
        adminUser.user_roleusers.Add(adminUserRole);
        context.roles.Add(adminRole);
        context.users.Add(adminUser);
        context.user_roles.Add(adminUserRole);
        await context.SaveChangesAsync();

        // Seed 1 facility
        var fac = new facility
        {
            id = 1,
            code = "FAC-HN",
            name = "Kho Ha Noi",
            address_line = "123 Pham Van Dong",
            city = "Ha Noi",
            status = "active",
            timezone = "Asia/Bangkok"
        };
        context.facilities.Add(fac);
        context.unit_types.Add(new unit_type
        {
            id = 1,
            code = "SMALL",
            name = "Kho nhỏ",
            is_active = true,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });
        context.facility_rates.Add(new facility_rate
        {
            id = 1,
            facility_id = 1,
            unit_type_id = 1,
            valid_from = new DateOnly(2026, 1, 1),
            created_at = DateTimeOffset.UtcNow
        });

        var custUser = new user
        {
            id = 20,
            email = "khach1@test.com",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var profile = new customer_profile
        {
            user_id = 20,
            user = custUser,
            full_name = "Tran Van A",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.users.Add(custUser);
        context.customer_profiles.Add(profile);

        // 3 reservations with different dates and statuses
        var r1 = new reservation
        {
            id = 101,
            reservation_code = "RSV-001",
            facility_id = 1,
            customer_id = 20,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = new DateOnly(2026, 10, 10),
            end_date = new DateOnly(2026, 11, 10),
            status = "confirmed",
            quoted_total = 1200000m,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };

        var r2 = new reservation
        {
            id = 102,
            reservation_code = "RSV-002",
            facility_id = 1,
            customer_id = 20,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = new DateOnly(2026, 10, 20),
            end_date = new DateOnly(2026, 11, 20),
            status = "pending",
            quoted_total = 800000m,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };

        var r3 = new reservation
        {
            id = 103,
            reservation_code = "RSV-003",
            facility_id = 1,
            customer_id = 20,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = new DateOnly(2026, 12, 1),
            end_date = new DateOnly(2027, 1, 1),
            status = "cancelled",
            quoted_total = 1500000m,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };

        context.reservations.AddRange(r1, r2, r3);
        await context.SaveChangesAsync();

        // Query report for month 10/2026
        var request = new ReservationReportRequest
        {
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31)
        };

        var report = await service.GetReservationReportAsync(
            actorUserId: 1,
            actorRoles: new[] { RoleConstants.SystemAdministrator },
            request: request);

        Assert.Equal(2, report.TotalReservationCount);
        Assert.Equal(1, report.ConfirmedCount);
        Assert.Equal(1, report.ActiveHoldCount);
        Assert.Equal(0, report.CancelledCount);
        Assert.Equal(2000000m, report.TotalQuotedAmount);
        Assert.Equal(2, report.Items.Count);
    }

    [Theory]
    [InlineData(RoleConstants.FacilityStaff)]
    [InlineData(RoleConstants.SystemAdministrator)]
    public async Task GetReservationReport_ActorWithoutAccount_ReturnsZeroReservationsFailClosed(string roleCode)
    {
        var (service, context) = SetupService($"{nameof(GetReservationReport_ActorWithoutAccount_ReturnsZeroReservationsFailClosed)}_{roleCode}");

        // Create reservations in facility 1 and 2
        var r1 = new reservation
        {
            id = 201,
            reservation_code = "RSV-201",
            facility_id = 1,
            customer_id = 20,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = new DateOnly(2026, 10, 5),
            end_date = new DateOnly(2026, 11, 5),
            status = "confirmed",
            quoted_total = 1200000m,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var r2 = new reservation
        {
            id = 202,
            reservation_code = "RSV-202",
            facility_id = 2,
            customer_id = 21,
            unit_type_id = 2,
            facility_rate_id = 2,
            start_date = new DateOnly(2026, 10, 10),
            end_date = new DateOnly(2026, 11, 10),
            status = "confirmed",
            quoted_total = 1500000m,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.reservations.AddRange(r1, r2);
        await context.SaveChangesAsync();

        // Staff user 999 with no facility assignments (accessibleFacilityIds is empty)
        var request = new ReservationReportRequest
        {
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31)
        };

        var report = await service.GetReservationReportAsync(
            actorUserId: 999,
            actorRoles: new[] { roleCode },
            request: request);

        Assert.Equal(0, report.TotalReservationCount);
        Assert.Empty(report.Items);
        Assert.Equal(0m, report.TotalQuotedAmount);
    }

    [Fact]
    public async Task GetReservationReport_StaffAccountWithoutAssignment_ReturnsNoReservations()
    {
        var (service, context) = SetupService(nameof(GetReservationReport_StaffAccountWithoutAssignment_ReturnsNoReservations));
        var staff = new user
        {
            id = 777,
            email = "unassigned-staff@test.com",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var staffRole = new role { id = 777, code = RoleConstants.FacilityStaff, display_name = "Staff" };
        staff.user_roleusers.Add(new user_role { user_id = staff.id, role_id = staffRole.id, user = staff, role = staffRole });
        context.users.Add(staff);
        context.reservations.Add(new reservation
        {
            id = 777,
            reservation_code = "RSV-777",
            facility_id = 1,
            customer_id = 20,
            unit_type_id = 1,
            facility_rate_id = 1,
            start_date = new DateOnly(2026, 10, 10),
            end_date = new DateOnly(2026, 11, 10),
            status = "confirmed",
            quoted_total = 100m,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var report = await service.GetReservationReportAsync(
            staff.id,
            new[] { RoleConstants.FacilityStaff },
            new ReservationReportRequest { StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) });

        Assert.Empty(report.Items);
        Assert.Equal(0, report.TotalReservationCount);
    }
}
