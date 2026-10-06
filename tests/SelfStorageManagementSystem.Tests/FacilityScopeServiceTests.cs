using Microsoft.EntityFrameworkCore;
using Moq;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class FacilityScopeServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();

    private SelfStorageDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SelfStorageDbContext(options);
    }

    [Fact]
    public async Task HasAccessToFacilityAsync_ForSystemAdministrator_ShouldReturnTrueRegardlessOfAssignment()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new FacilityScopeService(context, _userRepoMock.Object);

        var adminUser = new user
        {
            id = 1,
            email = "admin@example.test",
            status = UserStatusConstants.Active
        };
        var adminRole = new role { id = 5, code = RoleConstants.SystemAdministrator };
        adminUser.user_roleusers.Add(new user_role { user_id = 1, role_id = 5, role = adminRole });

        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminUser);

        var result = await service.HasAccessToFacilityAsync(1, 999, RoleConstants.FacilityStaff);

        Assert.True(result);
    }

    [Fact]
    public async Task HasAccessToFacilityAsync_ForStaffWithActiveAssignment_ShouldReturnTrue()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

        var staffUser = new user
        {
            id = 3,
            email = "staff@example.test",
            status = UserStatusConstants.Active,
            employee_profile = new employee_profile
            {
                user_id = 3,
                employee_code = "EMP-001",
                full_name = "Staff One",
                hire_date = new DateOnly(2025, 1, 1),
                employment_status = EmploymentStatusConstants.Active
            }
        };
        var staffRole = new role { id = 2, code = RoleConstants.FacilityStaff };
        staffUser.user_roleusers.Add(new user_role { user_id = 3, role_id = 2, role = staffRole });

        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(staffUser);

        context.staff_facility_assignments.Add(new staff_facility_assignment
        {
            id = 1,
            employee_id = 3,
            facility_id = 10,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = DateTimeOffset.UtcNow.AddDays(-10),
            ends_at = null
        });
        await context.SaveChangesAsync();

        var service = new FacilityScopeService(context, _userRepoMock.Object);

        var hasAccessFacility10 = await service.HasAccessToFacilityAsync(3, 10, RoleConstants.FacilityStaff);
        var hasAccessFacility20 = await service.HasAccessToFacilityAsync(3, 20, RoleConstants.FacilityStaff);

        Assert.True(hasAccessFacility10);
        Assert.False(hasAccessFacility20); // Different facility
    }

    [Fact]
    public async Task HasAccessToFacilityAsync_ForStaffWithExpiredAssignment_ShouldReturnFalse()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

        var staffUser = new user
        {
            id = 3,
            email = "staff@example.test",
            status = UserStatusConstants.Active,
            employee_profile = new employee_profile
            {
                user_id = 3,
                employee_code = "EMP-001",
                full_name = "Staff One",
                hire_date = new DateOnly(2025, 1, 1),
                employment_status = EmploymentStatusConstants.Active
            }
        };
        var staffRole = new role { id = 2, code = RoleConstants.FacilityStaff };
        staffUser.user_roleusers.Add(new user_role { user_id = 3, role_id = 2, role = staffRole });

        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(staffUser);

        // Expired yesterday
        context.staff_facility_assignments.Add(new staff_facility_assignment
        {
            id = 2,
            employee_id = 3,
            facility_id = 10,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = DateTimeOffset.UtcNow.AddDays(-30),
            ends_at = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await context.SaveChangesAsync();

        var service = new FacilityScopeService(context, _userRepoMock.Object);

        var result = await service.HasAccessToFacilityAsync(3, 10, RoleConstants.FacilityStaff);

        Assert.False(result);
    }
}
