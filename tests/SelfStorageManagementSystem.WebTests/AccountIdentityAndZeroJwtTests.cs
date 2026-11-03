using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.WebTests.Fixtures;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

public class AccountIdentityAndZeroJwtTests : IClassFixture<WebTestFixture>
{
    private readonly WebTestFixture _factory;

    public AccountIdentityAndZeroJwtTests(WebTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public void WebComposition_RegistersAccountIdentityService_AndCanBeResolved()
    {
        using var scope = _factory.Services.CreateScope();
        var identityService = scope.ServiceProvider.GetService<IAccountIdentityService>();
        Assert.NotNull(identityService);
    }

    [Fact]
    public void WebComposition_DoesNotRegisterJwtTokenGenerator_ResolvingFails()
    {
        using var scope = _factory.Services.CreateScope();
        
        // IJwtTokenGenerator MUST NOT be registered in Web DI container
        var jwtGenerator = scope.ServiceProvider.GetService<IJwtTokenGenerator>();
        Assert.Null(jwtGenerator);

        // Required service resolution must throw InvalidOperationException
        Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>());
    }

    [Fact]
    public void WebComposition_DoesNotRegisterIAuthService_ResolvingFails()
    {
        using var scope = _factory.Services.CreateScope();

        // IAuthService (which depends on JWT token generation) MUST NOT be registered in Web DI container
        var authService = scope.ServiceProvider.GetService<IAuthService>();
        Assert.Null(authService);

        // Required service resolution must throw InvalidOperationException
        Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IAuthService>());
    }

    [Fact]
    public async Task WebHost_OperatesAndAuthenticatesWithoutJwtSecret()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var identityService = scope.ServiceProvider.GetRequiredService<IAccountIdentityService>();

        // Ensure roles exist
        var testRole = await db.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.StorageCustomer);
        if (testRole == null)
        {
            testRole = new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" };
            db.roles.Add(testRole);
            await db.SaveChangesAsync();
        }

        var uniqueEmail = $"webuser_{Guid.NewGuid():N}@example.test";
        var rawPassword = "WebSecretPassword2026!";

        var testUser = new user
        {
            email = uniqueEmail,
            password_hash = hasher.HashPassword(rawPassword),
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(testUser);
        await db.SaveChangesAsync();

        db.user_roles.Add(new user_role
        {
            user_id = testUser.id,
            role_id = testRole.id,
            granted_at = DateTimeOffset.UtcNow
        });
        db.customer_profiles.Add(new customer_profile
        {
            user_id = testUser.id,
            full_name = "Web Cookie Customer",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // Act: Authenticate through IAccountIdentityService without ANY JWT token or secret
        var authResult = await identityService.AuthenticateAsync(uniqueEmail, rawPassword);

        // Assert: Succeeded, valid identity, no token or hash
        Assert.NotNull(authResult);
        Assert.Equal(testUser.id, authResult.UserId);
        Assert.Equal(uniqueEmail, authResult.Email);
        Assert.Equal("Web Cookie Customer", authResult.DisplayName);
        Assert.Contains(RoleConstants.StorageCustomer, authResult.Roles);
        Assert.NotNull(authResult.LastLoginAt);

        // Ensure login history was recorded in database
        var history = await db.login_histories
            .OrderByDescending(h => h.occurred_at)
            .FirstOrDefaultAsync(h => h.user_id == testUser.id);
        Assert.NotNull(history);
        Assert.Equal(LoginHistoryResultConstants.Succeeded, history.result);
    }

    [Fact]
    public async Task IdentitySnapshot_ReflectsFreshDbRoleChanges_Immediately()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var identityService = scope.ServiceProvider.GetRequiredService<IAccountIdentityService>();

        // Ensure roles exist
        var customerRole = await db.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.StorageCustomer);
        if (customerRole == null)
        {
            customerRole = new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" };
            db.roles.Add(customerRole);
            await db.SaveChangesAsync();
        }

        var staffRole = await db.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.FacilityStaff);
        if (staffRole == null)
        {
            staffRole = new role { id = 2, code = RoleConstants.FacilityStaff, display_name = "Staff" };
            db.roles.Add(staffRole);
            await db.SaveChangesAsync();
        }

        var email = $"roleshift_{Guid.NewGuid():N}@example.test";
        var user = new user
        {
            email = email,
            password_hash = hasher.HashPassword("TestPass123!"),
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(user);
        await db.SaveChangesAsync();

        db.user_roles.Add(new user_role { user_id = user.id, role_id = customerRole.id, granted_at = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // Check snapshot 1: Customer role
        var initialUser = await identityService.GetCurrentUserAsync(user.id);
        Assert.NotNull(initialUser);
        Assert.Contains(RoleConstants.StorageCustomer, initialUser.Roles);
        Assert.DoesNotContain(RoleConstants.FacilityStaff, initialUser.Roles);

        // Modify DB directly (simulate admin role upgrade)
        var ur = await db.user_roles.FirstAsync(r => r.user_id == user.id && r.role_id == customerRole.id);
        db.user_roles.Remove(ur);
        db.user_roles.Add(new user_role { user_id = user.id, role_id = staffRole.id, granted_at = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        // Check snapshot 2: MUST reflect new role immediately from DB
        var updatedUser = await identityService.GetCurrentUserAsync(user.id);
        Assert.NotNull(updatedUser);
        Assert.DoesNotContain(RoleConstants.StorageCustomer, updatedUser.Roles);
        Assert.Contains(RoleConstants.FacilityStaff, updatedUser.Roles);
    }

    [Fact]
    public async Task IdentitySnapshot_FiltersFacilityAssignments_ByEmploymentStatusAndRoles_OnWebHost()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var identityService = scope.ServiceProvider.GetRequiredService<IAccountIdentityService>();

        var managerRole = await db.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.FacilityManager);
        if (managerRole == null)
        {
            managerRole = new role { id = 11, code = RoleConstants.FacilityManager, display_name = "Manager" };
            db.roles.Add(managerRole);
            await db.SaveChangesAsync();
        }

        var staffRole = await db.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.FacilityStaff);
        if (staffRole == null)
        {
            staffRole = new role { id = 12, code = RoleConstants.FacilityStaff, display_name = "Staff" };
            db.roles.Add(staffRole);
            await db.SaveChangesAsync();
        }

        var customerRole = await db.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.StorageCustomer);
        if (customerRole == null)
        {
            customerRole = new role { id = 13, code = RoleConstants.StorageCustomer, display_name = "Customer" };
            db.roles.Add(customerRole);
            await db.SaveChangesAsync();
        }

        var fac1 = await db.facilities.FirstOrDefaultAsync(f => f.code == "WH_FAC1");
        if (fac1 == null)
        {
            fac1 = new facility { id = 101, code = "WH_FAC1", name = "WebFac1", address_line = "Addr 1", city = "Hanoi", timezone = "SE Asia Standard Time", status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow };
            db.facilities.Add(fac1);
            await db.SaveChangesAsync();
        }

        var fac2 = await db.facilities.FirstOrDefaultAsync(f => f.code == "WH_FAC2");
        if (fac2 == null)
        {
            fac2 = new facility { id = 102, code = "WH_FAC2", name = "WebFac2", address_line = "Addr 2", city = "Danang", timezone = "SE Asia Standard Time", status = "active", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow };
            db.facilities.Add(fac2);
            await db.SaveChangesAsync();
        }

        var email = $"emp_scope_{Guid.NewGuid():N}@example.test";
        var user = new user
        {
            email = email,
            password_hash = hasher.HashPassword("TestPass123!"),
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(user);
        await db.SaveChangesAsync();

        var empProfile = new employee_profile
        {
            user_id = user.id,
            employee_code = $"EC_{user.id}",
            full_name = "Scope Employee",
            employment_status = "active",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.employee_profiles.Add(empProfile);

        db.user_roles.Add(new user_role { user_id = user.id, role_id = managerRole.id, granted_at = DateTimeOffset.UtcNow });

        db.staff_facility_assignments.AddRange(
            new staff_facility_assignment
            {
                employee_id = user.id,
                facility_id = fac1.id,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-1),
                created_at = DateTimeOffset.UtcNow
            },
            new staff_facility_assignment
            {
                employee_id = user.id,
                facility_id = fac2.id,
                assignment_role = RoleConstants.FacilityStaff,
                starts_at = DateTimeOffset.UtcNow.AddDays(-1),
                created_at = DateTimeOffset.UtcNow
            }
        );
        await db.SaveChangesAsync();

        // 1. As Active Manager: receives both assignments
        var userState1 = await identityService.GetCurrentUserAsync(user.id);
        Assert.NotNull(userState1);
        Assert.Equal(2, userState1.ActiveAssignments.Count);

        // 2. Change role in DB to Staff only
        var currentRole = await db.user_roles.FirstAsync(ur => ur.user_id == user.id);
        db.user_roles.Remove(currentRole);
        db.user_roles.Add(new user_role { user_id = user.id, role_id = staffRole.id, granted_at = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var userState2 = await identityService.GetCurrentUserAsync(user.id);
        Assert.NotNull(userState2);
        Assert.Single(userState2.ActiveAssignments);
        Assert.Equal(RoleConstants.FacilityStaff, userState2.ActiveAssignments[0].AssignmentRole);
        Assert.Equal(fac2.id, userState2.ActiveAssignments[0].FacilityId);

        // 3. Change employment_status to leave: inactive employee is rejected and returns null
        empProfile.employment_status = "leave";
        await db.SaveChangesAsync();

        var userState3 = await identityService.GetCurrentUserAsync(user.id);
        Assert.Null(userState3);

        // 4. Change employment_status back to active, but revoke staff role (assign customer role)
        empProfile.employment_status = "active";
        var staffUr = await db.user_roles.FirstAsync(ur => ur.user_id == user.id);
        db.user_roles.Remove(staffUr);
        db.user_roles.Add(new user_role { user_id = user.id, role_id = customerRole.id, granted_at = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var userState4 = await identityService.GetCurrentUserAsync(user.id);
        Assert.NotNull(userState4);
        Assert.Empty(userState4.ActiveAssignments);
    }
}
