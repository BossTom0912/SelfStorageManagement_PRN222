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
    public async Task HasAccessToFacilityAsync_ForBusinessOperationsManager_ShouldReturnTrueRegardlessOfAssignment()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new FacilityScopeService(context, _userRepoMock.Object);

        var bomUser = new user
        {
            id = 2,
            email = "bom@example.test",
            status = UserStatusConstants.Active
        };
        var bomRole = new role { id = 4, code = RoleConstants.BusinessOperationsManager };
        bomUser.user_roleusers.Add(new user_role { user_id = 2, role_id = 4, role = bomRole });

        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bomUser);

        var result = await service.HasAccessToFacilityAsync(2, 999, RoleConstants.FacilityManager);

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

    [Fact]
    public async Task HasAccessToFacilityAsync_WhenRoleRevokedButAssignmentStillActive_ShouldReturnFalse()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

        // User whose staff role was revoked, leaving only customer role (or no roles)
        var user = new user
        {
            id = 4,
            email = "revoked@example.test",
            status = UserStatusConstants.Active,
            employee_profile = new employee_profile
            {
                user_id = 4,
                employee_code = "EMP-004",
                full_name = "Ex Staff",
                hire_date = new DateOnly(2025, 1, 1),
                employment_status = EmploymentStatusConstants.Active
            }
        };
        var customerRole = new role { id = 1, code = RoleConstants.StorageCustomer };
        user.user_roleusers.Add(new user_role { user_id = 4, role_id = 1, role = customerRole });

        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Old unexpired assignment still present in database
        context.staff_facility_assignments.Add(new staff_facility_assignment
        {
            id = 10,
            employee_id = 4,
            facility_id = 10,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = DateTimeOffset.UtcNow.AddDays(-10),
            ends_at = null
        });
        await context.SaveChangesAsync();

        var service = new FacilityScopeService(context, _userRepoMock.Object);

        var result = await service.HasAccessToFacilityAsync(4, 10, RoleConstants.FacilityStaff);

        Assert.False(result); // Must deny access because role was revoked
    }

    [Fact]
    public async Task HasAccessToFacilityAsync_StaffUser_RequestingManagerScope_ShouldReturnFalse()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

        var staffUser = new user
        {
            id = 5,
            email = "staff5@example.test",
            status = UserStatusConstants.Active,
            employee_profile = new employee_profile
            {
                user_id = 5,
                employee_code = "EMP-005",
                full_name = "Staff Five",
                hire_date = new DateOnly(2025, 1, 1),
                employment_status = EmploymentStatusConstants.Active
            }
        };
        var staffRole = new role { id = 2, code = RoleConstants.FacilityStaff };
        staffUser.user_roleusers.Add(new user_role { user_id = 5, role_id = 2, role = staffRole });

        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(staffUser);

        // Staff assignment at facility 10
        context.staff_facility_assignments.Add(new staff_facility_assignment
        {
            id = 11,
            employee_id = 5,
            facility_id = 10,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = DateTimeOffset.UtcNow.AddDays(-10),
            ends_at = null
        });
        await context.SaveChangesAsync();

        var service = new FacilityScopeService(context, _userRepoMock.Object);

        // Staff cannot access Manager scope
        var result = await service.HasAccessToFacilityAsync(5, 10, RoleConstants.FacilityManager);

        Assert.False(result);
    }

    [Fact]
    public async Task HasAccessToFacilityAsync_ManagerUser_RequestingStaffScope_ShouldReturnTrue()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

        var managerUser = new user
        {
            id = 6,
            email = "manager6@example.test",
            status = UserStatusConstants.Active,
            employee_profile = new employee_profile
            {
                user_id = 6,
                employee_code = "EMP-006",
                full_name = "Manager Six",
                hire_date = new DateOnly(2025, 1, 1),
                employment_status = EmploymentStatusConstants.Active
            }
        };
        var managerRole = new role { id = 3, code = RoleConstants.FacilityManager };
        managerUser.user_roleusers.Add(new user_role { user_id = 6, role_id = 3, role = managerRole });

        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsync(6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerUser);

        // Manager assignment at facility 10
        context.staff_facility_assignments.Add(new staff_facility_assignment
        {
            id = 12,
            employee_id = 6,
            facility_id = 10,
            assignment_role = RoleConstants.FacilityManager,
            starts_at = DateTimeOffset.UtcNow.AddDays(-10),
            ends_at = null
        });
        await context.SaveChangesAsync();

        var service = new FacilityScopeService(context, _userRepoMock.Object);

        // Manager can perform staff operations under current rules
        var staffResult = await service.HasAccessToFacilityAsync(6, 10, RoleConstants.FacilityStaff);
        var managerResult = await service.HasAccessToFacilityAsync(6, 10, RoleConstants.FacilityManager);

        Assert.True(staffResult);
        Assert.True(managerResult);
    }

    [Fact]
    public async Task GetAccessibleFacilityIdsAsync_WhenRoleRevoked_ShouldReturnEmptyList()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

        var user = new user
        {
            id = 7,
            email = "revoked7@example.test",
            status = UserStatusConstants.Active,
            employee_profile = new employee_profile
            {
                user_id = 7,
                employee_code = "EMP-007",
                employment_status = EmploymentStatusConstants.Active
            }
        };
        // No staff/manager role

        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        context.staff_facility_assignments.Add(new staff_facility_assignment
        {
            id = 13,
            employee_id = 7,
            facility_id = 10,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = DateTimeOffset.UtcNow.AddDays(-10),
            ends_at = null
        });
        await context.SaveChangesAsync();

        var service = new FacilityScopeService(context, _userRepoMock.Object);

        var ids = await service.GetAccessibleFacilityIdsAsync(7);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task GetActiveAssignmentsForUserAsync_WhenRoleRevoked_ShouldReturnEmptyList()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());

        var user = new user
        {
            id = 8,
            email = "revoked8@example.test",
            status = UserStatusConstants.Active,
            employee_profile = new employee_profile
            {
                user_id = 8,
                employee_code = "EMP-008",
                employment_status = EmploymentStatusConstants.Active
            }
        };
        // No staff/manager role

        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsync(8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        context.facilities.Add(new facility
        {
            id = 10,
            code = "FAC-10",
            name = "Facility 10",
            address_line = "123 Street",
            city = "Ho Chi Minh",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        });
        context.staff_facility_assignments.Add(new staff_facility_assignment
        {
            id = 14,
            employee_id = 8,
            facility_id = 10,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = DateTimeOffset.UtcNow.AddDays(-10),
            ends_at = null
        });
        await context.SaveChangesAsync();

        var service = new FacilityScopeService(context, _userRepoMock.Object);

        var assignments = await service.GetActiveAssignmentsForUserAsync(8);

        Assert.Empty(assignments);
    }
}
