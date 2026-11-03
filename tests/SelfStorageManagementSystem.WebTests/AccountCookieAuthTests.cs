using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.WebTests.Fixtures;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Bộ kiểm thử tích hợp toàn diện cho Task T05:
/// Cookie Login, Register, Logout, Forbidden, ValidatePrincipal, và Xử lý 401/403 cho AJAX/SignalR.
/// </summary>
public class AccountCookieAuthTests : IClassFixture<CookieAuthWebTestFixture>
{
    private readonly CookieAuthWebTestFixture _factory;

    public AccountCookieAuthTests(CookieAuthWebTestFixture factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(bool allowAutoRedirect = false)
    {
        return _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    #region 1. Kiểm thử Đăng nhập đầy đủ cả 5 vai trò (All Five Roles)

    [Theory]
    [InlineData(RoleConstants.StorageCustomer)]
    [InlineData(RoleConstants.FacilityStaff)]
    [InlineData(RoleConstants.FacilityManager)]
    [InlineData(RoleConstants.BusinessOperationsManager)]
    [InlineData(RoleConstants.SystemAdministrator)]
    public async Task Login_AllFiveRoles_Successful_SetsCookie_AndHasCorrectRoleClaim(string roleCode)
    {
        var email = $"role_{roleCode}_{Guid.NewGuid():N}@example.test";
        var password = "SecurePassword123!";
        var displayName = $"Người Dùng {roleCode}";

        var seededUser = await _factory.SeedUserAsync(email, password, roleCode, UserStatusConstants.Active, displayName);

        var client = CreateClient(allowAutoRedirect: false);

        // 1. Lấy Antiforgery token & cookie từ trang GET /Account/Login
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        // 2. Gửi yêu cầu POST /Account/Login
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken,
            ["ReturnUrl"] = "/"
        });

        var loginResponse = await client.SendAsync(loginRequest);

        // 3. Xác thực phản hồi 302 Redirect và nhận được Auth Cookie
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Equal("/", loginResponse.Headers.Location?.OriginalString);

        var setCookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var authCookie = setCookies.FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.NotNull(authCookie);
        Assert.Contains("HttpOnly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", authCookie, StringComparison.OrdinalIgnoreCase);

        // Đảm bảo TUYỆT ĐỐI không có cookie Role phụ ở dạng rõ
        Assert.DoesNotContain(setCookies, c => c.StartsWith("Role="));
        Assert.DoesNotContain(setCookies, c => c.StartsWith("UserName="));

        // 4. Sử dụng Auth Cookie để truy cập endpoint bảo mật /Account/SecuredData
        var cookieHeaderValue = authCookie.Split(';')[0];
        var securedRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/SecuredData");
        securedRequest.Headers.Add("Cookie", cookieHeaderValue);

        var securedResponse = await client.SendAsync(securedRequest);
        Assert.Equal(HttpStatusCode.OK, securedResponse.StatusCode);

        var json = await securedResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("success", root.GetProperty("status").GetString());
        Assert.Equal(seededUser.id.ToString(), root.GetProperty("userId").GetString());
        Assert.Equal(displayName, root.GetProperty("name").GetString());

        var rolesArray = root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains(roleCode, rolesArray);
    }

    #endregion

    #region 2. Kiểm thử Sai mật khẩu & Tài khoản bị khóa/vô hiệu hóa

