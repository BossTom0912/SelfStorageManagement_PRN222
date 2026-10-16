using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.WebTests.Fixtures;

/// <summary>
/// WebApplicationFactory chuyên biệt cho kiểm thử xác thực Cookie của Task T05:
/// - Giữ nguyên CookieAuthenticationScheme thực tế làm AuthenticationScheme mặc định (không thay thế bằng TestAuthHandler).
/// - Khởi tạo DbContext In-Memory riêng biệt cho từng kịch bản kiểm thử.
/// - Cung cấp helper gieo mầm dữ liệu (Seed) cho cả 5 role hệ thống và trích xuất Antiforgery token.
/// </summary>
public class CookieAuthWebTestFixture : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Thay thế SQL Server bằng In-Memory Database cho kiểm thử
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<SelfStorageDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<SelfStorageDbContext>(options =>
            {
                options.UseInMemoryDatabase($"SelfStorage_CookieAuthTests_{_dbName}");
            });
        });
    }

    public SelfStorageDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
    }

    /// <summary>
    /// Gieo sẵn cả 5 vai trò (roles) chuẩn vào cơ sở dữ liệu in-memory.
    /// </summary>
    public async Task SeedDefaultRolesAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();

        var existingCodes = await db.roles.Select(r => r.code).ToListAsync();

        var rolesToSeed = new List<(short id, string code, string name)>
        {
            (1, RoleConstants.StorageCustomer, "Customer"),
            (2, RoleConstants.FacilityStaff, "Facility Staff"),
            (3, RoleConstants.FacilityManager, "Facility Manager"),
            (4, RoleConstants.BusinessOperationsManager, "Operations Manager"),
            (5, RoleConstants.SystemAdministrator, "System Administrator")
        };

        foreach (var (id, code, name) in rolesToSeed)
        {
            if (!existingCodes.Contains(code))
            {
                db.roles.Add(new role
                {
                    id = id,
                    code = code,
                    display_name = name,
                    description = $"Role {code}"
                });
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Gieo một người dùng mới kèm mật khẩu mã hóa, hồ sơ và vai trò tương ứng.
    /// </summary>
    public async Task<user> SeedUserAsync(
        string email,
        string rawPassword,
        string roleCode,
        string status = UserStatusConstants.Active,
        string? displayName = null,
        string? employeeCode = null,
        string employmentStatus = "active")
    {
        await SeedDefaultRolesAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var targetRole = await db.roles.FirstAsync(r => r.code == roleCode);

        var existingUser = await db.users.FirstOrDefaultAsync(u => u.email == email);
        if (existingUser != null)
        {
            return existingUser;
        }

        var newUser = new user
        {
            email = email.Trim().ToLowerInvariant(),
            password_hash = hasher.HashPassword(rawPassword),
            status = status,
            phone_number = "0901234567",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };

        db.users.Add(newUser);
        await db.SaveChangesAsync();

        db.user_roles.Add(new user_role
        {
            user_id = newUser.id,
            role_id = targetRole.id,
            granted_at = DateTimeOffset.UtcNow
        });

        var name = displayName ?? $"User {roleCode}";

        if (roleCode == RoleConstants.StorageCustomer)
        {
            db.customer_profiles.Add(new customer_profile
            {
                user_id = newUser.id,
                full_name = name,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
        }
        else
        {
            db.employee_profiles.Add(new employee_profile
            {
                user_id = newUser.id,
                employee_code = employeeCode ?? $"EMP_{newUser.id:D4}",
                full_name = name,
                hire_date = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                employment_status = employmentStatus,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return newUser;
    }

    /// <summary>
    /// Trích xuất Antiforgery Token và Cookie từ trang HTML (GET /Account/Login hoặc /Account/Register hoặc /Account/Logout).
    /// </summary>
    public static async Task<(string token, string cookieHeader)> GetAntiforgeryTokenAndCookieAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.ToList()
            : new List<string>();

        var cookieHeader = string.Join("; ", setCookies.Select(c => c.Split(';')[0]));

        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(html, @"value=""([^""]+)"" name=""__RequestVerificationToken""");
        }

        Assert.True(match.Success, $"Không tìm thấy Antiforgery token trong nội dung HTML trả về từ {url}.");
        return (match.Groups[1].Value, cookieHeader);
    }
}
