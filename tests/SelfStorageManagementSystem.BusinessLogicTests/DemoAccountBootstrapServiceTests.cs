using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class DemoAccountBootstrapServiceTests
{
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<ILogger<DemoAccountBootstrapService>> _loggerMock = new();

    private SelfStorageDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SelfStorageDbContext(options);
    }

    private void SeedAdminUser(SelfStorageDbContext context, string email, bool withAdminRole = true)
    {
        var adminRole = context.roles.FirstOrDefault(r => r.code == RoleConstants.SystemAdministrator);
        if (adminRole == null)
        {
            adminRole = new role
            {
                id = 1,
                code = RoleConstants.SystemAdministrator,
                display_name = "System Administrator"
            };
            context.roles.Add(adminRole);
        }

        var user = new user
        {
            id = 1,
            email = email,
            password_hash = "external-auth-demo-only",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.users.Add(user);

        if (withAdminRole)
        {
            context.user_roles.Add(new user_role
            {
                user_id = user.id,
                role_id = adminRole.id,
                role = adminRole,
                user = user
            });
        }

        context.SaveChanges();
    }

    [Fact]
    public async Task BootstrapDemoAccountsAsync_WhenAdminUserNotFound_ShouldThrowInvalidOperationException()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "AdminAccount:Email", "admin@selfstorage.local" },
            { "AdminAccount:Password", "SecretAdminPassword123!" }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var service = new DemoAccountBootstrapService(
            context,
            _passwordHasherMock.Object,
            configuration,
            _loggerMock.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BootstrapDemoAccountsAsync());

        Assert.Contains("does not exist in the database", ex.Message);
    }

    [Fact]
    public async Task BootstrapDemoAccountsAsync_WhenUserDoesNotHaveAdminRole_ShouldThrowInvalidOperationException()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        SeedAdminUser(context, "admin@selfstorage.local", withAdminRole: false);

        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "AdminAccount:Email", "admin@selfstorage.local" },
            { "AdminAccount:Password", "SecretAdminPassword123!" }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var service = new DemoAccountBootstrapService(
            context,
            _passwordHasherMock.Object,
            configuration,
            _loggerMock.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BootstrapDemoAccountsAsync());

        Assert.Contains("does not have the required role", ex.Message);
        Assert.Contains(RoleConstants.SystemAdministrator, ex.Message);
    }

    [Fact]
    public async Task BootstrapDemoAccountsAsync_WhenPasswordNotConfigured_ShouldThrowInvalidOperationException()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        SeedAdminUser(context, "admin@selfstorage.local", withAdminRole: true);

        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "AdminAccount:Email", "admin@selfstorage.local" }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var service = new DemoAccountBootstrapService(
            context,
            _passwordHasherMock.Object,
            configuration,
            _loggerMock.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BootstrapDemoAccountsAsync());

        Assert.Contains("AdminAccount:Password", ex.Message);
    }

    [Fact]
    public async Task BootstrapDemoAccountsAsync_WhenAdminAndDemoConfigured_ShouldVerifyRoleAndUpdateHashes()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        SeedAdminUser(context, "admin@selfstorage.local", withAdminRole: true);

        context.users.Add(new user
        {
            id = 2,
            email = "demo@example.test",
            password_hash = "external-auth-demo-only",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        _passwordHasherMock.Setup(h => h.HashPassword("AdminSecretPassword!"))
            .Returns("hashed_admin_pw");
        _passwordHasherMock.Setup(h => h.HashPassword("DemoSecretPassword!"))
            .Returns("hashed_demo_pw");

        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "AdminAccount:Email", "admin@selfstorage.local" },
            { "AdminAccount:Password", "AdminSecretPassword!" },
            { "DemoAccounts:DefaultPassword", "DemoSecretPassword!" }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var service = new DemoAccountBootstrapService(
            context,
            _passwordHasherMock.Object,
            configuration,
            _loggerMock.Object);

        await service.BootstrapDemoAccountsAsync();

        var adminUser = await context.users.FindAsync((long)1);
        var demoUser = await context.users.FindAsync((long)2);

        Assert.NotNull(adminUser);
        Assert.Equal("hashed_admin_pw", adminUser.password_hash);

        Assert.NotNull(demoUser);
        Assert.Equal("hashed_demo_pw", demoUser.password_hash);
    }

    [Fact]
    public async Task BootstrapDemoAccountsAsync_WhenManagerD1HasLegacyHash_ShouldUpdateHashAndCreateEmployeeProfileAndAssignment()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        SeedAdminUser(context, "admin@selfstorage.local", withAdminRole: true);

        context.facilities.Add(new facility
        {
            id = 1,
            code = "FAC-01",
            name = "Facility 01",
            address_line = "123 Main St",
            city = "HCM",
            district = "D1",
            timezone = "Asia/Ho_Chi_Minh",
            status = "active",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });

        context.users.Add(new user
        {
            id = 10002,
            email = "manager.d1@selfstorage.local",
            password_hash = "PH:v1:100000:32:00000000000000000000000000000000:0000000000000000000000000000000000000000000000000000000000000000",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        _passwordHasherMock.Setup(h => h.HashPassword("SecretAdminPassword123!"))
            .Returns("hashed_secret_pw");

        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "AdminAccount:Email", "admin@selfstorage.local" },
            { "AdminAccount:Password", "SecretAdminPassword123!" }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var service = new DemoAccountBootstrapService(
            context,
            _passwordHasherMock.Object,
            configuration,
            _loggerMock.Object);

        await service.BootstrapDemoAccountsAsync();

        var managerUser = await context.users.FindAsync((long)10002);
        Assert.NotNull(managerUser);
        Assert.Equal("hashed_secret_pw", managerUser.password_hash);

        var empProfile = await context.employee_profiles.FindAsync((long)10002);
        Assert.NotNull(empProfile);
        Assert.Equal(EmploymentStatusConstants.Active, empProfile.employment_status);

        var assignment = await context.staff_facility_assignments.FirstOrDefaultAsync(a => a.employee_id == 10002);
        Assert.NotNull(assignment);
        Assert.Equal(RoleConstants.FacilityManager, assignment.assignment_role);
    }
}