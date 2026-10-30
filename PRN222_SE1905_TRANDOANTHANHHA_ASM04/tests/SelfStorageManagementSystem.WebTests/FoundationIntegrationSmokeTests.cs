using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.Web.Hubs;
using SelfStorageManagementSystem.WebTests.Fixtures;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Comprehensive foundation integration smoke tests for Task T08:
/// 1. Public Pages Smoke Test: Landing, Catalog, Login, Register, Error, and StatusCode pages.
/// 2. Zero JWT Secret Startup: Host boots cleanly and resolves all core domain services without JWT key/secret.
/// 3. Dependency Injection Safety: Verified service lifetimes, no duplicate registrations, no scoped leaks into singletons.
/// 4. Cookie -> Service -> Repository -> EF Core InMemory Pipeline: Full auth roundtrip with facility scopes & dynamic renewal.
/// 5. Authorization Policies: Strict verification of CustomerOnly, StaffOnly, ManagerOnly, AdminOnly against 5 roles.
/// 6. Architectural Boundary: MvcWebApp does NOT reference DataAccess/Presentation/Wpf, PageModels/Controllers/Hubs have no DbContext/Repository.
/// 7. WebTests Host Decoupling: WebTests uses Web Program without type collision with API Presentation Program.
/// </summary>
public class FoundationIntegrationSmokeTests : IClassFixture<CookieAuthWebTestFixture>
{
    private readonly CookieAuthWebTestFixture _factory;

    public FoundationIntegrationSmokeTests(CookieAuthWebTestFixture factory)
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

    #region 1. Public Pages Smoke Tests (HTTP Smoke)

    [Theory]
    [InlineData("/", "SelfStorage", "PRN222")]
    [InlineData("/Catalog", "Danh Mục Cơ Sở", "Bộ lọc")]
    [InlineData("/Account/Login", "Đăng Nhập", "__RequestVerificationToken")]
    [InlineData("/Account/Register", "Đăng Ký", "__RequestVerificationToken")]
    public async Task PublicPages_SmokeTest_ReturnOkAndExpectedContent(string url, string expectedContent1, string expectedContent2)
    {
        var client = CreateClient(allowAutoRedirect: false);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Contains("text/html", response.Content.Headers.ContentType.ToString());

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedContent1, html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedContent2, html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/Home/StatusCode/404", HttpStatusCode.NotFound, "404")]
    [InlineData("/Home/StatusCode/403", HttpStatusCode.Forbidden, "403")]
    [InlineData("/Home/StatusCode/401", HttpStatusCode.Unauthorized, "401")]
    public async Task ErrorPages_StatusCodeReExecute_ReturnsAccurateStatusCodeAndErrorView(
        string url, HttpStatusCode expectedStatus, string expectedText)
    {
        var client = CreateClient(allowAutoRedirect: false);

        var response = await client.GetAsync(url);

        Assert.Equal(expectedStatus, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedText, html);
        Assert.Contains("Thông Báo Trạng Thái", html);
    }

    [Fact]
    public async Task ErrorPages_CentralizedExceptionHandler_HandlesGetAndPost_Preserves500()
    {
        var client = CreateClient(allowAutoRedirect: false);

        var getResponse = await client.GetAsync("/Home/Error");
        Assert.Equal(HttpStatusCode.InternalServerError, getResponse.StatusCode);
        var getHtml = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("500", getHtml);
        Assert.Contains("Lỗi Xử Lý Yêu Cầu", getHtml);

        var postResponse = await client.PostAsync("/Home/Error", null);
        Assert.Equal(HttpStatusCode.InternalServerError, postResponse.StatusCode);
        var postHtml = await postResponse.Content.ReadAsStringAsync();
        Assert.Contains("500", postHtml);
        Assert.Contains("Lỗi Xử Lý Yêu Cầu", postHtml);
    }

    #endregion

    #region 2. Zero JWT Secret Startup & Core Service Resolution

    [Fact]
    public void Startup_WithoutJwtSecret_ResolvesAllDomainServices_AndExcludesJwtGenerator()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // Verify that JWT token generator is NOT registered in Web container
        var jwtGenerator = sp.GetService<IJwtTokenGenerator>();
        Assert.Null(jwtGenerator);

        // Verify all 13 core domain services resolve cleanly without any JWT configuration
        Assert.NotNull(sp.GetRequiredService<IAccountIdentityService>());
        Assert.NotNull(sp.GetRequiredService<IStorageUnitAdminService>());
        Assert.NotNull(sp.GetRequiredService<IFacilityCatalogService>());
        Assert.NotNull(sp.GetRequiredService<IReservationService>());
        Assert.NotNull(sp.GetRequiredService<IPaymentService>());
        Assert.NotNull(sp.GetRequiredService<ICheckInService>());
        Assert.NotNull(sp.GetRequiredService<IAdminAccountService>());
        Assert.NotNull(sp.GetRequiredService<IRentalAgreementService>());
        Assert.NotNull(sp.GetRequiredService<IInvoiceService>());
        Assert.NotNull(sp.GetRequiredService<IFacilityScopeService>());
        Assert.NotNull(sp.GetRequiredService<IAuditService>());
        Assert.NotNull(sp.GetRequiredService<IPasswordHasher>());
        Assert.NotNull(sp.GetRequiredService<TimeProvider>());
    }

