using System.Net;
using System.Text.RegularExpressions;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.Web.Helpers;
using SelfStorageManagementSystem.WebTests.Fixtures;

namespace SelfStorageManagementSystem.WebTests;

public class LayoutAndSharedComponentsTests : IClassFixture<WebTestFixture>
{
    private readonly WebTestFixture _factory;
    private readonly HttpClient _client;

    public LayoutAndSharedComponentsTests(WebTestFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task MvcView_RendersWithSharedLayout_AndTeacherCodeStyle()
    {
        // Act - Fetch MVC View Home/Index
        var response = await _client.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        // 1. Language & Charset
        Assert.Contains("<html lang=\"vi\">", html);
        Assert.Contains("<meta charset=\"utf-8\"", html);

        // 2. Brand matching TeacherCode style
        Assert.Contains("SelfStorage", decoded);
        Assert.Contains("PRN222", decoded);

        // 3. Local assets (No CDNs)
        Assert.Contains("/lib/bootstrap/dist/css/bootstrap.min.css", html);
        Assert.Contains("/css/site.css", html);
        Assert.Contains("/lib/jquery/dist/jquery.min.js", html);
        Assert.Contains("/lib/bootstrap/dist/js/bootstrap.bundle.min.js", html);
        Assert.Contains("/lib/microsoft/signalr/dist/browser/signalr.min.js", html);
        Assert.Contains("/js/site.js", html);

        // 4. Academic TeacherCode footer
        Assert.Contains("Hệ Thống Quản Lý Kho Tự Quản SelfStorage", decoded);
        Assert.Contains("PRN222 Web", decoded);
        Assert.Contains("ASP.NET Core (.NET 8)", decoded);
        Assert.Contains("MVC Cookie", decoded);
    }

    [Fact]
    public async Task RazorPage_RendersWithSharedLayout_AndComponents()
    {
        // Act - Fetch Razor Page /layout-test
        var response = await _client.GetAsync("/layout-test");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        // 1. Verifies that Razor Page uses the exact same _Layout
        Assert.Contains("<html lang=\"vi\">", html);
        Assert.Contains("Kiểm Tra Khả Năng Tương Thích Layout Chung", decoded);
        Assert.Contains("SelfStorage", decoded);
        Assert.Contains("PRN222", decoded);

        // 2. Verifies Vietnamese and Currency formatting from AppFormatters
        Assert.Contains("1.500.000 ₫", decoded);

        // 3. Verifies Reusable Empty State partial
        Assert.Contains("Chưa Có Đơn Đặt Chỗ Nào", decoded);
        Assert.Contains("Xem danh mục kho", decoded);

        // 4. Verifies Form and Validation Partial
        Assert.Contains("SampleName", html);
        Assert.Contains("SampleEmail", html);

        // 5. Verifies Client-side Validation script partial
        Assert.Contains("/lib/jquery-validation/dist/jquery.validate.min.js", html);
        Assert.Contains("/lib/jquery-validation-unobtrusive/jquery.validate.unobtrusive.min.js", html);
        Assert.Contains("Trường này không được để trống.", decoded);
    }

    [Fact]
    public async Task GuestNavigation_RendersOnlyPublicLinks()
    {
        // Act - Unauthenticated request
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        var navHtml = Regex.Match(html, @"<nav[^>]*>([\s\S]*?)<\/nav>").Value;
        var decodedNav = WebUtility.HtmlDecode(navHtml);

        // Public menu items in navbar
        Assert.Contains("Trang chủ", decodedNav);
        Assert.Contains("Danh mục kho", decodedNav);
        Assert.Contains("/Catalog", navHtml);

        // Auth action buttons
        Assert.Contains("Đăng nhập", decodedNav);
        Assert.Contains("/Account/Login", navHtml);
        Assert.Contains("Đăng ký", decodedNav);
        Assert.Contains("/Account/Register", navHtml);

        // Must NOT render role-specific protected links in nav
        Assert.DoesNotContain("Đặt chỗ của tôi", decodedNav);
        Assert.DoesNotContain("Ô kho của tôi", decodedNav);
        Assert.DoesNotContain("Quầy tiếp nhận", decodedNav);
        Assert.DoesNotContain("Quản trị ô kho", decodedNav);
        Assert.DoesNotContain("Quản lý tài khoản", decodedNav);
        Assert.DoesNotContain("Đăng xuất", decodedNav);
    }

    [Fact]
    public async Task CustomerNavigation_RendersCustomerSpecificLinks()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Test-Role", RoleConstants.StorageCustomer);
        request.Headers.Add("X-Test-User", "khachhang.nguyen@example.com");

        // Act
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        var navHtml = Regex.Match(html, @"<nav[^>]*>([\s\S]*?)<\/nav>").Value;
        var decodedNav = WebUtility.HtmlDecode(navHtml);

        // Assert Customer Links in navbar
        Assert.Contains("Đặt chỗ của tôi", decodedNav);
        Assert.Contains("/Reservations", navHtml);
        Assert.Contains("Ô kho của tôi", decodedNav);
        Assert.Contains("/MyUnits", navHtml);
        Assert.Contains("Thanh toán & Hợp đồng", decodedNav);
        Assert.Contains("/Invoices", navHtml);
        Assert.Contains("/Payments", navHtml);
        Assert.Contains("/Agreements", navHtml);
        Assert.Contains("Thông báo", decodedNav);
        Assert.Contains("/Notifications", navHtml);

        // User badge and identity
        Assert.Contains("Khách hàng", decodedNav);
        Assert.Contains("khachhang.nguyen@example.com", decodedNav);

        // CSRF-protected POST Logout form
        Assert.Contains("action=\"/Account/Logout\"", navHtml);
        Assert.Contains("method=\"post\"", navHtml);
        Assert.Contains("__RequestVerificationToken", navHtml);
        Assert.Contains("Đăng xuất", decodedNav);

        // Must NOT render Staff or Admin items in navbar
        Assert.DoesNotContain("Quầy tiếp nhận", decodedNav);
        Assert.DoesNotContain("Quản trị ô kho", decodedNav);
        Assert.DoesNotContain("Quản lý tài khoản", decodedNav);
    }

