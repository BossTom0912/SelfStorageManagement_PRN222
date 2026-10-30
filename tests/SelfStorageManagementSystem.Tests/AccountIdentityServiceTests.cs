using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Models;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class AccountIdentityServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IRepository<role>> _roleRepoMock = new();
    private readonly Mock<IRepository<user_role>> _userRoleRepoMock = new();
    private readonly Mock<IRepository<customer_profile>> _customerProfileRepoMock = new();
    private readonly Mock<IRepository<login_history>> _loginHistoryRepoMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IFacilityScopeService> _facilityScopeMock = new();
    private readonly Mock<IAuditService> _auditMock = new();

    private readonly AccountIdentityServiceKhoiVTH _identityService;

    public AccountIdentityServiceTests()
    {
        _identityService = new AccountIdentityServiceKhoiVTH(
            _userRepoMock.Object,
            _roleRepoMock.Object,
            _userRoleRepoMock.Object,
            _customerProfileRepoMock.Object,
            _loginHistoryRepoMock.Object,
            _passwordHasherMock.Object,
            _facilityScopeMock.Object,
            _auditMock.Object,
            NullLogger<AccountIdentityServiceKhoiVTH>.Instance);
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidCredentials_ShouldSucceed_UpdateLastLogin_RecordHistory_AndReturnIdentityWithoutToken()
    {
        // Arrange
        var email = "customer@example.test";
        var password = "ValidPassword123";
        var user = new user
        {
            id = 101,
            email = email,
            phone_number = "0901234567",
            password_hash = "hashed_pw",
            status = UserStatusConstants.Active,
            customer_profile = new customer_profile { full_name = "Le Van A" }
        };
        var role = new role { id = 1, code = RoleConstants.StorageCustomer };
        user.user_roleusers.Add(new user_role { user_id = 101, role_id = 1, role = role });

        _userRepoMock.Setup(r => r.GetIdentitySnapshotByEmailAsync(email, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentitySnapshotKhoiVTH?)null); // tests fallback or entity lookup
        _userRepoMock.Setup(r => r.GetByEmailWithRolesAndProfilesAsNoTrackingAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock.Setup(h => h.VerifyPassword("hashed_pw", password))
            .Returns(true);

        // Act
        var result = await _identityService.AuthenticateAsync(email, password);

        // Assert: Identity & Roles present, absolutely no token or hash
        Assert.NotNull(result);
        Assert.Equal(101, result.UserId);
        Assert.Equal(email, result.Email);
        Assert.Equal("Le Van A", result.DisplayName);
        Assert.Equal("0901234567", result.PhoneNumber);
        Assert.Equal(UserStatusConstants.Active, result.Status);
        Assert.Contains(RoleConstants.StorageCustomer, result.Roles);
        Assert.Equal("customer", result.UserType);

        // Assert: Updated last login at
        _userRepoMock.Verify(r => r.UpdateLastLoginAtAsync(101, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);

        // Assert: Recorded succeeded login history once
        _loginHistoryRepoMock.Verify(h => h.AddAsync(
            It.Is<login_history>(lh => lh.result == LoginHistoryResultConstants.Succeeded && lh.user_id == 101 && lh.attempted_email == email),
            It.IsAny<CancellationToken>()), Times.Once);
        _loginHistoryRepoMock.Verify(h => h.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_WithNonExistentEmail_ShouldThrowUnauthorizedAndRecordFailedHistory()
    {
        // Arrange
        var email = "nonexistent@example.test";
        _userRepoMock.Setup(r => r.GetIdentitySnapshotByEmailAsync(email, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentitySnapshotKhoiVTH?)null);
        _userRepoMock.Setup(r => r.GetByEmailWithRolesAndProfilesAsNoTrackingAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((user?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _identityService.AuthenticateAsync(email, "AnyPassword"));

        Assert.Equal("Invalid email or password.", ex.Message);

        _loginHistoryRepoMock.Verify(h => h.AddAsync(
            It.Is<login_history>(lh => lh.result == LoginHistoryResultConstants.Failed && lh.attempted_email == email && lh.user_id == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_WithWrongPassword_ShouldThrowUnauthorizedAndRecordFailedHistory()
    {
        // Arrange
        var email = "user@example.test";
        var snapshot = new UserIdentitySnapshotKhoiVTH
        {
            UserId = 200,
            Email = email,
            PasswordHash = "correct_hash",
            Status = UserStatusConstants.Active,
            Roles = new List<string> { RoleConstants.StorageCustomer }
        };

        _userRepoMock.Setup(r => r.GetIdentitySnapshotByEmailAsync(email, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        _passwordHasherMock.Setup(h => h.VerifyPassword("correct_hash", "WrongPassword"))
            .Returns(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _identityService.AuthenticateAsync(email, "WrongPassword"));

        Assert.Equal("Invalid email or password.", ex.Message);

        _loginHistoryRepoMock.Verify(h => h.AddAsync(
            It.Is<login_history>(lh => lh.result == LoginHistoryResultConstants.Failed && lh.user_id == 200),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_WithLockedAccount_ShouldThrowUnauthorizedAndRecordLockedHistory()
    {
        // Arrange
        var email = "locked@example.test";
        var snapshot = new UserIdentitySnapshotKhoiVTH
        {
            UserId = 201,
            Email = email,
            PasswordHash = "hash",
            Status = UserStatusConstants.Locked,
            Roles = new List<string> { RoleConstants.StorageCustomer }
        };

        _userRepoMock.Setup(r => r.GetIdentitySnapshotByEmailAsync(email, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _identityService.AuthenticateAsync(email, "AnyPassword"));

        Assert.Equal("Invalid email or password.", ex.Message);

        _loginHistoryRepoMock.Verify(h => h.AddAsync(
            It.Is<login_history>(lh => lh.result == LoginHistoryResultConstants.Locked && lh.user_id == 201),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_WithDisabledAccount_ShouldThrowUnauthorizedAndRecordBlockedHistory()
    {
        // Arrange
        var email = "disabled@example.test";
        var snapshot = new UserIdentitySnapshotKhoiVTH
        {
            UserId = 202,
            Email = email,
            PasswordHash = "hash",
            Status = UserStatusConstants.Disabled,
            Roles = new List<string> { RoleConstants.StorageCustomer }
        };

        _userRepoMock.Setup(r => r.GetIdentitySnapshotByEmailAsync(email, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _identityService.AuthenticateAsync(email, "AnyPassword"));

        Assert.Equal("Invalid email or password.", ex.Message);

        _loginHistoryRepoMock.Verify(h => h.AddAsync(
            It.Is<login_history>(lh => lh.result == LoginHistoryResultConstants.Blocked && lh.user_id == 202),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("terminated")]
    public async Task AuthenticateAsync_WithInactiveEmploymentStatus_ShouldThrowUnauthorizedAndRecordBlockedHistory(string employmentStatus)
    {
        // Arrange
        var email = "inactive_emp_login@example.test";
        var snapshot = new UserIdentitySnapshotKhoiVTH
        {
            UserId = 205,
            Email = email,
            PasswordHash = "hash",
            Status = UserStatusConstants.Active,
            EmployeeCode = "EMP205",
            EmploymentStatus = employmentStatus,
            Roles = new List<string> { RoleConstants.FacilityStaff }
        };

        _userRepoMock.Setup(r => r.GetIdentitySnapshotByEmailAsync(email, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _identityService.AuthenticateAsync(email, "AnyPassword"));

        Assert.Equal("Invalid email or password.", ex.Message);

        _loginHistoryRepoMock.Verify(h => h.AddAsync(
            It.Is<login_history>(lh => lh.result == LoginHistoryResultConstants.Blocked && lh.user_id == 205),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthServiceAdapter_LoginAsync_CallsAccountIdentity_AndDoesNotRecordHistoryTwice()
    {
        // Arrange
        var email = "staff@example.test";
        var password = "StaffPassword123";
        var snapshot = new UserIdentitySnapshotKhoiVTH
        {
            UserId = 300,
            Email = email,
            PasswordHash = "staff_hash",
            Status = UserStatusConstants.Active,
            EmployeeFullName = "Staff Member",
            EmploymentStatus = EmploymentStatusConstants.Active,
            Roles = new List<string> { RoleConstants.FacilityStaff }
        };

        _userRepoMock.Setup(r => r.GetIdentitySnapshotByEmailAsync(email, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        _passwordHasherMock.Setup(h => h.VerifyPassword("staff_hash", password))
            .Returns(true);

        var jwtMock = new Mock<IJwtTokenGenerator>();
        jwtMock.Setup(j => j.GenerateToken(300, email, It.IsAny<IEnumerable<string>>()))
            .Returns(("jwt_sample_token", DateTimeOffset.UtcNow.AddMinutes(60)));

        var authService = new AuthServiceKhoiVTH(_identityService, jwtMock.Object);

        // Act
        var loginResponse = await authService.LoginAsync(new LoginRequest { Email = email, Password = password });

        // Assert
        Assert.NotNull(loginResponse);
        Assert.Equal("jwt_sample_token", loginResponse.AccessToken);
        Assert.Equal(300, loginResponse.UserId);
        Assert.Equal("Staff Member", loginResponse.DisplayName);

        // Crucial invariant: login_history was recorded exactly ONCE across the entire call stack!
        _loginHistoryRepoMock.Verify(h => h.AddAsync(
            It.Is<login_history>(lh => lh.result == LoginHistoryResultConstants.Succeeded && lh.user_id == 300),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithInactiveOrMissingUser_ShouldReturnNull()
    {
        // Arrange
        _userRepoMock.Setup(r => r.GetIdentitySnapshotByIdAsync(999, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentitySnapshotKhoiVTH?)null);
        _userRepoMock.Setup(r => r.GetByIdWithRolesAndProfilesAsNoTrackingAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((user?)null);

        // Act
        var result = await _identityService.GetCurrentUserAsync(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithActiveUser_ShouldReturnCurrentUserResponseWithActiveAssignments()
    {
        // Arrange
        var snapshot = new UserIdentitySnapshotKhoiVTH
        {
            UserId = 400,
            Email = "manager@example.test",
            PhoneNumber = "0987654321",
            Status = UserStatusConstants.Active,
            EmployeeFullName = "Manager Le",
            EmployeeCode = "EMP001",
            EmploymentStatus = "active",
            Roles = new List<string> { RoleConstants.FacilityManager },
            ActiveAssignments = new List<StaffAssignmentSnapshotKhoiVTH>
            {
                new()
                {
                    Id = 1,
                    EmployeeId = 400,
                    FacilityId = 10,
                    FacilityCode = "FAC01",
                    FacilityName = "Facility North",
                    AssignmentRole = RoleConstants.FacilityManager,
                    StartsAt = DateTimeOffset.UtcNow.AddDays(-10)
                }
            }
        };

        _userRepoMock.Setup(r => r.GetIdentitySnapshotByIdAsync(400, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var currentUser = await _identityService.GetCurrentUserAsync(400);

        // Assert
        Assert.NotNull(currentUser);
        Assert.Equal(400, currentUser.UserId);
        Assert.Equal("Manager Le", currentUser.DisplayName);
        Assert.Equal("employee", currentUser.UserType);
        Assert.Equal("EMP001", currentUser.EmployeeCode);
        Assert.Contains(RoleConstants.FacilityManager, currentUser.Roles);
        Assert.Single(currentUser.ActiveAssignments);
        Assert.Equal(10, currentUser.ActiveAssignments[0].FacilityId);
        Assert.Equal("FAC01", currentUser.ActiveAssignments[0].FacilityCode);
    }

    [Fact]
    public async Task SnapshotQuery_ReflectsFreshDbChanges_WithoutRelyingOnTrackedEntity()
    {
        // Verify real EF Core UserRepositoryKhoiVTH behavior with In-Memory DbContext
        var dbOptions = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase("IdentitySnapshotFreshnessTest_" + Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SelfStorageDbContext(dbOptions);

        var adminRole = new role { id = 5, code = RoleConstants.SystemAdministrator, display_name = "Admin" };
        var managerRole = new role { id = 3, code = RoleConstants.FacilityManager, display_name = "Manager" };
        var facility = new facility { id = 1, code = "F01", name = "Facility 1", address_line = "Addr 1", city = "Hanoi", timezone = "SE Asia Standard Time", status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow };
        db.roles.AddRange(adminRole, managerRole);
        db.facilities.Add(facility);

        var testUser = new user
        {
            id = 500,
            email = "snapshot@example.test",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(testUser);
        db.user_roles.Add(new user_role { user_id = 500, role_id = 5, role = adminRole, granted_at = DateTimeOffset.UtcNow });
        db.employee_profiles.Add(new employee_profile { user_id = 500, employee_code = "E500", full_name = "Snapshot User", employment_status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var repo = new UserRepositoryKhoiVTH(db);

        // Step 1: Read initial snapshot
        var initialSnapshot = await repo.GetIdentitySnapshotByIdAsync(500);
        Assert.NotNull(initialSnapshot);
        Assert.Contains(RoleConstants.SystemAdministrator, initialSnapshot.Roles);
        Assert.DoesNotContain(RoleConstants.FacilityManager, initialSnapshot.Roles);
        Assert.Empty(initialSnapshot.ActiveAssignments);

        // Step 2: Simulate direct DB change (e.g. role revoked and reassigned, assignment added, status changed)
        // We modify DB state directly
        var existingRole = await db.user_roles.FirstAsync(ur => ur.user_id == 500 && ur.role_id == 5);
        db.user_roles.Remove(existingRole);
        db.user_roles.Add(new user_role { user_id = 500, role_id = 3, role = managerRole, granted_at = DateTimeOffset.UtcNow });

        db.staff_facility_assignments.Add(new staff_facility_assignment
        {
            id = 501,
            employee_id = 500,
            facility_id = 1,
            assignment_role = RoleConstants.FacilityManager,
            starts_at = DateTimeOffset.UtcNow.AddHours(-1),
            created_at = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // Step 3: Query snapshot again - MUST reflect new role and assignment without being shadowed by EF tracking
        var updatedSnapshot = await repo.GetIdentitySnapshotByIdAsync(500);
        Assert.NotNull(updatedSnapshot);
        Assert.DoesNotContain(RoleConstants.SystemAdministrator, updatedSnapshot.Roles);
        Assert.Contains(RoleConstants.FacilityManager, updatedSnapshot.Roles);
        Assert.Single(updatedSnapshot.ActiveAssignments);
        Assert.Equal(1, updatedSnapshot.ActiveAssignments[0].FacilityId);
        Assert.Equal("F01", updatedSnapshot.ActiveAssignments[0].FacilityCode);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ActiveManager_ReceivesBothManagerAndStaffAssignments()
    {
        // Arrange
        var snapshot = new UserIdentitySnapshotKhoiVTH
        {
            UserId = 401,
            Email = "manager@example.test",
            Status = UserStatusConstants.Active,
            EmployeeFullName = "Manager Le",
            EmployeeCode = "EMP001",
            EmploymentStatus = EmploymentStatusConstants.Active,
            Roles = new List<string> { RoleConstants.FacilityManager },
            ActiveAssignments = new List<StaffAssignmentSnapshotKhoiVTH>
            {
                new()
                {
                    Id = 1,
                    EmployeeId = 401,
                    FacilityId = 10,
                    FacilityCode = "FAC01",
                    FacilityName = "Facility 1",
                    AssignmentRole = RoleConstants.FacilityManager,
                    StartsAt = DateTimeOffset.UtcNow.AddDays(-5)
                },
                new()
                {
                    Id = 2,
                    EmployeeId = 401,
                    FacilityId = 20,
                    FacilityCode = "FAC02",
                    FacilityName = "Facility 2",
                    AssignmentRole = RoleConstants.FacilityStaff,
                    StartsAt = DateTimeOffset.UtcNow.AddDays(-5)
                }
            }
        };

        _userRepoMock.Setup(r => r.GetIdentitySnapshotByIdAsync(401, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var result = await _identityService.GetCurrentUserAsync(401);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.ActiveAssignments.Count);
        Assert.Contains(result.ActiveAssignments, a => a.AssignmentRole == RoleConstants.FacilityManager);
        Assert.Contains(result.ActiveAssignments, a => a.AssignmentRole == RoleConstants.FacilityStaff);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ActiveStaff_DoesNotReceiveManagerAssignments()
    {
        // Arrange
        var snapshot = new UserIdentitySnapshotKhoiVTH
        {
            UserId = 402,
            Email = "staff@example.test",
            Status = UserStatusConstants.Active,
            EmployeeFullName = "Staff Tran",
            EmployeeCode = "EMP002",
            EmploymentStatus = EmploymentStatusConstants.Active,
            Roles = new List<string> { RoleConstants.FacilityStaff },
            ActiveAssignments = new List<StaffAssignmentSnapshotKhoiVTH>
            {
                new()
                {
                    Id = 1,
                    EmployeeId = 402,
                    FacilityId = 10,
                    FacilityCode = "FAC01",
                    FacilityName = "Facility 1",
                    AssignmentRole = RoleConstants.FacilityManager,
                    StartsAt = DateTimeOffset.UtcNow.AddDays(-5)
                },
                new()
                {
                    Id = 2,
                    EmployeeId = 402,
                    FacilityId = 20,
                    FacilityCode = "FAC02",
                    FacilityName = "Facility 2",
                    AssignmentRole = RoleConstants.FacilityStaff,
                    StartsAt = DateTimeOffset.UtcNow.AddDays(-5)
                }
            }
        };

        _userRepoMock.Setup(r => r.GetIdentitySnapshotByIdAsync(402, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var result = await _identityService.GetCurrentUserAsync(402);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.ActiveAssignments);
        Assert.Equal(RoleConstants.FacilityStaff, result.ActiveAssignments[0].AssignmentRole);
        Assert.Equal(20, result.ActiveAssignments[0].FacilityId);
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("terminated")]
    public async Task GetCurrentUserAsync_EmploymentStatusLeaveOrTerminated_ReturnsNull(string employmentStatus)
    {
        // Arrange
        var snapshot = new UserIdentitySnapshotKhoiVTH
        {
            UserId = 403,
            Email = "inactive_emp@example.test",
            Status = UserStatusConstants.Active,
            EmployeeFullName = "Inactive Emp",
            EmployeeCode = "EMP003",
            EmploymentStatus = employmentStatus,
            Roles = new List<string> { RoleConstants.FacilityManager },
            ActiveAssignments = new List<StaffAssignmentSnapshotKhoiVTH>
            {
                new()
                {
                    Id = 1,
                    EmployeeId = 403,
                    FacilityId = 10,
                    FacilityCode = "FAC01",
                    FacilityName = "Facility 1",
                    AssignmentRole = RoleConstants.FacilityManager,
                    StartsAt = DateTimeOffset.UtcNow.AddDays(-5)
                }
            }
        };

        _userRepoMock.Setup(r => r.GetIdentitySnapshotByIdAsync(403, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var result = await _identityService.GetCurrentUserAsync(403);

        // Assert: Inactive employee receives null and loses all role access
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentUserAsync_RevokedStaffOrManagerRole_ReturnsEmptyAssignments()
    {
        // Arrange: User has active employee profile and assignment in DB, but roles contain neither manager nor staff
        var snapshot = new UserIdentitySnapshotKhoiVTH
        {
            UserId = 404,
            Email = "customer_now@example.test",
            Status = UserStatusConstants.Active,
            EmployeeFullName = "Former Staff",
            EmployeeCode = "EMP004",
            EmploymentStatus = EmploymentStatusConstants.Active,
            Roles = new List<string> { RoleConstants.StorageCustomer }, // Roles revoked!
            ActiveAssignments = new List<StaffAssignmentSnapshotKhoiVTH>
            {
                new()
                {
                    Id = 1,
                    EmployeeId = 404,
                    FacilityId = 10,
                    FacilityCode = "FAC01",
                    FacilityName = "Facility 1",
                    AssignmentRole = RoleConstants.FacilityStaff,
                    StartsAt = DateTimeOffset.UtcNow.AddDays(-5)
                }
            }
        };

        _userRepoMock.Setup(r => r.GetIdentitySnapshotByIdAsync(404, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var result = await _identityService.GetCurrentUserAsync(404);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.ActiveAssignments);
    }

    [Fact]
    public async Task SnapshotQuery_EnforcesRoleAndEmploymentStatusRules_DirectlyFromDb()
    {
        var dbOptions = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase("IdentityScopeRulesTest_" + Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SelfStorageDbContext(dbOptions);

        var managerRole = new role { id = 3, code = RoleConstants.FacilityManager, display_name = "Manager" };
        var staffRole = new role { id = 4, code = RoleConstants.FacilityStaff, display_name = "Staff" };
        var customerRole = new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" };
        var facility1 = new facility { id = 10, code = "F10", name = "Facility 10", address_line = "Addr 10", city = "Hanoi", timezone = "SE Asia Standard Time", status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow };
        var facility2 = new facility { id = 20, code = "F20", name = "Facility 20", address_line = "Addr 20", city = "Danang", timezone = "SE Asia Standard Time", status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow };

        db.roles.AddRange(managerRole, staffRole, customerRole);
        db.facilities.AddRange(facility1, facility2);

        // User A: Active Manager with both Manager and Staff assignments
        var userA = new user { id = 601, email = "userA@example.test", password_hash = "hash", status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow };
        db.users.Add(userA);
        db.user_roles.Add(new user_role { user_id = 601, role_id = 3, role = managerRole, granted_at = DateTimeOffset.UtcNow });
        db.employee_profiles.Add(new employee_profile { user_id = 601, employee_code = "EA", full_name = "User A", employment_status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });
        db.staff_facility_assignments.AddRange(
            new staff_facility_assignment { id = 701, employee_id = 601, facility_id = 10, assignment_role = RoleConstants.FacilityManager, starts_at = DateTimeOffset.UtcNow.AddDays(-1), created_at = DateTimeOffset.UtcNow },
            new staff_facility_assignment { id = 702, employee_id = 601, facility_id = 20, assignment_role = RoleConstants.FacilityStaff, starts_at = DateTimeOffset.UtcNow.AddDays(-1), created_at = DateTimeOffset.UtcNow }
        );

        // User B: Active Staff with both Manager and Staff assignments in DB
        var userB = new user { id = 602, email = "userB@example.test", password_hash = "hash", status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow };
        db.users.Add(userB);
        db.user_roles.Add(new user_role { user_id = 602, role_id = 4, role = staffRole, granted_at = DateTimeOffset.UtcNow });
        db.employee_profiles.Add(new employee_profile { user_id = 602, employee_code = "EB", full_name = "User B", employment_status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });
        db.staff_facility_assignments.AddRange(
            new staff_facility_assignment { id = 703, employee_id = 602, facility_id = 10, assignment_role = RoleConstants.FacilityManager, starts_at = DateTimeOffset.UtcNow.AddDays(-1), created_at = DateTimeOffset.UtcNow },
            new staff_facility_assignment { id = 704, employee_id = 602, facility_id = 20, assignment_role = RoleConstants.FacilityStaff, starts_at = DateTimeOffset.UtcNow.AddDays(-1), created_at = DateTimeOffset.UtcNow }
        );

        // User C: Manager role, but employment_status is "leave"
        var userC = new user { id = 603, email = "userC@example.test", password_hash = "hash", status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow };
        db.users.Add(userC);
        db.user_roles.Add(new user_role { user_id = 603, role_id = 3, role = managerRole, granted_at = DateTimeOffset.UtcNow });
        db.employee_profiles.Add(new employee_profile { user_id = 603, employee_code = "EC", full_name = "User C", employment_status = "leave", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });
        db.staff_facility_assignments.Add(new staff_facility_assignment { id = 705, employee_id = 603, facility_id = 10, assignment_role = RoleConstants.FacilityManager, starts_at = DateTimeOffset.UtcNow.AddDays(-1), created_at = DateTimeOffset.UtcNow });

        // User D: Manager role, but employment_status is "terminated"
        var userD = new user { id = 604, email = "userD@example.test", password_hash = "hash", status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow };
        db.users.Add(userD);
        db.user_roles.Add(new user_role { user_id = 604, role_id = 3, role = managerRole, granted_at = DateTimeOffset.UtcNow });
        db.employee_profiles.Add(new employee_profile { user_id = 604, employee_code = "ED", full_name = "User D", employment_status = "terminated", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });
        db.staff_facility_assignments.Add(new staff_facility_assignment { id = 706, employee_id = 604, facility_id = 10, assignment_role = RoleConstants.FacilityManager, starts_at = DateTimeOffset.UtcNow.AddDays(-1), created_at = DateTimeOffset.UtcNow });

        await db.SaveChangesAsync();

        var repo = new UserRepositoryKhoiVTH(db);

        // Verify User A: Active Manager receives both assignments
        var snapA = await repo.GetIdentitySnapshotByIdAsync(601);
        Assert.NotNull(snapA);
        Assert.Equal(2, snapA.ActiveAssignments.Count);

        // Verify User B: Active Staff only receives Staff assignment (Manager assignment filtered out)
        var snapB = await repo.GetIdentitySnapshotByIdAsync(602);
        Assert.NotNull(snapB);
        Assert.Single(snapB.ActiveAssignments);
        Assert.Equal(RoleConstants.FacilityStaff, snapB.ActiveAssignments[0].AssignmentRole);
        Assert.Equal(20, snapB.ActiveAssignments[0].FacilityId);

        // Verify User C: Leave status receives empty assignments
        var snapC = await repo.GetIdentitySnapshotByIdAsync(603);
        Assert.NotNull(snapC);
        Assert.Empty(snapC.ActiveAssignments);

        // Verify User D: Terminated status receives empty assignments
        var snapD = await repo.GetIdentitySnapshotByIdAsync(604);
        Assert.NotNull(snapD);
        Assert.Empty(snapD.ActiveAssignments);

        // Verify User A role revoked in DB: change role to customer
        var roleA = await db.user_roles.FirstAsync(ur => ur.user_id == 601);
        db.user_roles.Remove(roleA);
        db.user_roles.Add(new user_role { user_id = 601, role_id = 1, role = customerRole, granted_at = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // Fresh snapshot MUST reflect empty assignments immediately
        var snapARevoked = await repo.GetIdentitySnapshotByIdAsync(601);
        Assert.NotNull(snapARevoked);
        Assert.Empty(snapARevoked.ActiveAssignments);
    }
}
