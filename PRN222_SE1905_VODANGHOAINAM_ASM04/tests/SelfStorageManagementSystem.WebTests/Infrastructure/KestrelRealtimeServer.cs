using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.Web.Hubs;
using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.WebTests.Infrastructure;

/// <summary>
/// Lightweight standalone Kestrel server hosting the real ASP.NET Core Razor Pages,
/// static assets (wwwroot), and SignalR Hub (/storageUnitHub) over real HTTP and WebSocket transports.
/// Used for genuine end-to-end browser tests in Headless Microsoft Edge.
/// </summary>
public sealed class KestrelRealtimeServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string ServerUrl { get; }
    public IServiceProvider Services => _app.Services;

    private KestrelRealtimeServer(WebApplication app, string serverUrl)
    {
        _app = app;
        ServerUrl = serverUrl;
    }

    public static async Task<KestrelRealtimeServer> StartAsync(string? dbName = null, Action<IServiceCollection>? configureServices = null)
    {
        dbName ??= Guid.NewGuid().ToString("N");
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
            EnvironmentName = "Testing",
            Args = Array.Empty<string>()
        });

        // 1. Configure Kestrel to listen on loopback with an ephemeral dynamic port (port 0)
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0);
        });

        // 2. Register BLL & Core Services
        builder.Services.AddSelfStorageWebComposition(builder.Configuration);

        // Replace DbContext with in-memory database for isolated testing
        builder.Services.RemoveAll(typeof(DbContextOptions<SelfStorageDbContext>));
        builder.Services.RemoveAll(typeof(SelfStorageDbContext));
        builder.Services.AddDbContext<SelfStorageDbContext>(options =>
        {
            options.UseInMemoryDatabase($"SelfStorage_KestrelTest_{dbName}");
        });

        // 3. Register Controllers, Razor Pages & SignalR Hub
        builder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(SelfStorageManagementSystem.Web.Pages.StorageUnits.IndexModel).Assembly);
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

        // 4. Cookie Authentication & Authorization
        builder.Services.AddWebCookieAuthentication();
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

        configureServices?.Invoke(builder.Services);

        // 5. Build application
        var app = builder.Build();

        app.UseStaticFiles();
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        // Test login endpoint allowing headless browser to acquire real authentication cookie
        app.MapGet("/test-auth-login", async (HttpContext ctx, string? role, long? userId, string? name) =>
        {
            var userRole = role ?? RoleConstants.FacilityManager;
            var uid = userId ?? 1001L;
            var userName = name ?? $"Manager_{uid}";

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, uid.ToString()),
                new Claim(ClaimTypes.Name, userName),
                new Claim(ClaimTypes.Role, userRole),
                new Claim("UserStatus", UserStatusConstants.Active)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.Ok(new { success = true, role = userRole, userId = uid });
        });

        app.MapHub<StorageUnitHub>("/storageUnitHub", options =>
        {
            options.CloseOnAuthenticationExpiration = true;
        });
        app.MapRazorPages();
        app.MapControllers();

        await app.StartAsync();

        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        var serverUrl = addresses!.Addresses.First();

        return new KestrelRealtimeServer(app, serverUrl);
    }

    public IServiceScope CreateScope() => _app.Services.CreateScope();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        catch { }
    }
}
