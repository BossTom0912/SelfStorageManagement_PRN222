using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class AdminAccountServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IRepository<role>> _roleRepoMock = new();
    private readonly Mock<IRepository<user_role>> _userRoleRepoMock = new();
    private readonly Mock<IRepository<employee_profile>> _employeeProfileRepoMock = new();
    private readonly Mock<IRepository<staff_facility_assignment>> _assignmentRepoMock = new();
    private readonly Mock<IRepository<facility>> _facilityRepoMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IAuditService> _auditMock = new();
    private readonly Mock<ILogger<AdminAccountService>> _loggerMock = new();

    private SelfStorageDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SelfStorageDbContext(options);
    }

    [Fact]
    public async Task UpdateUserStatus_ShouldPreventLockingTheOnlyActiveSystemAdministrator()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new AdminAccountService(
            context,
            _userRepoMock.Object,
            _roleRepoMock.Object,
            _userRoleRepoMock.Object,
            _employeeProfileRepoMock.Object,
            _assignmentRepoMock.Object,
            _facilityRepoMock.Object,
            _passwordHasherMock.Object,
            _auditMock.Object,
            _loggerMock.Object);

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

        // 0 remaining active admins if user 1 is excluded
        _userRepoMock.Setup(r => r.CountActiveSystemAdministratorsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.UpdateUserStatusAsync(1, new UpdateUserStatusRequest { Status = UserStatusConstants.Locked }, 1));

        Assert.Contains("only remaining active system administrator", ex.Message);
    }

    [Fact]
    public async Task ManageUserRoles_ShouldPreventRevokingSystemAdministratorFromLastAdmin()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new AdminAccountService(
            context,
            _userRepoMock.Object,
            _roleRepoMock.Object,
            _userRoleRepoMock.Object,
            _employeeProfileRepoMock.Object,
            _assignmentRepoMock.Object,
            _facilityRepoMock.Object,
            _passwordHasherMock.Object,
            _auditMock.Object,
            _loggerMock.Object);

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

        // 0 other admins
        _userRepoMock.Setup(r => r.CountActiveSystemAdministratorsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.ManageUserRolesAsync(1, new ManageUserRolesRequest { RoleCodes = new List<string> { RoleConstants.StorageCustomer } }, 1));

        Assert.Contains("only remaining system administrator", ex.Message);
    }

    [Fact]
    public async Task ManageUserRoles_ShouldPreventAssigningStaffRoleWithoutEmployeeProfile()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new AdminAccountService(
            context,
            _userRepoMock.Object,
            _roleRepoMock.Object,
            _userRoleRepoMock.Object,
            _employeeProfileRepoMock.Object,
            _assignmentRepoMock.Object,
            _facilityRepoMock.Object,
            _passwordHasherMock.Object,
            _auditMock.Object,
            _loggerMock.Object);

        var customerUser = new user
        {
            id = 2,
            email = "customer@example.test",
            status = UserStatusConstants.Active,
            employee_profile = null // No employee profile!
        };

        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customerUser);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.ManageUserRolesAsync(2, new ManageUserRolesRequest { RoleCodes = new List<string> { RoleConstants.FacilityStaff } }, 1));

        Assert.Contains("without an employee profile", ex.Message);
    }

    [Fact]
    public async Task AssignFacility_WithOverlappingPeriod_ShouldThrowConflictException()
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
                employment_status = EmploymentStatusConstants.Active
            }
        };
        var staffRole = new role { id = 2, code = RoleConstants.FacilityStaff };
        staffUser.user_roleusers.Add(new user_role { user_id = 3, role_id = 2, role = staffRole });

        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(staffUser);

        _facilityRepoMock.Setup(f => f.GetByIdAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new facility { id = 10, code = "FAC-1", name = "Facility 1" });

        // Add existing assignment from day 1 to day 30
        var now = DateTimeOffset.UtcNow;
        context.staff_facility_assignments.Add(new staff_facility_assignment
        {
            id = 1,
            employee_id = 3,
            facility_id = 10,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = now.AddDays(-10),
            ends_at = now.AddDays(20)
        });
        await context.SaveChangesAsync();

        var service = new AdminAccountService(
            context,
            _userRepoMock.Object,
            _roleRepoMock.Object,
            _userRoleRepoMock.Object,
            _employeeProfileRepoMock.Object,
            _assignmentRepoMock.Object,
            _facilityRepoMock.Object,
            _passwordHasherMock.Object,
            _auditMock.Object,
            _loggerMock.Object);

        var request = new CreateFacilityAssignmentRequest
        {
            FacilityId = 10,
            AssignmentRole = RoleConstants.FacilityStaff,
            StartsAt = now,
            EndsAt = now.AddDays(15) // Overlaps!
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignFacilityAsync(3, request, 1));

        Assert.Contains("overlapping", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
