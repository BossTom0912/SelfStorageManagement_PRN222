using Microsoft.AspNetCore.Authentication.Cookies;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.Web.Hubs;
using SelfStorageManagementSystem.Web.Realtime;
using SelfStorageManagementSystem.Web.Workers;

var builder = WebApplication.CreateBuilder(args);

// 1. Đăng ký DAL + BLL Core thông qua BLL Composition Entrypoint.
// Web app không reference DataAccess, API hay WPF, và không resolve hay cần JWT secret key.
builder.Services.AddSelfStorageWebComposition(builder.Configuration);

// 2. Đăng ký MVC Controllers với Views, Razor Pages, và SignalR Realtime Hub
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IStorageUnitHubConnectionTracker, StorageUnitHubConnectionTracker>();
builder.Services.AddScoped<StorageUnitRealtimePublisher>();
builder.Services.AddScoped<IStorageUnitRealtimePublisher>(sp => sp.GetRequiredService<StorageUnitRealtimePublisher>());
builder.Services.AddScoped<IWebRealtimePublisher>(sp => sp.GetRequiredService<StorageUnitRealtimePublisher>());
builder.Services.AddScoped<StorageUnitHubScopeRevocationService>();
builder.Services.AddScoped<IUserScopeChangedNotifier>(sp => sp.GetRequiredService<StorageUnitHubScopeRevocationService>());
builder.Services.AddHostedService<StorageUnitScopeSyncWorker>();
builder.Services.AddSingleton(System.Text.Encodings.Web.HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All));

// 3. Đăng ký Background Worker xử lý hết hạn giữ chỗ 15 phút (BR-RSV-01) và Hook cho T15
builder.Services.AddScoped<IReservationExpirationHook, ReservationExpirationRealtimeHook>();
if (builder.Configuration.GetValue<bool?>("ReservationWorker:Enabled") ?? true)
{
    builder.Services.AddHostedService(sp => new ReservationHoldExpirationWorker(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<ILogger<ReservationHoldExpirationWorker>>(),
        sp.GetRequiredService<IConfiguration>()));
}

// 4. Cấu hình Cookie Authentication độc lập và an toàn theo chuẩn TeacherCode & PRN222 (T05)
// Cookie 5 phút, sliding expiration, ValidatePrincipal đọc identity snapshot mới nhất, xử lý 401/403 cho AJAX/SignalR
builder.Services.AddWebCookieAuthentication();

// 5. Cấu hình phân quyền dựa trên Role Code thật (Real Roles Authorization Policies)
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

// 6. Cấu hình bảo vệ chống giả mạo yêu cầu (Antiforgery protection)
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "SelfStoragePRN222.Web.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

var app = builder.Build();

// Xử lý ngoại lệ tập trung và trang mã trạng thái HTTP
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseStatusCodePagesWithReExecute("/Home/StatusCode/{0}");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Origin gate for Realtime SignalR Hub (T16)
app.UseStorageUnitHubOriginRestriction();

// Realtime SignalR Hub Endpoint
app.MapHub<StorageUnitHub>("/storageUnitHub", options =>
{
    options.CloseOnAuthenticationExpiration = true;
});

// Default MVC Controller Route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Attribute-routed API Controllers (VNPAY callbacks, Demo simulator)
app.MapControllers();

// Razor Pages Route
app.MapRazorPages();

app.Run();

// Partial class Program declaration for WebApplicationFactory in WebTests
public partial class Program { }
