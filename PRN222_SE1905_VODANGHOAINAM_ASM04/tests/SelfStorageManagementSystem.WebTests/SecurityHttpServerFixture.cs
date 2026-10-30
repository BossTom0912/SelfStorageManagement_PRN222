using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.Web.Hubs;
using SelfStorageManagementSystem.Web.Realtime;
using SelfStorageManagementSystem.Web.Workers;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Dedicated test harness and fixture for WebMigration Task T16:
/// - Boots a real ASP.NET Core Kestrel HTTP web server on loopback TCP (127.0.0.1:0).
/// - Configures the exact production middleware pipeline (Cookie auth, Authorize policies, Antiforgery, SignalR hub, Exception handler, StatusCode re-execute).
/// - Backed by an isolated EF Core In-Memory database with seed helpers for all 5 roles, facilities, units, reservations.
/// - Provides real HTTP client helpers for CSRF extraction, cookie handling, and form submission over genuine TCP loopback.
/// </summary>
public sealed class SecurityHttpServerFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private readonly string _dbName = Guid.NewGuid().ToString("N");
    private static long _facilitySeq = 50000;
    private static long _userSeq = 30000;

    public string ServerUrl { get; private set; } = string.Empty;
    public IServiceProvider Services => _app?.Services ?? throw new InvalidOperationException("Server not started.");

    public async Task InitializeAsync()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "../../../../../src/SelfStorageManagementSystem.Web")),
            Path.GetFullPath(Path.Combine(baseDir, "../../../../src/SelfStorageManagementSystem.Web")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src/SelfStorageManagementSystem.Web")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../src/SelfStorageManagementSystem.Web"))
        };
        var webRoot = candidates.FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException($"Could not locate SelfStorageManagementSystem.Web directory. Checked: {string.Join(", ", candidates)}");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = webRoot,
            WebRootPath = Path.Combine(webRoot, "wwwroot"),
            EnvironmentName = "Production", // Test under production mode to verify safe error handling and zero leakage
            Args = Array.Empty<string>()
        });

        // 1. Configure Kestrel to listen on loopback with an ephemeral dynamic port (port 0)
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0);
        });

        // 2. Register BLL & Core Services
        builder.Services.AddSelfStorageWebComposition(builder.Configuration);

        // Replace DbContext with isolated in-memory database
        builder.Services.RemoveAll(typeof(DbContextOptions<SelfStorageDbContext>));
        builder.Services.RemoveAll(typeof(SelfStorageDbContext));
        builder.Services.AddDbContext<SelfStorageDbContext>(options =>
        {
            options.UseInMemoryDatabase($"SelfStorage_SecurityHttp_{_dbName}");
        });

        // 3. Register Controllers, Razor Pages, SignalR & Realtime components
        builder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(SelfStorageManagementSystem.Web.Controllers.AccountController).Assembly);
        builder.Services.AddRazorPages()
            .AddApplicationPart(typeof(SelfStorageManagementSystem.Web.Pages.StorageUnits.IndexModel).Assembly);
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IStorageUnitHubConnectionTracker, StorageUnitHubConnectionTracker>();
        builder.Services.AddScoped<StorageUnitRealtimePublisher>();
        builder.Services.AddScoped<IStorageUnitRealtimePublisher>(sp => sp.GetRequiredService<StorageUnitRealtimePublisher>());
        builder.Services.AddScoped<IWebRealtimePublisher>(sp => sp.GetRequiredService<StorageUnitRealtimePublisher>());
        builder.Services.AddScoped<StorageUnitHubScopeRevocationService>();
        builder.Services.AddScoped<IUserScopeChangedNotifier>(sp => sp.GetRequiredService<StorageUnitHubScopeRevocationService>());
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton(System.Text.Encodings.Web.HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All));
        builder.Services.AddScoped<IReservationExpirationHook, ReservationExpirationRealtimeHook>();

        // 4. Web Cookie Authentication
        builder.Services.AddWebCookieAuthentication();

        // 5. Authorization Policies
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("CustomerOnly", policy => policy.RequireRole(RoleConstants.StorageCustomer));
            options.AddPolicy("StaffOnly", policy => policy.RequireRole(
                RoleConstants.FacilityStaff,
                RoleConstants.FacilityManager,
                RoleConstants.BusinessOperationsManager,
                RoleConstants.SystemAdministrator));
            options.AddPolicy("ManagerOnly", policy => policy.RequireRole(
                RoleConstants.FacilityManager,
                RoleConstants.BusinessOperationsManager,
                RoleConstants.SystemAdministrator));
            options.AddPolicy("AdminOnly", policy => policy.RequireRole(RoleConstants.SystemAdministrator));
        });

        // 6. Antiforgery Configuration
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "SelfStoragePRN222.Web.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        _app = builder.Build();

        // Middleware pipeline matching Program.cs
        if (!_app.Environment.IsDevelopment())
        {
            _app.UseExceptionHandler("/Home/Error");
        }
        _app.UseStatusCodePagesWithReExecute("/Home/StatusCode/{0}");

        _app.UseStaticFiles();
        _app.UseRouting();

        _app.UseAuthentication();
        _app.UseAuthorization();

        // Origin gate for Realtime SignalR Hub (T16)
        _app.UseStorageUnitHubOriginRestriction();

        _app.MapHub<StorageUnitHub>("/storageUnitHub", options =>
        {
            options.CloseOnAuthenticationExpiration = true;
        });
        _app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");
        _app.MapControllers();
        _app.MapRazorPages();

        await _app.StartAsync();

        var server = _app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        ServerUrl = addresses!.Addresses.First();

        await SeedDefaultRolesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app != null)
        {
            try
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
            catch { }
        }
    }

    public SelfStorageDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
    }

    public HttpClient CreateHttpClient(bool allowAutoRedirect = false, CookieContainer? cookieContainer = null)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
            CookieContainer = cookieContainer ?? new CookieContainer(),
            UseCookies = true
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(ServerUrl)
        };
    }

    public async Task SeedDefaultRolesAsync()
    {
        using var db = CreateDbContext();
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

    public async Task<user> SeedUserAsync(
        string email,
        string password,
        string roleCode,
        string status = UserStatusConstants.Active,
        string? displayName = null,
        string employmentStatus = "active",
        long? assignedFacilityId = null,
        string? assignedRole = null)
    {
        using var db = CreateDbContext();
        var hasher = Services.GetRequiredService<IPasswordHasher>();

        var targetRole = await db.roles.FirstAsync(r => r.code == roleCode);

        var existingUser = await db.users.FirstOrDefaultAsync(u => u.email == email);
        if (existingUser != null)
        {
            return existingUser;
        }

        var uid = Interlocked.Increment(ref _userSeq);
        var newUser = new user
        {
            id = uid,
            email = email.Trim().ToLowerInvariant(),
            password_hash = hasher.HashPassword(password),
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

        var name = displayName ?? $"User {roleCode} {uid}";

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
                full_name = name,
                employee_code = $"EMP{newUser.id:D5}",
                employment_status = employmentStatus,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
        }

        if (assignedFacilityId.HasValue)
        {
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = newUser.id,
                facility_id = assignedFacilityId.Value,
                assignment_role = assignedRole ?? roleCode,
                starts_at = DateTimeOffset.UtcNow.AddDays(-7),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return newUser;
    }

    public async Task<(facility Facility, unit_type UnitType, facility_area Area, facility_rate Rate, storage_unit Unit)> SeedFacilityHierarchyAsync(
        string suffix,
        string unitStatus = "available")
    {
        using var db = CreateDbContext();
        var facId = Interlocked.Increment(ref _facilitySeq);

        var fac = new facility
        {
            id = facId,
            code = $"FAC_{suffix.ToUpperInvariant()}_{facId}",
            name = $"Cơ Sở HTTP {suffix}",
            address_line = $"Số {facId} Phố Bảo Mật",
            ward = "Phường 1",
            district = "Quận Ba Đình",
            city = "Hà Nội",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh",
            opening_time = new TimeOnly(8, 0),
            closing_time = new TimeOnly(20, 0),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.facilities.Add(fac);

        var area = new facility_area
        {
            id = facId * 100 + 1,
            facility_id = facId,
            code = $"AREA_{suffix.ToUpperInvariant()}",
            name = $"Khu {suffix}",
            area_type = "floor",
            display_order = 1,
            map_metadata = "{}",
            is_active = true
        };
        db.facility_areas.Add(area);

        var utype = new unit_type
        {
            id = facId * 100 + 2,
            code = $"TYPE_{suffix.ToUpperInvariant()}",
            name = $"Loại {suffix}",
            description = "Mô tả",
            width_m = 2.0m,
            length_m = 3.0m,
            height_m = 2.5m,
            area_m2 = 6.0m,
            volume_m3 = 15.0m,
            climate_controlled = true,
            max_weight_kg = 500m,
            is_active = true,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.unit_types.Add(utype);

        var rate = new facility_rate
        {
            id = facId * 100 + 3,
            facility_id = facId,
            unit_type_id = utype.id,
            monthly_rate = 1200000m,
            deposit_amount = 1200000m,
            booking_fee = 100000m,
            valid_from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            valid_to = null,
            created_at = DateTimeOffset.UtcNow
        };
        db.facility_rates.Add(rate);

        var unit = new storage_unit
        {
            id = facId * 100 + 4,
            facility_id = facId,
            area_id = area.id,
            unit_type_id = utype.id,
            unit_code = $"UNIT-{suffix.ToUpperInvariant()}-{facId}",
            floor_label = "Tầng 1",
            zone_label = "Khu A",
            physical_status = unitStatus,
            is_listed = true,
            notes = "Ô kho test",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.storage_units.Add(unit);

        await db.SaveChangesAsync();
        return (fac, utype, area, rate, unit);
    }

    public async Task<storage_unit> SeedAdditionalStorageUnitAsync(
        long facilityId,
        long areaId,
        long unitTypeId,
        string unitCode,
        string physicalStatus = "available")
    {
        using var db = CreateDbContext();
        var unitId = Interlocked.Increment(ref _facilitySeq) * 100 + 4;
        var unit = new storage_unit
        {
            id = unitId,
            facility_id = facilityId,
            area_id = areaId,
            unit_type_id = unitTypeId,
            unit_code = unitCode,
            floor_label = "Tầng 1",
            zone_label = "Khu A",
            physical_status = physicalStatus,
            is_listed = true,
            notes = "Ô kho test bổ sung",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.storage_units.Add(unit);
        await db.SaveChangesAsync();
        return unit;
    }

    public async Task<reservation> SeedReservationAsync(
        long customerUserId,
        long facilityId,
        long unitTypeId,
        long facilityRateId,
        string status = "pending",
        TimeSpan? holdDuration = null)
    {
        using var db = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var holdSpan = holdDuration ?? TimeSpan.FromMinutes(15);

        var res = new reservation
        {
            reservation_code = $"RES-SEC-{Guid.NewGuid():N}".Substring(0, 20).ToUpperInvariant(),
            customer_id = customerUserId,
            facility_id = facilityId,
            unit_type_id = unitTypeId,
            facility_rate_id = facilityRateId,
            start_date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            end_date = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            monthly_rate_snapshot = 1200000m,
            deposit_snapshot = 1200000m,
            booking_fee_snapshot = 100000m,
            discount_snapshot = 0m,
            quoted_total = 2500000m,
            hold_until = now.Add(holdSpan),
            status = status,
            created_at = now,
            updated_at = now
        };

        db.reservations.Add(res);
        await db.SaveChangesAsync();
        return res;
    }

    public async Task<(string token, string cookieHeader)> GetAntiforgeryTokensAsync(
        HttpClient client,
        string pagePath = "/Account/Login",
        CookieContainer? cookieContainer = null)
    {
        var response = await client.GetAsync(pagePath);
        response.EnsureSuccessStatusCode();

        string? cookieHeader = null;

        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToList()
            : new List<string>();

        var antiforgeryCookie = setCookies.FirstOrDefault(c => c.Contains("SelfStoragePRN222.Web.Antiforgery"));
        if (antiforgeryCookie != null)
        {
            cookieHeader = antiforgeryCookie.Split(';')[0];
        }
        else if (cookieContainer != null && client.BaseAddress != null)
        {
            var cookies = cookieContainer.GetCookies(client.BaseAddress);
            var existingCookie = cookies["SelfStoragePRN222.Web.Antiforgery"];
            if (existingCookie != null)
            {
                cookieHeader = $"{existingCookie.Name}={existingCookie.Value}";
            }
        }

        if (string.IsNullOrEmpty(cookieHeader))
        {
            throw new InvalidOperationException("Could not find antiforgery cookie in response or cookie container.");
        }

        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(html, @"<input[^>]+value=""([^""]+)""[^>]+name=""__RequestVerificationToken""", RegexOptions.IgnoreCase);
        }

        if (!match.Success)
        {
            throw new InvalidOperationException("Could not extract __RequestVerificationToken from page HTML.");
        }

        return (match.Groups[1].Value, cookieHeader);
    }

    public async Task<(string authCookieHeader, HttpResponseMessage response)> LoginViaHttpAsync(
        HttpClient client,
        string email,
        string password,
        string returnUrl = "/",
        CookieContainer? cookieContainer = null)
    {
        var (token, cookieHeader) = await GetAntiforgeryTokensAsync(client, "/Account/Login", cookieContainer);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        request.Headers.Add("Cookie", cookieHeader);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = returnUrl
        });

        var response = await client.SendAsync(request);

        string authCookieHeader = string.Empty;
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var authCookie = cookies.FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
            if (authCookie != null)
            {
                authCookieHeader = authCookie.Split(';')[0];
            }
        }

        if (string.IsNullOrEmpty(authCookieHeader) && cookieContainer != null && client.BaseAddress != null)
        {
            var c = cookieContainer.GetCookies(client.BaseAddress)[CookieAuthExtensions.CookieName];
            if (c != null)
            {
                authCookieHeader = $"{c.Name}={c.Value}";
            }
        }

        return (authCookieHeader, response);
    }
}