    [Fact]
    public async Task StaffNavigation_RendersStaffSpecificLinks()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Test-Role", RoleConstants.FacilityStaff);
        request.Headers.Add("X-Test-User", "staff.le@example.com");

        // Act
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        var navHtml = Regex.Match(html, @"<nav[^>]*>([\s\S]*?)<\/nav>").Value;
        var decodedNav = WebUtility.HtmlDecode(navHtml);

        // Assert Staff Links in navbar
        Assert.Contains("Quầy tiếp nhận", decodedNav);
        Assert.Contains("/Staff/CheckIn", navHtml);
        Assert.Contains("Quản trị ô kho", decodedNav);
        Assert.Contains("/StorageUnits", navHtml);
        Assert.Contains("Nghiệp vụ cơ sở", decodedNav);
        Assert.Contains("/Invoices", navHtml);
        Assert.Contains("/Agreements", navHtml);

        // User badge
        Assert.Contains("Nhân viên cơ sở", decodedNav);
        Assert.Contains("staff.le@example.com", decodedNav);

        // Staff does NOT have payment reconciliation or account management
        Assert.DoesNotContain("Đối soát thanh toán & Hoàn tiền", decodedNav);
        Assert.DoesNotContain("Quản lý tài khoản", decodedNav);
    }

    [Fact]
    public async Task ManagerNavigation_RendersManagerSpecificLinks()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Test-Role", RoleConstants.FacilityManager);
        request.Headers.Add("X-Test-User", "manager.tran@example.com");

        // Act
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        var navHtml = Regex.Match(html, @"<nav[^>]*>([\s\S]*?)<\/nav>").Value;
        var decodedNav = WebUtility.HtmlDecode(navHtml);

        // Assert Manager Links in navbar
        Assert.Contains("Quầy tiếp nhận", decodedNav);
        Assert.Contains("/Staff/CheckIn", navHtml);
        Assert.Contains("Quản trị ô kho", decodedNav);
        Assert.Contains("/StorageUnits", navHtml);
        Assert.Contains("Nghiệp vụ cơ sở", decodedNav);
        Assert.Contains("Đối soát thanh toán & Hoàn tiền", decodedNav);
        Assert.Contains("/Payments", navHtml);

        // User badge
        Assert.Contains("Quản lý cơ sở", decodedNav);
        Assert.Contains("manager.tran@example.com", decodedNav);

        // Manager does NOT have system account management
        Assert.DoesNotContain("Quản lý tài khoản", decodedNav);
    }

    [Fact]
    public async Task AdminNavigation_RendersAdministratorLinks()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Test-Role", RoleConstants.SystemAdministrator);
        request.Headers.Add("X-Test-User", "admin.sys@example.com");

        // Act
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        var navHtml = Regex.Match(html, @"<nav[^>]*>([\s\S]*?)<\/nav>").Value;
        var decodedNav = WebUtility.HtmlDecode(navHtml);

        // Assert Admin Links in navbar
        Assert.Contains("Quản trị ô kho", decodedNav);
        Assert.Contains("/StorageUnits", navHtml);
        Assert.Contains("Quản lý tài khoản", decodedNav);
        Assert.Contains("/Admin/Accounts", navHtml);
        Assert.Contains("Quản trị hệ thống", decodedNav);

        // User badge
        Assert.Contains("Quản trị hệ thống", decodedNav);
        Assert.Contains("admin.sys@example.com", decodedNav);
    }

    [Fact]
    public async Task BusinessOperationsManagerNavigation_RendersOperationsLinks()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Test-Role", RoleConstants.BusinessOperationsManager);
        request.Headers.Add("X-Test-User", "ops.manager@example.com");

        // Act
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        var navHtml = Regex.Match(html, @"<nav[^>]*>([\s\S]*?)<\/nav>").Value;
        var decodedNav = WebUtility.HtmlDecode(navHtml);

        // Assert Operations Links in navbar
        Assert.Contains("Quản trị ô kho", decodedNav);
        Assert.Contains("/StorageUnits", navHtml);
        Assert.Contains("Quản trị hệ thống", decodedNav);
        Assert.Contains("Điều hành kinh doanh", decodedNav);
        Assert.Contains("ops.manager@example.com", decodedNav);
    }

    [Fact]
    public void AppFormatters_StandardizesVietnameseFormats()
    {
        // VND Currency
        Assert.Equal("1.500.000 ₫", AppFormatters.FormatVnd(1500000m));
        Assert.Equal("0 ₫", AppFormatters.FormatVnd(0m));
        Assert.Equal("0 ₫", AppFormatters.FormatVnd(null));

        // Dates & Times
        var dtOffset = new DateTimeOffset(2026, 10, 15, 14, 30, 0, TimeSpan.FromHours(7));
        Assert.Equal("15/10/2026", AppFormatters.FormatDate(dtOffset));
        Assert.Equal("15/10/2026 14:30", AppFormatters.FormatDateTime(dtOffset));
        Assert.Equal("—", AppFormatters.FormatDate((DateTimeOffset?)null));
        Assert.Equal("—", AppFormatters.FormatDateTime((DateTimeOffset?)null));

        // Role Display Names
        Assert.Equal("Khách hàng", AppFormatters.GetRoleDisplayName(RoleConstants.StorageCustomer));
        Assert.Equal("Nhân viên cơ sở", AppFormatters.GetRoleDisplayName(RoleConstants.FacilityStaff));
        Assert.Equal("Quản lý cơ sở", AppFormatters.GetRoleDisplayName(RoleConstants.FacilityManager));
        Assert.Equal("Điều hành kinh doanh", AppFormatters.GetRoleDisplayName(RoleConstants.BusinessOperationsManager));
        Assert.Equal("Quản trị hệ thống", AppFormatters.GetRoleDisplayName(RoleConstants.SystemAdministrator));

        // Role Badges
        Assert.Contains("bg-info", AppFormatters.GetRoleBadgeClass(RoleConstants.StorageCustomer));
        Assert.Contains("bg-primary", AppFormatters.GetRoleBadgeClass(RoleConstants.FacilityStaff));
        Assert.Contains("bg-warning", AppFormatters.GetRoleBadgeClass(RoleConstants.FacilityManager));
        Assert.Contains("bg-success", AppFormatters.GetRoleBadgeClass(RoleConstants.BusinessOperationsManager));
        Assert.Contains("bg-danger", AppFormatters.GetRoleBadgeClass(RoleConstants.SystemAdministrator));
    }

    [Fact]
    public async Task SafeErrorPages_NotFound_DoesNotLeakStackTraceOrSql()
    {
        // Act
        var response = await _client.GetAsync("/route-khong-ton-tai-404");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("404", html);
        Assert.Contains("Không Tìm Thấy Trang", decoded);
        Assert.Contains("Trang chủ", decoded);

        // Security check: No leakages
        Assert.DoesNotContain("System.Exception", html);
        Assert.DoesNotContain("StackTrace", html);
        Assert.DoesNotContain("SqlException", html);
        Assert.DoesNotContain("SELECT ", html);
        Assert.DoesNotContain("ConnectionString", html);
        Assert.DoesNotContain("Password", html);
        Assert.DoesNotContain("Secret", html);
    }

    [Fact]
    public async Task SafeErrorPages_InternalError_DoesNotLeakStackTraceOrSql()
    {
        // Act
        var response = await _client.PostAsync("/Home/Error", null);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("500", html);
        Assert.Contains("Lỗi Xử Lý Yêu Cầu", decoded);
        Assert.Contains("Mã sự cố (Request ID)", decoded);

        // Security check: No leakages
        Assert.DoesNotContain("System.Exception", html);
        Assert.DoesNotContain("StackTrace", html);
        Assert.DoesNotContain("SqlException", html);
        Assert.DoesNotContain("SELECT ", html);
        Assert.DoesNotContain("ConnectionString", html);
        Assert.DoesNotContain("Password", html);
        Assert.DoesNotContain("Secret", html);
    }

    [Fact]
    public async Task SafeErrorPages_ForbiddenStatusCode_RendersSafely()
    {
        // Act - Request centralized 403 Forbidden handler
        var response = await _client.GetAsync("/Home/StatusCode/403");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("403", html);
        Assert.Contains("Từ Chối Quyền Truy Cập (Forbidden)", decoded);
        Assert.Contains("Bạn không có quyền truy cập", decoded);
        Assert.Contains("/Account/Login", html);

        // Security check: No leakages
        Assert.DoesNotContain("System.Exception", html);
        Assert.DoesNotContain("StackTrace", html);
        Assert.DoesNotContain("SqlException", html);
        Assert.DoesNotContain("SELECT ", html);
        Assert.DoesNotContain("ConnectionString", html);
    }

    [Fact]
    public void ForbiddenView_FileExistsAndIsConfigured()
    {
        // Verify dedicated Views/Account/Forbidden.cshtml exists for TeacherCode AccountController compatibility
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "SelfStorageManagementSystem.Web.sln")))
        {
            currentDir = currentDir.Parent;
        }

        Assert.NotNull(currentDir);
        var forbiddenViewPath = Path.Combine(currentDir.FullName, "src", "SelfStorageManagementSystem.Web", "Views", "Account", "Forbidden.cshtml");
        Assert.True(File.Exists(forbiddenViewPath), $"Forbidden.cshtml view not found at {forbiddenViewPath}");

        var content = File.ReadAllText(forbiddenViewPath);
        Assert.Contains("Từ Chối Quyền Truy Cập", content);
        Assert.DoesNotContain("asp-asp-controller", content); // TeacherCode bug must be fixed
    }

    [Fact]
    public async Task Layout_DoesNotEstablishGlobalSignalRConnection()
    {
        // Act
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        // The layout includes the signalr.min.js script tag:
        Assert.Contains("/lib/microsoft/signalr/dist/browser/signalr.min.js", html);

        // BUT the layout MUST NOT invoke .withUrl("/storageUnitHub") or .start() in global scope.
        // SignalR connections are only created per-page where realtime is explicitly needed.
        Assert.DoesNotContain("new signalR.HubConnectionBuilder()", html);
        Assert.DoesNotContain(".start().then", html);
    }

    [Fact]
    public async Task Navbar_AllLinksAreWellFormedAndMatchContractRoutes()
    {
        // Act
        var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        // Extract all hrefs inside the navbar
        var navMatch = Regex.Match(html, @"<nav[^>]*>([\s\S]*?)<\/nav>");
        Assert.True(navMatch.Success, "Navbar element not found in rendered HTML.");

        var hrefMatches = Regex.Matches(navMatch.Value, @"href=""([^""]*)""");
        Assert.NotEmpty(hrefMatches);

        foreach (Match match in hrefMatches)
        {
            var href = match.Groups[1].Value;

            // Must NOT contain template syntax or broken TeacherCode typos (e.g. asp-asp-controller)
            Assert.DoesNotContain("asp-", href);
            Assert.DoesNotContain("undefined", href);

            // Must be a root-relative path or hash anchor for dropdown toggles
            Assert.True(href.StartsWith("/") || href.StartsWith("#"), 
                $"Navbar contains malformed link: '{href}'");
        }
    }

    [Fact]
    public void AppFormatters_ConvertsUtcToVietnamTimezone_Correctly()
    {
        // Arrange: UTC time at 07:30 should convert to 14:30 in Vietnam (UTC+7)
        var utcOffset = new DateTimeOffset(2026, 10, 15, 7, 30, 0, TimeSpan.Zero);
        Assert.Equal("15/10/2026 14:30", AppFormatters.FormatDateTime(utcOffset));
        Assert.Equal("15/10/2026", AppFormatters.FormatDate(utcOffset));

        // Arrange: UTC DateTime kind
        var utcDateTime = new DateTime(2026, 10, 15, 7, 30, 0, DateTimeKind.Utc);
        Assert.Equal("15/10/2026 14:30", AppFormatters.FormatDateTime(utcDateTime));
        Assert.Equal("15/10/2026", AppFormatters.FormatDate(utcDateTime));

        // Arrange: Different offset (e.g. UTC+2 at 09:30 is 07:30 UTC -> 14:30 Vietnam)
        var plus2Offset = new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.FromHours(2));
        Assert.Equal("15/10/2026 14:30", AppFormatters.FormatDateTime(plus2Offset));

        // Null checks
        Assert.Equal("—", AppFormatters.FormatDateTime((DateTimeOffset?)null));
        Assert.Equal("—", AppFormatters.FormatDate((DateTimeOffset?)null));
        Assert.Equal("—", AppFormatters.FormatDateTime((DateTime?)null));
        Assert.Equal("—", AppFormatters.FormatDate((DateTime?)null));
        Assert.Equal("—", AppFormatters.FormatTime((TimeSpan?)null));
        TimeOnly? nullTime = null;
        Assert.Equal("—", AppFormatters.FormatTime(nullTime));
        Assert.Equal("08:00", AppFormatters.FormatTime(new TimeSpan(8, 0, 0)));
        Assert.Equal("08:00", AppFormatters.FormatTime(new TimeOnly(8, 0)));
    }

    [Fact]
    public void AppFormatters_GetPrimaryRole_FollowsPriorityOrder_RegardlessOfInputOrder()
    {
        // 1. Customer + Admin in both orderings -> SystemAdministrator
        Assert.Equal(RoleConstants.SystemAdministrator, AppFormatters.GetPrimaryRole(new[] { RoleConstants.StorageCustomer, RoleConstants.SystemAdministrator }));
        Assert.Equal(RoleConstants.SystemAdministrator, AppFormatters.GetPrimaryRole(new[] { RoleConstants.SystemAdministrator, RoleConstants.StorageCustomer }));

        // 2. Staff + Manager in both orderings -> FacilityManager
        Assert.Equal(RoleConstants.FacilityManager, AppFormatters.GetPrimaryRole(new[] { RoleConstants.FacilityStaff, RoleConstants.FacilityManager }));
        Assert.Equal(RoleConstants.FacilityManager, AppFormatters.GetPrimaryRole(new[] { RoleConstants.FacilityManager, RoleConstants.FacilityStaff }));

        // 3. Customer + Staff -> FacilityStaff
        Assert.Equal(RoleConstants.FacilityStaff, AppFormatters.GetPrimaryRole(new[] { RoleConstants.StorageCustomer, RoleConstants.FacilityStaff }));

        // 4. Operations + Admin -> SystemAdministrator
        Assert.Equal(RoleConstants.SystemAdministrator, AppFormatters.GetPrimaryRole(new[] { RoleConstants.BusinessOperationsManager, RoleConstants.SystemAdministrator }));

        // 5. Manager + Operations -> BusinessOperationsManager
        Assert.Equal(RoleConstants.BusinessOperationsManager, AppFormatters.GetPrimaryRole(new[] { RoleConstants.FacilityManager, RoleConstants.BusinessOperationsManager }));

        // 6. All roles present in reverse priority order
        var allRolesReversed = new[]
        {
            RoleConstants.StorageCustomer,
            RoleConstants.FacilityStaff,
            RoleConstants.FacilityManager,
            RoleConstants.BusinessOperationsManager,
            RoleConstants.SystemAdministrator
        };
        Assert.Equal(RoleConstants.SystemAdministrator, AppFormatters.GetPrimaryRole(allRolesReversed));

        // 7. ClaimsPrincipal with multiple role claims
        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, RoleConstants.StorageCustomer),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, RoleConstants.FacilityStaff),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, RoleConstants.FacilityManager)
        };
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "TestAuth"));
        Assert.Equal(RoleConstants.FacilityManager, AppFormatters.GetPrimaryRole(principal));
    }

    [Fact]
    public async Task MultiRole_CustomerAndAdmin_ShowsAdminBadge_AndRetainsCustomerAndAdminMenus()
    {
        // Arrange: Header with Customer first, then Admin
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Test-Role", $"{RoleConstants.StorageCustomer},{RoleConstants.SystemAdministrator}");
        request.Headers.Add("X-Test-User", "admin.customer@example.com");

        // Act
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        var navHtml = Regex.Match(html, @"<nav[^>]*>([\s\S]*?)<\/nav>").Value;
        var decodedNav = WebUtility.HtmlDecode(navHtml);

        // Assert: Badge must be Admin (highest priority), not Customer
        Assert.Contains("Quản trị hệ thống", decodedNav);
        Assert.Contains("bg-danger", navHtml);

        // Assert: Both Customer and Admin navigation items exist because User.IsInRole is preserved for both
        Assert.Contains("Đặt chỗ của tôi", decodedNav);
        Assert.Contains("/Reservations", navHtml);
        Assert.Contains("Quản lý tài khoản", decodedNav);
        Assert.Contains("/Admin/Accounts", navHtml);
    }

    [Fact]
    public async Task MultiRole_AdminAndCustomer_OrderInverted_ShowsAdminBadge_AndRetainsCustomerAndAdminMenus()
    {
        // Arrange: Header with Admin first, then Customer
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Test-Role", $"{RoleConstants.SystemAdministrator},{RoleConstants.StorageCustomer}");
        request.Headers.Add("X-Test-User", "admin.customer2@example.com");

        // Act
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        var navHtml = Regex.Match(html, @"<nav[^>]*>([\s\S]*?)<\/nav>").Value;
        var decodedNav = WebUtility.HtmlDecode(navHtml);

        // Assert: Badge must be Admin regardless of claim order
        Assert.Contains("Quản trị hệ thống", decodedNav);
        Assert.Contains("bg-danger", navHtml);

        // Assert: Both Customer and Admin menus are available
        Assert.Contains("Đặt chỗ của tôi", decodedNav);
        Assert.Contains("/Reservations", navHtml);
        Assert.Contains("Quản lý tài khoản", decodedNav);
        Assert.Contains("/Admin/Accounts", navHtml);
    }

    [Fact]
    public async Task MultiRole_StaffAndManager_ShowsManagerBadge()
    {
        // Arrange: Staff first, then Manager
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Test-Role", $"{RoleConstants.FacilityStaff},{RoleConstants.FacilityManager}");
        request.Headers.Add("X-Test-User", "lead.staff@example.com");

        // Act
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        var navHtml = Regex.Match(html, @"<nav[^>]*>([\s\S]*?)<\/nav>").Value;
        var decodedNav = WebUtility.HtmlDecode(navHtml);

        // Assert: Badge must be Manager (highest priority)
        Assert.Contains("Quản lý cơ sở", decodedNav);
        Assert.Contains("bg-warning", navHtml);

        // Assert: Manager reconciliation link is present
        Assert.Contains("Đối soát thanh toán & Hoàn tiền", decodedNav);
    }

    [Fact]
    public async Task MultiRole_ManagerAndStaff_OrderInverted_ShowsManagerBadge()
    {
        // Arrange: Manager first, then Staff
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Test-Role", $"{RoleConstants.FacilityManager},{RoleConstants.FacilityStaff}");
        request.Headers.Add("X-Test-User", "lead.staff2@example.com");

        // Act
        var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        var navHtml = Regex.Match(html, @"<nav[^>]*>([\s\S]*?)<\/nav>").Value;
        var decodedNav = WebUtility.HtmlDecode(navHtml);

        // Assert: Badge must still be Manager
        Assert.Contains("Quản lý cơ sở", decodedNav);
        Assert.Contains("bg-warning", navHtml);

        // Assert: Manager reconciliation link is present
        Assert.Contains("Đối soát thanh toán & Hoàn tiền", decodedNav);
    }
}
