using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "IntegrationTestSecretKeyMustBeLongEnoughToPassValidation2026!";
    public const string TestIssuer = "SelfStoragePRN222";
    public const string TestAudience = "SelfStoragePRN222Clients";

    private readonly string _dbName = "IntegrationTestDb_" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Key", TestJwtKey);
        builder.UseSetting("Jwt:Issuer", TestIssuer);
        builder.UseSetting("Jwt:Audience", TestAudience);
        builder.UseSetting("VnPay:TmnCode", "DEMOTMN01");
        builder.UseSetting("VnPay:HashSecret", "SECRETTESTKEY1234567890ABCDEF12");
        builder.UseSetting("VnPay:BaseUrl", "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html");
        builder.UseSetting("VnPay:ReturnUrl", "http://localhost:5000/api/payments/vnpay/return");
        builder.UseSetting("Payment:AllowDemoSimulator", "true");

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<SelfStorageDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Register InMemory database
            services.AddDbContext<SelfStorageDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });
        });
    }

    public async Task SeedInitialDataAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        if (await db.users.AnyAsync()) return;

        // Roles
        var adminRole = new role { id = 5, code = RoleConstants.SystemAdministrator, display_name = "Admin" };
        var customerRole = new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" };
        db.roles.AddRange(adminRole, customerRole);

        // Admin User
        var adminUser = new user
        {
            id = 1,
            email = "admin@example.test",
            password_hash = hasher.HashPassword("AdminPass123!"),
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(adminUser);
        db.user_roles.Add(new user_role { user_id = 1, role_id = 5, granted_at = DateTimeOffset.UtcNow });

        // Customer User
        var customerUser = new user
        {
            id = 2,
            email = "customer@example.test",
            password_hash = hasher.HashPassword("CustomerPass123!"),
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(customerUser);
        db.user_roles.Add(new user_role { user_id = 2, role_id = 1, granted_at = DateTimeOffset.UtcNow });
        db.customer_profiles.Add(new customer_profile
        {
            user_id = 2,
            full_name = "Customer One",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });

        // Locked User
        var lockedUser = new user
        {
            id = 3,
            email = "locked@example.test",
            password_hash = hasher.HashPassword("LockedPass123!"),
            status = UserStatusConstants.Locked,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(lockedUser);
        db.user_roles.Add(new user_role { user_id = 3, role_id = 1, granted_at = DateTimeOffset.UtcNow });

        // Disabled User
        var disabledUser = new user
        {
            id = 4,
            email = "disabled@example.test",
            password_hash = hasher.HashPassword("DisabledPass123!"),
            status = UserStatusConstants.Disabled,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(disabledUser);
        db.user_roles.Add(new user_role { user_id = 4, role_id = 1, granted_at = DateTimeOffset.UtcNow });

        await db.SaveChangesAsync();
    }
}

public class AuthAndAuthorizationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AuthAndAuthorizationIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private string GenerateTokenForUser(long userId, string email, params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var tokenGen = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var user = new user { id = userId, email = email };
        var (token, _) = tokenGen.GenerateToken(user, roles);
        return token;
    }

    [Fact]
    public void MissingJwtKey_ShouldThrowConfigurationExceptionAndStopStartup()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Key", "");
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("JWT signing key is missing or shorter than", ex.Message);
    }

    [Fact]
    public void ShortJwtKey_ShouldThrowConfigurationExceptionAndStopStartup()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Key", "ShortKey123");
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("JWT signing key is missing or shorter than", ex.Message);
    }

    [Fact]
    public async Task Token_SignedAndVerifiedSuccessfully_ShouldAuthenticateApiRequest()
    {
        await _factory.SeedInitialDataAsync();
        var client = _factory.CreateClient();

        var token = GenerateTokenForUser(1, "admin@example.test", RoleConstants.SystemAdministrator);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<CurrentUserResponse>>(JsonOpts);
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.Equal("admin@example.test", body.Data?.Email);
    }

    [Fact]
    public async Task Login_NonExistentEmail_Returns401AndUniformMessage()
    {
        await _factory.SeedInitialDataAsync();
        var client = _factory.CreateClient();

        var request = new LoginRequest
        {
            Email = "doesnotexist@example.test",
            Password = "AnyPassword123!"
        };

        var response = await client.PostAsJsonAsync("/api/auth/login", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(JsonOpts);
        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.Equal("Invalid email or password.", body.Message);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401AndUniformMessage()
    {
        await _factory.SeedInitialDataAsync();
        var client = _factory.CreateClient();

        var request = new LoginRequest
        {
            Email = "admin@example.test",
            Password = "WrongPassword123!"
        };

        var response = await client.PostAsJsonAsync("/api/auth/login", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(JsonOpts);
        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.Equal("Invalid email or password.", body.Message);
    }

    [Fact]
    public async Task Login_LockedAccount_Returns401AndUniformMessage()
    {
        await _factory.SeedInitialDataAsync();
        var client = _factory.CreateClient();

        var request = new LoginRequest
        {
            Email = "locked@example.test",
            Password = "LockedPass123!"
        };

        var response = await client.PostAsJsonAsync("/api/auth/login", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(JsonOpts);
        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.Equal("Invalid email or password.", body.Message);
    }

    [Fact]
    public async Task Login_DisabledAccount_Returns401AndUniformMessage()
    {
        await _factory.SeedInitialDataAsync();
        var client = _factory.CreateClient();

        var request = new LoginRequest
        {
            Email = "disabled@example.test",
            Password = "DisabledPass123!"
        };

        var response = await client.PostAsJsonAsync("/api/auth/login", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(JsonOpts);
        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.Equal("Invalid email or password.", body.Message);
    }

    [Fact]
    public async Task UnauthenticatedRequest_ToAdminAccounts_ShouldReturn401()
    {
        await _factory.SeedInitialDataAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/admin/accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CustomerToken_ToAdminAccounts_ShouldReturn403()
    {
        await _factory.SeedInitialDataAsync();
        var client = _factory.CreateClient();

        var customerToken = GenerateTokenForUser(2, "customer@example.test", RoleConstants.StorageCustomer);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var response = await client.GetAsync("/api/admin/accounts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminToken_ToAdminAccounts_ShouldReturn200()
    {
        await _factory.SeedInitialDataAsync();
        var client = _factory.CreateClient();

        var adminToken = GenerateTokenForUser(1, "admin@example.test", RoleConstants.SystemAdministrator);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync("/api/admin/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TokenRevalidation_WhenAdminUserBecomesLockedInDatabase_NextRequestWithOldTokenMustReturn401()
    {
        await _factory.SeedInitialDataAsync();
        var client = _factory.CreateClient();

        var adminToken = GenerateTokenForUser(1, "admin@example.test", RoleConstants.SystemAdministrator);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // First request is OK
        var firstResponse = await client.GetAsync("/api/admin/accounts");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Lock user 1 in the database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var user = await db.users.FindAsync((long)1);
            user!.status = UserStatusConstants.Locked;
            await db.SaveChangesAsync();
        }

        // Second request with SAME unexpired token must fail with 401
        var secondResponse = await client.GetAsync("/api/admin/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, secondResponse.StatusCode);

        // Unlock user 1 back to active for other tests
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var user = await db.users.FindAsync((long)1);
            user!.status = UserStatusConstants.Active;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task TokenRevalidation_WhenAdminRoleRevokedInDatabase_NextRequestWithOldAdminTokenMustReturn403()
    {
        await _factory.SeedInitialDataAsync();
        var client = _factory.CreateClient();

        var adminToken = GenerateTokenForUser(1, "admin@example.test", RoleConstants.SystemAdministrator);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // First request is OK
        var firstResponse = await client.GetAsync("/api/admin/accounts");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Revoke role 5 (system_administrator) from user 1 in DB, keeping user active
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var userRole = await db.user_roles.FirstOrDefaultAsync(ur => ur.user_id == 1 && ur.role_id == 5);
            if (userRole != null)
            {
                db.user_roles.Remove(userRole);
                await db.SaveChangesAsync();
            }
        }

        // Second request with SAME old token (which still contains role claim in JWT string!)
        // Server must dynamically revalidate and realize user no longer possesses the role -> 403 Forbidden!
        var secondResponse = await client.GetAsync("/api/admin/accounts");
        Assert.Equal(HttpStatusCode.Forbidden, secondResponse.StatusCode);

        // Restore role for consistency
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            db.user_roles.Add(new user_role { user_id = 1, role_id = 5, granted_at = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
    }
}