    [Fact]
    public async Task Login_InvalidPassword_FailsWith200AndErrorMessage_NoAuthCookie()
    {
        var email = $"invalid_pw_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        await _factory.SeedUserAsync(email, password, RoleConstants.StorageCustomer);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = "WrongPassword999!",
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var response = await client.SendAsync(loginRequest);

        // Hiển thị lại form Login (200 OK) kèm thông báo lỗi
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Tên đăng nhập hoặc mật khẩu không chính xác", decoded);

        // Không cấp phát cookie xác thực
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            Assert.DoesNotContain(setCookies, c => c.Contains(CookieAuthExtensions.CookieName) && !c.Contains("expires="));
        }
    }

    [Theory]
    [InlineData(UserStatusConstants.Locked)]
    [InlineData(UserStatusConstants.Disabled)]
    public async Task Login_LockedOrDisabledUser_FailsWith200AndErrorMessage_NoAuthCookie(string inactiveStatus)
    {
        var email = $"locked_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        await _factory.SeedUserAsync(email, password, RoleConstants.StorageCustomer, status: inactiveStatus);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var response = await client.SendAsync(loginRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Tên đăng nhập hoặc mật khẩu không chính xác", decoded);

        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            Assert.DoesNotContain(setCookies, c => c.Contains(CookieAuthExtensions.CookieName) && !c.Contains("expires="));
        }
    }

    [Theory]
    [InlineData(EmploymentStatusConstants.Leave)]
    [InlineData(EmploymentStatusConstants.Terminated)]
    public async Task Login_EmployeeEmploymentInactive_IsRejected(string employmentStatus)
    {
        var email = $"inactive_emp_login_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        await _factory.SeedUserAsync(
            email,
            password,
            RoleConstants.FacilityStaff,
            status: UserStatusConstants.Active,
            employmentStatus: employmentStatus);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var loginResponse = await client.SendAsync(loginRequest);

        // Đăng nhập thất bại: trả về lại View Login (HTTP 200 OK)
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Không phát hành auth cookie hợp lệ
        if (loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            var authCookieHeader = setCookies.FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
            if (authCookieHeader != null)
            {
                Assert.True(authCookieHeader.Contains("expires=") || authCookieHeader.Contains("max-age=0"));
            }
        }

        var html = WebUtility.HtmlDecode(await loginResponse.Content.ReadAsStringAsync());
        Assert.Contains("Tên đăng nhập hoặc mật khẩu không chính xác", html);
    }

    #endregion

    #region 3. Kiểm thử ValidatePrincipal (User bị khóa sau đăng nhập & Role đổi sau đăng nhập)

    [Fact]
    public async Task ValidatePrincipal_UserDisabledAfterLogin_RejectsPrincipalAndRedirectsToLogin()
    {
        var email = $"lock_after_login_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        var seededUser = await _factory.SeedUserAsync(email, password, RoleConstants.StorageCustomer);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var loginResponse = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var authCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains(CookieAuthExtensions.CookieName))
            .Split(';')[0];

        // Khóa người dùng trong DB ngay sau khi họ đã nhận cookie
        using (var db = _factory.CreateDbContext())
        {
            var userInDb = await db.users.FirstAsync(u => u.id == seededUser.id);
            userInDb.status = UserStatusConstants.Locked;
            await db.SaveChangesAsync();
        }

        // Gửi yêu cầu tiếp theo với cookie cũ tới endpoint yêu cầu xác thực /Account/Profile
        var profileRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/Profile");
        profileRequest.Headers.Add("Cookie", authCookie);

        var profileResponse = await client.SendAsync(profileRequest);

        // ValidatePrincipal từ chối principal và chuyển hướng về /Account/Login
        Assert.Equal(HttpStatusCode.Redirect, profileResponse.StatusCode);
        Assert.Contains("/Account/Login", profileResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ValidatePrincipal_RoleChangedInDb_DynamicallyUpdatesClaimsAndRenewsCookie()
    {
        var email = $"role_change_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        var seededUser = await _factory.SeedUserAsync(email, password, RoleConstants.FacilityStaff, displayName: "Nhân Viên Gốc");

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var loginResponse = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var authCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains(CookieAuthExtensions.CookieName))
            .Split(';')[0];

        // Thay đổi quyền trong DB: thăng chức nhân viên lên FacilityManager
        using (var db = _factory.CreateDbContext())
        {
            var managerRole = await db.roles.FirstAsync(r => r.code == RoleConstants.FacilityManager);
            var staffRole = await db.roles.FirstAsync(r => r.code == RoleConstants.FacilityStaff);

            var existingUserRole = await db.user_roles.FirstAsync(ur => ur.user_id == seededUser.id && ur.role_id == staffRole.id);
            db.user_roles.Remove(existingUserRole);

            db.user_roles.Add(new user_role
            {
                user_id = seededUser.id,
                role_id = managerRole.id,
                granted_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Gửi yêu cầu với cookie cũ tới endpoint /Account/SecuredData
        var request = new HttpRequestMessage(HttpMethod.Get, "/Account/SecuredData");
        request.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var roles = doc.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();

        // Quyền hạn đã được tự động đồng bộ sang facility_manager mà không cần đăng nhập lại
        Assert.Contains(RoleConstants.FacilityManager, roles);
        Assert.DoesNotContain(RoleConstants.FacilityStaff, roles);

        // ValidatePrincipal thiết lập ShouldRenew = true -> Server phát hành Set-Cookie mới
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            Assert.Contains(setCookies, c => c.Contains(CookieAuthExtensions.CookieName));
        }
    }

    [Fact]
    public async Task ValidatePrincipal_EmploymentBecomesTerminated_RejectsPrincipalAndSignsOut()
    {
        var email = $"emp_term_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        var seededUser = await _factory.SeedUserAsync(
            email,
            password,
            RoleConstants.FacilityStaff,
            status: UserStatusConstants.Active,
            employmentStatus: EmploymentStatusConstants.Active);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var loginResponse = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var authCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains(CookieAuthExtensions.CookieName))
            .Split(';')[0];

        // Cập nhật trạng thái nhân viên sang terminated trong DB khi đang giữ phiên cookie
        using (var db = _factory.CreateDbContext())
        {
            var empProfile = await db.employee_profiles.FirstAsync(ep => ep.user_id == seededUser.id);
            empProfile.employment_status = EmploymentStatusConstants.Terminated;
            await db.SaveChangesAsync();
        }

        // Gửi request kế tiếp với cookie cũ tới endpoint /Account/Profile
        var profileRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/Profile");
        profileRequest.Headers.Add("Cookie", authCookie);

        var profileResponse = await client.SendAsync(profileRequest);

        // ValidatePrincipal từ chối principal, sign out và chuyển hướng về /Account/Login
        Assert.Equal(HttpStatusCode.Redirect, profileResponse.StatusCode);
        Assert.Contains("/Account/Login", profileResponse.Headers.Location?.OriginalString);

        // Header Set-Cookie phải có lệnh xóa auth cookie (SignOutAsync)
        Assert.True(profileResponse.Headers.TryGetValues("Set-Cookie", out var setCookiesSignOut));
        var authSignOutCookie = setCookiesSignOut.FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.NotNull(authSignOutCookie);
        Assert.True(authSignOutCookie.Contains("expires=") || authSignOutCookie.Contains("max-age=0"));
    }

    [Fact]
    public async Task ValidatePrincipal_ActiveCustomerWithoutEmployeeProfile_RemainsAuthenticated()
    {
        var email = $"pure_customer_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        var seededUser = await _factory.SeedUserAsync(
            email,
            password,
            RoleConstants.StorageCustomer,
            status: UserStatusConstants.Active);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var loginResponse = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var authCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains(CookieAuthExtensions.CookieName))
            .Split(';')[0];

        // Khách hàng không có hồ sơ nhân sự vẫn duy trì phiên xác thực bình thường
        var profileRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/Profile");
        profileRequest.Headers.Add("Cookie", authCookie);

        var profileResponse = await client.SendAsync(profileRequest);
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
    }

    [Fact]
    public async Task ValidatePrincipal_TerminatedOperationsManager_LosesRoleAccess()
    {
        var email = $"ops_mgr_term_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        var seededUser = await _factory.SeedUserAsync(
            email,
            password,
            RoleConstants.BusinessOperationsManager,
            status: UserStatusConstants.Active,
            employmentStatus: EmploymentStatusConstants.Active);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var loginResponse = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var authCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains(CookieAuthExtensions.CookieName))
            .Split(';')[0];

        // Đảm bảo ban đầu truy cập được SecuredData
        var testReq1 = new HttpRequestMessage(HttpMethod.Get, "/Account/SecuredData");
        testReq1.Headers.Add("Cookie", authCookie);
        var testRes1 = await client.SendAsync(testReq1);
        Assert.Equal(HttpStatusCode.OK, testRes1.StatusCode);

        // Chấm dứt hợp đồng nhân sự của Operations Manager trong DB
        using (var db = _factory.CreateDbContext())
        {
            var empProfile = await db.employee_profiles.FirstAsync(ep => ep.user_id == seededUser.id);
            empProfile.employment_status = EmploymentStatusConstants.Terminated;
            await db.SaveChangesAsync();
        }

        // Gửi request kế tiếp tới SecuredData với cookie cũ
        var testReq2 = new HttpRequestMessage(HttpMethod.Get, "/Account/SecuredData");
        testReq2.Headers.Add("Cookie", authCookie);
        var testRes2 = await client.SendAsync(testReq2);

        // ValidatePrincipal từ chối principal -> không được truy cập SecuredData -> chuyển hướng về Login
        Assert.Equal(HttpStatusCode.Redirect, testRes2.StatusCode);
        Assert.Contains("/Account/Login", testRes2.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ValidatePrincipal_TerminatedSystemAdministrator_LosesRoleAccess()
    {
        var email = $"sys_admin_term_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        var seededUser = await _factory.SeedUserAsync(
            email,
            password,
            RoleConstants.SystemAdministrator,
            status: UserStatusConstants.Active,
            employmentStatus: EmploymentStatusConstants.Active);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var loginResponse = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var authCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains(CookieAuthExtensions.CookieName))
            .Split(';')[0];

        // Xác nhận ban đầu Admin truy cập thành công endpoint độc quyền AdminCheck
        var adminReq1 = new HttpRequestMessage(HttpMethod.Get, "/Account/AdminCheck");
        adminReq1.Headers.Add("Cookie", authCookie);
        var adminRes1 = await client.SendAsync(adminReq1);
        Assert.Equal(HttpStatusCode.OK, adminRes1.StatusCode);

        // Chấm dứt hợp đồng nhân sự của Admin trong DB: không có ngoại lệ cho Admin!
        using (var db = _factory.CreateDbContext())
        {
            var empProfile = await db.employee_profiles.FirstAsync(ep => ep.user_id == seededUser.id);
            empProfile.employment_status = EmploymentStatusConstants.Terminated;
            await db.SaveChangesAsync();
        }

        // Gửi request kế tiếp tới AdminCheck
        var adminReq2 = new HttpRequestMessage(HttpMethod.Get, "/Account/AdminCheck");
        adminReq2.Headers.Add("Cookie", authCookie);
        var adminRes2 = await client.SendAsync(adminReq2);

        // ValidatePrincipal ngay lập tức thu hồi principal và sign out -> chuyển hướng về Login
        Assert.Equal(HttpStatusCode.Redirect, adminRes2.StatusCode);
        Assert.Contains("/Account/Login", adminRes2.Headers.Location?.OriginalString);
    }

    #endregion

    #region 4. Kiểm thử Cấu hình Cookie (5 phút, sliding expiration, HttpOnly, SameSite=Lax)

    [Fact]
    public void CookieConfiguration_Has5MinuteLifetime_SlidingExpiration_HttpOnly_SameSiteLax()
    {
        using var scope = _factory.Services.CreateScope();
        var optionsMonitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var options = optionsMonitor.Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal(TimeSpan.FromMinutes(5), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal("SelfStoragePRN222.Web.Auth", options.Cookie.Name);
        Assert.Equal(new Microsoft.AspNetCore.Http.PathString("/Account/Login"), options.LoginPath);
        Assert.Equal(new Microsoft.AspNetCore.Http.PathString("/Account/Forbidden"), options.AccessDeniedPath);
    }

    #endregion

    #region 5. Kiểm thử Chống Open Redirect (Chỉ chấp nhận ReturnUrl nội bộ)

    [Theory]
    [InlineData("https://evil.com/phish")]
    [InlineData("http://attacker.example.com")]
    [InlineData("//malicious-site.com/exploit")]
    [InlineData("javascript:alert(1)")]
    public async Task Login_ExternalReturnUrl_PreventsOpenRedirect_RedirectsToHome(string externalUrl)
    {
        var email = $"open_redirect_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        await _factory.SeedUserAsync(email, password, RoleConstants.StorageCustomer);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, $"/Account/Login?ReturnUrl={Uri.EscapeDataString(externalUrl)}");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken,
            ["ReturnUrl"] = externalUrl
        });

        var response = await client.SendAsync(loginRequest);

        // Chuyển hướng an toàn về trang chủ nội bộ ("/"), tuyệt đối không chuyển hướng sang externalUrl
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_ValidLocalReturnUrl_RedirectsToTargetLocalUrl()
    {
        var email = $"local_redirect_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        await _factory.SeedUserAsync(email, password, RoleConstants.StorageCustomer);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var localUrl = "/Catalog";
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, $"/Account/Login?ReturnUrl={Uri.EscapeDataString(localUrl)}");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken,
            ["ReturnUrl"] = localUrl
        });

        var response = await client.SendAsync(loginRequest);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(localUrl, response.Headers.Location?.OriginalString);
    }

    #endregion

    #region 6. Kiểm thử Antiforgery Token (Thiếu token trên POST Login & Logout)

    [Fact]
    public async Task Login_PostWithoutAntiforgeryToken_ReturnsBadRequest400()
    {
        var client = CreateClient(allowAutoRedirect: false);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = "anyuser@example.test",
            ["Password"] = "anypassword"
        });

        var response = await client.SendAsync(request);

        // Antiforgery middleware chặn đứng và trả về 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_PostWithoutAntiforgeryToken_ReturnsBadRequest400()
    {
        var client = CreateClient(allowAutoRedirect: false);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Logout");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region 7. Kiểm thử GET Logout không làm thay đổi trạng thái xác thực & POST Logout hủy phiên

    [Fact]
    public async Task Logout_GetMethod_DoesNotSignOut_ReturnsConfirmationView()
    {
        var email = $"logout_get_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        await _factory.SeedUserAsync(email, password, RoleConstants.StorageCustomer, displayName: "Khách Logout");

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var loginResponse = await client.SendAsync(loginRequest);
        var authCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains(CookieAuthExtensions.CookieName))
            .Split(';')[0];

        // Gửi GET /Account/Logout
        var getLogoutRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/Logout");
        getLogoutRequest.Headers.Add("Cookie", authCookie);

        var getLogoutResponse = await client.SendAsync(getLogoutRequest);

        // Trả về trang xác nhận 200 OK (View)
        Assert.Equal(HttpStatusCode.OK, getLogoutResponse.StatusCode);
        var html = await getLogoutResponse.Content.ReadAsStringAsync();
        Assert.Contains("Xác Nhận Đăng Xuất", html);

        // Phiên xác thực vẫn còn nguyên vẹn! Gửi yêu cầu tiếp theo vẫn 200 OK
        var checkRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/SecuredData");
        checkRequest.Headers.Add("Cookie", authCookie);

        var checkResponse = await client.SendAsync(checkRequest);
        Assert.Equal(HttpStatusCode.OK, checkResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_PostMethod_SignsOutAndClearsSession()
    {
        var email = $"logout_post_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        await _factory.SeedUserAsync(email, password, RoleConstants.StorageCustomer);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var loginResponse = await client.SendAsync(loginRequest);
        var authCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains(CookieAuthExtensions.CookieName))
            .Split(';')[0];

        // Lấy antiforgery token từ trang Logout
        var getLogout = new HttpRequestMessage(HttpMethod.Get, "/Account/Logout");
        getLogout.Headers.Add("Cookie", authCookie);
        var getLogoutResponse = await client.SendAsync(getLogout);
        var logoutHtml = await getLogoutResponse.Content.ReadAsStringAsync();

        var tokenMatch = System.Text.RegularExpressions.Regex.Match(logoutHtml, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        Assert.True(tokenMatch.Success);
        var logoutToken = tokenMatch.Groups[1].Value;

        // Gửi POST /Account/Logout có Antiforgery token
        var postLogoutRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Logout");
        postLogoutRequest.Headers.Add("Cookie", $"{authCookie}; {antiforgeryCookie}");
        postLogoutRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = logoutToken
        });

        var logoutResponse = await client.SendAsync(postLogoutRequest);

        // Chuyển hướng về /Account/Login
        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.Equal("/Account/Login", logoutResponse.Headers.Location?.OriginalString);

        // Gửi lại cookie cũ tới endpoint /Account/Profile -> Bị từ chối vì đã đăng xuất
        var afterLogoutRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/Profile");
        if (logoutResponse.Headers.TryGetValues("Set-Cookie", out var expiredCookies))
        {
            var expiredCookie = expiredCookies.FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
            if (expiredCookie != null)
            {
                afterLogoutRequest.Headers.Add("Cookie", expiredCookie.Split(';')[0]);
            }
        }

        var afterLogoutResponse = await client.SendAsync(afterLogoutRequest);
        Assert.Equal(HttpStatusCode.Redirect, afterLogoutResponse.StatusCode);
        Assert.Contains("/Account/Login", afterLogoutResponse.Headers.Location?.OriginalString);
    }

    #endregion

    #region 8. Kiểm thử AJAX & SignalR nhận 401/403 thay vì HTML redirect

    [Fact]
    public async Task SecuredEndpoint_AjaxRequestWithoutAuth_Returns401JsonNotRedirect()
    {
        var client = CreateClient(allowAutoRedirect: false);

        // Thử nghiệm với Header X-Requested-With: XMLHttpRequest
        var ajaxRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/Profile");
        ajaxRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var response = await client.SendAsync(ajaxRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Null(response.Headers.Location);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("401", body);
        Assert.Contains("Unauthorized", body);

        // Thử nghiệm với Header Accept: application/json
        var jsonRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/Profile");
        jsonRequest.Headers.Add("Accept", "application/json");

        var jsonResponse = await client.SendAsync(jsonRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, jsonResponse.StatusCode);
        Assert.Equal("application/json; charset=utf-8", jsonResponse.Content.Headers.ContentType?.ToString());
        Assert.Null(jsonResponse.Headers.Location);
    }

    [Fact]
    public async Task SecuredEndpoint_BrowserRequestWithoutAuth_Returns302RedirectToLogin()
    {
        var client = CreateClient(allowAutoRedirect: false);

        // Trình duyệt thông thường không gửi header AJAX/JSON
        var browserRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/Profile");

        var response = await client.SendAsync(browserRequest);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login?ReturnUrl=%2FAccount%2FProfile", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AdminEndpoint_AjaxRequestWithWrongRole_Returns403JsonNotRedirect()
    {
        var email = $"customer_denied_ajax_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        await _factory.SeedUserAsync(email, password, RoleConstants.StorageCustomer);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var loginResponse = await client.SendAsync(loginRequest);
        var authCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains(CookieAuthExtensions.CookieName))
            .Split(';')[0];

        // Khách hàng thông thường truy cập endpoint Admin qua AJAX
        var ajaxAdminRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/AdminCheck");
        ajaxAdminRequest.Headers.Add("Cookie", authCookie);
        ajaxAdminRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var response = await client.SendAsync(ajaxAdminRequest);

        // Trả về 403 Forbidden dạng JSON thay vì HTML redirect
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Null(response.Headers.Location);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("403", body);
        Assert.Contains("Forbidden", body);
    }

    [Fact]
    public async Task AdminEndpoint_BrowserRequestWithWrongRole_Returns302RedirectToForbidden()
    {
        var email = $"customer_denied_browser_{Guid.NewGuid():N}@example.test";
        var password = "CorrectPassword123!";
        await _factory.SeedUserAsync(email, password, RoleConstants.StorageCustomer);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var loginResponse = await client.SendAsync(loginRequest);
        var authCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains(CookieAuthExtensions.CookieName))
            .Split(';')[0];

        // Trình duyệt thông thường truy cập endpoint Admin
        var browserAdminRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/AdminCheck");
        browserAdminRequest.Headers.Add("Cookie", authCookie);

        var response = await client.SendAsync(browserAdminRequest);

        // Chuyển hướng 302 về /Account/Forbidden
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Forbidden", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public void IsAjaxOrApiOrSignalR_RecognizesAllRequiredRequestPatterns()
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        // 1. Bình thường
        Assert.False(CookieAuthExtensions.IsAjaxOrApiOrSignalR(httpContext.Request));

        // 2. XMLHttpRequest
        httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        Assert.True(CookieAuthExtensions.IsAjaxOrApiOrSignalR(httpContext.Request));
        httpContext.Request.Headers.Remove("X-Requested-With");

        // 3. Accept application/json
        httpContext.Request.Headers["Accept"] = "application/json";
        Assert.True(CookieAuthExtensions.IsAjaxOrApiOrSignalR(httpContext.Request));
        httpContext.Request.Headers.Remove("Accept");

        // 4. SignalR Hub path (/storageUnitHub/negotiate)
        httpContext.Request.Path = "/storageUnitHub/negotiate";
        Assert.True(CookieAuthExtensions.IsAjaxOrApiOrSignalR(httpContext.Request));

        // 5. API path (/api/v1/units)
        httpContext.Request.Path = "/api/v1/units";
        Assert.True(CookieAuthExtensions.IsAjaxOrApiOrSignalR(httpContext.Request));
    }

    #endregion

    #region 9. Kiểm thử Đăng ký khách hàng (Customer Registration)

    [Fact]
    public async Task Register_ValidCustomer_CreatesUserAndRedirectsToLogin()
    {
        await _factory.SeedDefaultRolesAsync();
        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Register");

        var newEmail = $"new_customer_{Guid.NewGuid():N}@example.test";
        var password = "NewCustomerPass123!";
        var fullName = "Khách Hàng Mới Đăng Ký";

        var registerRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Register");
        registerRequest.Headers.Add("Cookie", antiforgeryCookie);
        registerRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = fullName,
            ["Email"] = newEmail,
            ["Password"] = password,
            ["ConfirmPassword"] = password,
            ["PhoneNumber"] = "0987112233",
            ["IdentityNumber"] = "079200112233",
            ["Address"] = "123 Đường Số 1, Quận 1, TP.HCM",
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var response = await client.SendAsync(registerRequest);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);

        // Kiểm tra tài khoản đã tồn tại trong DB với Role storage_customer
        using var db = _factory.CreateDbContext();
        var userInDb = await db.users
            .Include(u => u.customer_profile)
            .Include(u => u.user_roleusers)
                .ThenInclude(ur => ur.role)
            .FirstOrDefaultAsync(u => u.email == newEmail);

        Assert.NotNull(userInDb);
        Assert.Equal(UserStatusConstants.Active, userInDb.status);
        Assert.Equal(fullName, userInDb.customer_profile?.full_name);
        Assert.Contains(userInDb.user_roleusers, ur => ur.role.code == RoleConstants.StorageCustomer);
    }

    [Fact]
    public async Task Register_DuplicateEmail_FailsWithErrorMessage()
    {
        var existingEmail = $"duplicate_{Guid.NewGuid():N}@example.test";
        await _factory.SeedUserAsync(existingEmail, "SomePass123!", RoleConstants.StorageCustomer);

        var client = CreateClient(allowAutoRedirect: false);
        var (antiforgeryToken, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Register");

        var registerRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Register");
        registerRequest.Headers.Add("Cookie", antiforgeryCookie);
        registerRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Người Trùng Email",
            ["Email"] = existingEmail,
            ["Password"] = "SomePass123!",
            ["ConfirmPassword"] = "SomePass123!",
            ["__RequestVerificationToken"] = antiforgeryToken
        });

        var response = await client.SendAsync(registerRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("already registered", html, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 10. Độc lập hoàn toàn khỏi JWT (Zero JWT Verification)

    [Fact]
    public void CookieAuth_DoesNotResolveJwt_IndependentArchitecture()
    {
        using var scope = _factory.Services.CreateScope();

        // Không có dịch vụ cấp phát token JWT nào được giải quyết trong Web Host
        var jwtGenerator = scope.ServiceProvider.GetService<IJwtTokenGenerator>();
        Assert.Null(jwtGenerator);

        // Dịch vụ IAccountIdentityService được cung cấp đầy đủ
        var accountIdentityService = scope.ServiceProvider.GetService<IAccountIdentityService>();
        Assert.NotNull(accountIdentityService);
    }

    #endregion
}