    #endregion

    #region 3. Dependency Injection Lifetime & Duplicate Registration Check

    [Fact]
    public void DependencyInjection_ServiceLifetimes_AreCorrect_AndNoDuplicates()
    {
        // Build an independent service collection using the same Web composition method
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=dummy;Database=dummy;"
            })
            .Build();

        services.AddSelfStorageWebComposition(config);

        // Check for duplicate service registrations
        var coreServiceTypes = new[]
        {
            typeof(IAccountIdentityService),
            typeof(IStorageUnitAdminService),
            typeof(IFacilityCatalogService),
            typeof(IReservationService),
            typeof(IPaymentService),
            typeof(ICheckInService),
            typeof(IAdminAccountService),
            typeof(IRentalAgreementService),
            typeof(IInvoiceService),
            typeof(IFacilityScopeService),
            typeof(IAuditService),
            typeof(IPasswordHasher),
            typeof(TimeProvider)
        };

        foreach (var svcType in coreServiceTypes)
        {
            var registrations = services.Where(sd => sd.ServiceType == svcType).ToList();
            Assert.True(registrations.Count == 1,
                $"Dịch vụ {svcType.Name} bị đăng ký trùng lặp {registrations.Count} lần trong DI container.");
        }

        // Verify singletons
        var timeProviderDesc = services.First(sd => sd.ServiceType == typeof(TimeProvider));
        Assert.Equal(ServiceLifetime.Singleton, timeProviderDesc.Lifetime);

        var hasherDesc = services.First(sd => sd.ServiceType == typeof(IPasswordHasher));
        Assert.Equal(ServiceLifetime.Singleton, hasherDesc.Lifetime);

        // Verify scoped services
        var scopedTypes = new[]
        {
            typeof(IAccountIdentityService),
            typeof(IStorageUnitAdminService),
            typeof(IFacilityCatalogService),
            typeof(IReservationService),
            typeof(IPaymentService),
            typeof(ICheckInService),
            typeof(IAdminAccountService),
            typeof(IRentalAgreementService),
            typeof(IInvoiceService),
            typeof(IFacilityScopeService),
            typeof(IAuditService)
        };

        foreach (var svcType in scopedTypes)
        {
            var desc = services.First(sd => sd.ServiceType == svcType);
            Assert.Equal(ServiceLifetime.Scoped, desc.Lifetime);
        }
    }

    #endregion

    #region 4. Full Pipeline: Cookie -> Service -> Repository -> EF Core InMemory With Facility Scopes

    [Fact]
    public async Task FullPipeline_CookieToServiceToRepositoryToEfInMemory_WithFacilityScopesAndRenewal()
    {
        var email = $"staff_pipeline_{Guid.NewGuid():N}@example.test";
        var password = "SecureStaffPass123!";
        var displayName = "Staff Pipeline User";
        var empCode = $"EMP_PL_{Guid.NewGuid():N}"[..10];

        // 1. Seed Facility in database
        long facilityId;
        var facilityCode = $"FAC_{Guid.NewGuid():N}"[..10];
        var facilityName = "Hanoi Central Storage Facility";
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var facility = new facility
            {
                code = facilityCode,
                name = facilityName,
                address_line = "456 Duong Cau Giay",
                city = "Ha Noi",
                district = "Cau Giay",
                timezone = "Asia/Ho_Chi_Minh",
                status = "operational",
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.facilities.Add(facility);
            await db.SaveChangesAsync();
            facilityId = facility.id;
        }

        // 2. Seed Employee User with active status and role facility_staff
        var user = await _factory.SeedUserAsync(
            email, password, RoleConstants.FacilityStaff,
            UserStatusConstants.Active, displayName, empCode, employmentStatus: "active");

        // 3. Seed active staff facility assignment
        long assignmentId;
        using (var assignScope = _factory.Services.CreateScope())
        {
            var db = assignScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var assignment = new staff_facility_assignment
            {
                employee_id = user.id,
                facility_id = facilityId,
                assignment_role = "facility_staff",
                starts_at = DateTimeOffset.UtcNow.AddDays(-1),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            };
            db.staff_facility_assignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.id;
        }

        // 4. Perform Login via HTTP POST /Account/Login with Antiforgery Token
        var client = CreateClient(allowAutoRedirect: false);
        var (token, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginMsg = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginMsg.Headers.Add("Cookie", antiforgeryCookie);
        loginMsg.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/"
        });

        var loginResp = await client.SendAsync(loginMsg);
        Assert.Equal(HttpStatusCode.Redirect, loginResp.StatusCode);

        var authCookieHeader = loginResp.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.NotNull(authCookieHeader);
        var rawCookie = authCookieHeader!.Split(';')[0];

        // 5. Test Profile Page: Full Cookie -> OnValidatePrincipal -> Service -> Repository -> Database pipeline
        var profileMsg = new HttpRequestMessage(HttpMethod.Get, "/Account/Profile");
        profileMsg.Headers.Add("Cookie", rawCookie);

        var profileResp = await client.SendAsync(profileMsg);
        Assert.Equal(HttpStatusCode.OK, profileResp.StatusCode);
        var profileHtml = await profileResp.Content.ReadAsStringAsync();

        // Verify HTML displays user details and assigned facility scope from persistence
        Assert.Contains(displayName, profileHtml);
        Assert.Contains(empCode, profileHtml);
        Assert.Contains(facilityCode, profileHtml);
        Assert.Contains(facilityName, profileHtml);

        // 6. Test SecuredData JSON endpoint: Verify claims in authenticated principal
        var securedMsg = new HttpRequestMessage(HttpMethod.Get, "/Account/SecuredData");
        securedMsg.Headers.Add("Cookie", rawCookie);

        var securedResp = await client.SendAsync(securedMsg);
        Assert.Equal(HttpStatusCode.OK, securedResp.StatusCode);
        var securedJson = await securedResp.Content.ReadAsStringAsync();
        Assert.Contains("facility_staff", securedJson);
        Assert.Contains(user.id.ToString(), securedJson);

        // 7. Test Dynamic Scope Revocation & Cookie Renewal:
        // Terminate the facility assignment in DB persistence
        using (var terminateScope = _factory.Services.CreateScope())
        {
            var db = terminateScope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var sfa = await db.staff_facility_assignments.FindAsync(assignmentId);
            Assert.NotNull(sfa);
            sfa!.ends_at = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        // Send a new request with the original cookie.
        // OnValidatePrincipal reads DB snapshot -> detects active assignment is gone -> shouldRenew = true.
        var renewMsg = new HttpRequestMessage(HttpMethod.Get, "/Account/SecuredData");
        renewMsg.Headers.Add("Cookie", rawCookie);

        var renewResp = await client.SendAsync(renewMsg);
        Assert.Equal(HttpStatusCode.OK, renewResp.StatusCode);

        // Verify new renewed Set-Cookie header is issued
        var renewedCookies = renewResp.Headers.TryGetValues("Set-Cookie", out var rCookies)
            ? rCookies.ToList()
            : new List<string>();

        var renewedAuthCookie = renewedCookies.FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.NotNull(renewedAuthCookie);
    }

    #endregion

    #region 5. Authorization Policy Matrix Verification

    [Theory]
    [InlineData(RoleConstants.StorageCustomer, true, false, false, false)]
    [InlineData(RoleConstants.FacilityStaff, false, true, false, false)]
    [InlineData(RoleConstants.FacilityManager, false, true, true, false)]
    [InlineData(RoleConstants.BusinessOperationsManager, false, true, true, false)]
    [InlineData(RoleConstants.SystemAdministrator, false, true, true, true)]
    public async Task AuthorizationPolicies_EnforceExactRoleMatrix(
        string roleCode, bool expectCustomer, bool expectStaff, bool expectManager, bool expectAdmin)
    {
        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "9999"),
            new(ClaimTypes.Name, $"Test User {roleCode}"),
            new(ClaimTypes.Role, roleCode)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthExtensions.CookieScheme);
        var principal = new ClaimsPrincipal(identity);

        var customerResult = await authService.AuthorizeAsync(principal, null, "CustomerOnly");
        var staffResult = await authService.AuthorizeAsync(principal, null, "StaffOnly");
        var managerResult = await authService.AuthorizeAsync(principal, null, "ManagerOnly");
        var adminResult = await authService.AuthorizeAsync(principal, null, "AdminOnly");

        Assert.Equal(expectCustomer, customerResult.Succeeded);
        Assert.Equal(expectStaff, staffResult.Succeeded);
        Assert.Equal(expectManager, managerResult.Succeeded);
        Assert.Equal(expectAdmin, adminResult.Succeeded);
    }

    #endregion

    #region 6. Architectural Boundary Verification

    [Fact]
    public void Architecture_MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf()
    {
        var webAssembly = typeof(Program).Assembly;
        var referencedAssemblies = webAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        // Must reference BusinessLogic
        Assert.Contains("SelfStorageManagementSystem.BusinessLogic", referencedAssemblies);

        // Must NOT reference DataAccess, Presentation, or WpfClient directly
        Assert.DoesNotContain("SelfStorageManagementSystem.DataAccess", referencedAssemblies);
        Assert.DoesNotContain("SelfStorageManagementSystem.Presentation", referencedAssemblies);
        Assert.DoesNotContain("SelfStorageManagementSystem.WpfClient", referencedAssemblies);
    }

    [Fact]
    public void Architecture_PageModelsControllersAndHubs_DoNotInjectDbContextOrRepositories()
    {
        var webAssembly = typeof(Program).Assembly;
        var uiTypes = webAssembly.GetTypes()
            .Where(t =>
                typeof(PageModel).IsAssignableFrom(t) ||
                typeof(ControllerBase).IsAssignableFrom(t) ||
                typeof(Hub).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(uiTypes);
        Assert.Contains(uiTypes, type => type == typeof(StorageUnitHub));

        foreach (var type in uiTypes)
        {
            // Inspect all constructors
            foreach (var ctor in type.GetConstructors())
            {
                foreach (var param in ctor.GetParameters())
                {
                    var paramType = param.ParameterType;
                    var fullName = paramType.FullName ?? string.Empty;

                    Assert.False(
                        fullName.StartsWith("SelfStorageManagementSystem.DataAccess") ||
                        typeof(DbContext).IsAssignableFrom(paramType) ||
                        fullName.Contains("Repository"),
                        $"Lớp UI '{type.FullName}' vi phạm phân tầng: inject '{paramType.FullName}' trực tiếp qua constructor.");
                }
            }

            // Inspect all fields
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                var fieldType = field.FieldType;
                var fullName = fieldType.FullName ?? string.Empty;

                Assert.False(
                    fullName.StartsWith("SelfStorageManagementSystem.DataAccess") ||
                    typeof(DbContext).IsAssignableFrom(fieldType) ||
                    fullName.Contains("Repository"),
                    $"Lớp UI '{type.FullName}' vi phạm phân tầng: chứa trường '{field.Name}' thuộc kiểu '{fieldType.FullName}'.");
            }

            // Inspect all properties
            foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                var propType = prop.PropertyType;
                var fullName = propType.FullName ?? string.Empty;

                Assert.False(
                    fullName.StartsWith("SelfStorageManagementSystem.DataAccess") ||
                    typeof(DbContext).IsAssignableFrom(propType) ||
                    fullName.Contains("Repository"),
                    $"Lớp UI '{type.FullName}' vi phạm phân tầng: chứa thuộc tính '{prop.Name}' thuộc kiểu '{propType.FullName}'.");
            }
        }
    }

    #endregion

    #region 7. WebTests Host Decoupling Verification

    [Fact]
    public void WebTests_EntrypointDecoupledFromApiPresentationProgram()
    {
        // Verify the Program type used by WebTests belongs exclusively to SelfStorageManagementSystem.Web
        var programType = typeof(Program);
        Assert.Equal("SelfStorageManagementSystem.Web", programType.Assembly.GetName().Name);

        // Verify that Presentation assembly is not referenced by WebTests assembly
        var testReferencedAssemblies = typeof(FoundationIntegrationSmokeTests).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        Assert.DoesNotContain("SelfStorageManagementSystem.Presentation", testReferencedAssemblies);
    }

    #endregion

    #region 8. SignalR StorageUnitHub Security Tests (Task T08-F01)

    [Fact]
    public void StorageUnitHub_HasAuthorizeAttribute()
    {
        var hubType = typeof(SelfStorageManagementSystem.Web.Hubs.StorageUnitHub);
        var authorizeAttr = hubType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorizeAttr);
    }

    [Fact]
    public async Task StorageUnitHub_AnonymousNegotiate_IsRejected()
    {
        var client = CreateClient(allowAutoRedirect: false);

        var response = await client.PostAsync("/storageUnitHub/negotiate?negotiateVersion=1", null);

        // Kết quả không được là HTTP 200
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        // Nếu Cookie middleware trả Redirect, kiểm tra Location trỏ về /Account/Login.
        // Nếu cấu hình trả 401, chấp nhận HTTP 401 (do IsAjaxOrApiOrSignalR trả 401).
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.NotNull(response.Headers.Location);
            Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task StorageUnitHub_AuthenticatedCookie_NegotiateSucceeds()
    {
        var email = $"hub_user_{Guid.NewGuid():N}@example.test";
        var password = "SecureHubPassword123!";
        var displayName = "Hub Test User";

        // 1. Seed user active
        await _factory.SeedUserAsync(email, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, displayName);

        var client = CreateClient(allowAutoRedirect: false);

        // 2. Đăng nhập qua /Account/Login với antiforgery token
        var (token, antiforgeryCookie) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var loginMsg = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        loginMsg.Headers.Add("Cookie", antiforgeryCookie);
        loginMsg.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/"
        });

        var loginResp = await client.SendAsync(loginMsg);
        Assert.Equal(HttpStatusCode.Redirect, loginResp.StatusCode);

        // 3. Lấy authentication cookie
        var setCookies = loginResp.Headers.GetValues("Set-Cookie").ToList();
        var authCookie = setCookies.FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.NotNull(authCookie);
        var rawCookie = authCookie!.Split(';')[0];

        // 4. Gửi POST negotiate kèm cookie
        var negotiateMsg = new HttpRequestMessage(HttpMethod.Post, "/storageUnitHub/negotiate?negotiateVersion=1");
        negotiateMsg.Headers.Add("Cookie", rawCookie);

        var negotiateResp = await client.SendAsync(negotiateMsg);

        // 5. Phải nhận HTTP 200 và response có connectionToken
        Assert.Equal(HttpStatusCode.OK, negotiateResp.StatusCode);
        var json = await negotiateResp.Content.ReadAsStringAsync();
        Assert.Contains("connectionToken", json);
    }

    #endregion
}
