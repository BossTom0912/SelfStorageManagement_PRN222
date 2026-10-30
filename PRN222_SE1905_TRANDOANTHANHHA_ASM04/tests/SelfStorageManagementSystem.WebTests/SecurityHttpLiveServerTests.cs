using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.Web.Realtime;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Bộ kiểm thử an ninh, phân quyền và HTTP thực tế (Security, Authorization & Real HTTP/Kestrel) cho Task T16.
/// Tất cả các bài kiểm thử trong class này đều được thực thi trên máy chủ ASP.NET Core Kestrel thật
/// lắng nghe trên cổng TCP loopback (http://127.0.0.1:{port}), với phiên Cookie thật, tài khoản DB thật,
/// và không sử dụng bất kỳ Service Mocks hay TestAuthHandler nào làm bằng chứng duy nhất.
/// </summary>
public class SecurityHttpLiveServerTests : IClassFixture<SecurityHttpServerFixture>
{
    private readonly SecurityHttpServerFixture _fixture;

    public SecurityHttpLiveServerTests(SecurityHttpServerFixture fixture)
    {
        _fixture = fixture;
    }

    private static string? GetPath(Uri? uri)
    {
        if (uri == null) return null;
        var str = uri.OriginalString;
        if (Uri.TryCreate(str, UriKind.Absolute, out var absUri))
        {
            return absUri.AbsolutePath;
        }
        var queryIdx = str.IndexOf('?');
        return queryIdx >= 0 ? str.Substring(0, queryIdx) : str;
    }

    private static string? GetPathAndQuery(Uri? uri)
    {
        if (uri == null) return null;
        var str = uri.OriginalString;
        if (Uri.TryCreate(str, UriKind.Absolute, out var absUri))
        {
            return absUri.PathAndQuery;
        }
        return str;
    }

    #region 1. Anonymous Access Tests (HTTP & WebSocket)

    [Theory]
    [InlineData("/Account/Profile", "/Account/Login?ReturnUrl=%2FAccount%2FProfile")]
    [InlineData("/Account/SecuredData", "/Account/Login?ReturnUrl=%2FAccount%2FSecuredData")]
    public async Task Anonymous_Get_MvcProtectedPage_RedirectsToLogin_WithReturnUrl(string protectedPath, string expectedRedirect)
    {
        // Arrange
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false);

        // Act
        var response = await client.GetAsync(protectedPath);

        // Assert: Real HTTP 302 Redirect to Login with ReturnUrl
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(expectedRedirect, GetPathAndQuery(response.Headers.Location));
    }

    [Theory]
    [InlineData("/StorageUnits", "/Account/Login?ReturnUrl=%2FStorageUnits")]
    [InlineData("/Reservations", "/Account/Login?ReturnUrl=%2FReservations")]
    [InlineData("/Staff/CheckIn", "/Account/Login?ReturnUrl=%2FStaff%2FCheckIn")]
    [InlineData("/Admin/Accounts", "/Account/Login?ReturnUrl=%2FAdmin%2FAccounts")]
    public async Task Anonymous_Get_RazorProtectedPage_RedirectsToLogin_WithReturnUrl(string razorPagePath, string expectedRedirect)
    {
        // Arrange
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false);

        // Act
        var response = await client.GetAsync(razorPagePath);

        // Assert: Real HTTP 302 Redirect to Login with ReturnUrl
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(expectedRedirect, GetPathAndQuery(response.Headers.Location));
    }

    [Fact]
    public async Task Anonymous_AjaxRequest_Returns401Json_WithoutHtmlRedirect()
    {
        // Arrange
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false);
        var request = new HttpRequestMessage(HttpMethod.Get, "/Account/SecuredData");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        // Act
        var response = await client.SendAsync(request);

        // Assert: CookieAuthExtensions returns 401 JSON instead of 302 HTML redirect
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(401, doc.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Contains("Unauthorized", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Anonymous_SignalR_Negotiate_Returns401Json()
    {
        // Arrange
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false);
        var request = new HttpRequestMessage(HttpMethod.Post, "/storageUnitHub/negotiate?negotiateVersion=1");

        // Act
        var response = await client.SendAsync(request);

        // Assert: Unauthenticated SignalR negotiate request is rejected with 401
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_SignalR_DirectConnection_IsRejected()
    {
        // Arrange
        var hubConnection = new HubConnectionBuilder()
            .WithUrl($"{_fixture.ServerUrl}/storageUnitHub")
            .Build();

        // Act & Assert: WebSocket / direct connection without Cookie fails
        await Assert.ThrowsAnyAsync<Exception>(() => hubConnection.StartAsync());
    }

    #endregion

    #region 2. Role Authorization Tests (Sufficient vs Missing Roles)

    [Fact]
    public async Task CustomerRole_CanAccessCustomerPages_ButDeniedOnStaffAndAdminPages()
    {
        // Arrange: Seed real storage_customer in DB
        var email = $"cust_{Guid.NewGuid():N}@example.test";
        var password = "CustomerPassword123!";
        var user = await _fixture.SeedUserAsync(email, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, "Khách Hàng A");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);

        // Authenticate via real HTTP POST /Account/Login with CSRF token
        var (authCookie, loginResp) = await _fixture.LoginViaHttpAsync(client, email, password);
        Assert.Equal(HttpStatusCode.Redirect, loginResp.StatusCode);
        Assert.False(string.IsNullOrEmpty(authCookie));

        // 1. Authorized: /Account/Profile (MVC)
        var profileResp = await client.GetAsync("/Account/Profile");
        Assert.Equal(HttpStatusCode.OK, profileResp.StatusCode);

        // 2. Authorized: /Account/SecuredData (MVC AJAX endpoint)
        var securedResp = await client.GetAsync("/Account/SecuredData");
        Assert.Equal(HttpStatusCode.OK, securedResp.StatusCode);
        var securedJson = await securedResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(securedJson);
        Assert.Equal(user.id.ToString(), doc.RootElement.GetProperty("userId").GetString());

        // 3. Authorized: /Reservations (Customer Razor Page)
        var reservationsResp = await client.GetAsync("/Reservations");
        Assert.Equal(HttpStatusCode.OK, reservationsResp.StatusCode);

        // 4. Forbidden: /StorageUnits (Requires StaffOnly policy -> 302 to /Account/Forbidden)
        var storageUnitsResp = await client.GetAsync("/StorageUnits");
        Assert.Equal(HttpStatusCode.Redirect, storageUnitsResp.StatusCode);
        Assert.Equal("/Account/Forbidden", GetPath(storageUnitsResp.Headers.Location));

        // 5. Forbidden: /Staff/CheckIn (Requires StaffOnly policy -> 302 to /Account/Forbidden)
        var checkInResp = await client.GetAsync("/Staff/CheckIn");
        Assert.Equal(HttpStatusCode.Redirect, checkInResp.StatusCode);
        Assert.Equal("/Account/Forbidden", GetPath(checkInResp.Headers.Location));

        // 6. Forbidden: /Admin/Accounts (Requires AdminOnly policy -> 302 to /Account/Forbidden)
        var adminAccountsResp = await client.GetAsync("/Admin/Accounts");
        Assert.Equal(HttpStatusCode.Redirect, adminAccountsResp.StatusCode);
        Assert.Equal("/Account/Forbidden", GetPath(adminAccountsResp.Headers.Location));

        // 7. Forbidden: /Account/AdminCheck (Requires RoleConstants.SystemAdministrator)
        var adminCheckResp = await client.GetAsync("/Account/AdminCheck");
        Assert.Equal(HttpStatusCode.Redirect, adminCheckResp.StatusCode);
        Assert.Equal("/Account/Forbidden", GetPath(adminCheckResp.Headers.Location));
    }

    [Fact]
    public async Task CustomerRole_AjaxRequestToForbiddenPage_Returns403Json()
    {
        // Arrange
        var email = $"cust_ajax_{Guid.NewGuid():N}@example.test";
        var password = "CustomerPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, "Khách Hàng AJAX");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        await _fixture.LoginViaHttpAsync(client, email, password);

        // Act: AJAX request to AdminCheck
        var request = new HttpRequestMessage(HttpMethod.Get, "/Account/AdminCheck");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var response = await client.SendAsync(request);

        // Assert: 403 Forbidden JSON without HTML redirect
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(403, doc.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Contains("Forbidden", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task StaffRole_CanAccessStaffPages_ButDeniedOnAdminPages()
    {
        // Arrange
        var hierarchy = await _fixture.SeedFacilityHierarchyAsync("staff_perm");
        var email = $"staff_{Guid.NewGuid():N}@example.test";
        var password = "StaffPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Nhân Viên Quầy",
            assignedFacilityId: hierarchy.Facility.id, assignedRole: RoleConstants.FacilityStaff);

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        await _fixture.LoginViaHttpAsync(client, email, password);

        // 1. Authorized on /StorageUnits
        var storageUnitsResp = await client.GetAsync("/StorageUnits");
        Assert.Equal(HttpStatusCode.OK, storageUnitsResp.StatusCode);

        // 2. Authorized on /Staff/CheckIn
        var checkInResp = await client.GetAsync("/Staff/CheckIn");
        Assert.Equal(HttpStatusCode.OK, checkInResp.StatusCode);

        // 3. Denied on /Admin/Accounts
        var adminAccountsResp = await client.GetAsync("/Admin/Accounts");
        Assert.Equal(HttpStatusCode.Redirect, adminAccountsResp.StatusCode);
        Assert.Equal("/Account/Forbidden", GetPath(adminAccountsResp.Headers.Location));

        // 4. Denied on /Account/AdminCheck
        var adminCheckResp = await client.GetAsync("/Account/AdminCheck");
        Assert.Equal(HttpStatusCode.Redirect, adminCheckResp.StatusCode);
        Assert.Equal("/Account/Forbidden", GetPath(adminCheckResp.Headers.Location));
    }

    [Fact]
    public async Task AdminRole_CanAccessAllPages_IncludingAdminCheckAndAccounts()
    {
        // Arrange
        var hierarchy = await _fixture.SeedFacilityHierarchyAsync("admin_perm");
        var email = $"admin_{Guid.NewGuid():N}@example.test";
        var password = "AdminPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Quản Trị Viên",
            assignedFacilityId: hierarchy.Facility.id, assignedRole: RoleConstants.SystemAdministrator);

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        await _fixture.LoginViaHttpAsync(client, email, password);

        // 1. StorageUnits -> OK
        var storageUnitsResp = await client.GetAsync("/StorageUnits");
        Assert.Equal(HttpStatusCode.OK, storageUnitsResp.StatusCode);

        // 2. Staff CheckIn -> OK
        var checkInResp = await client.GetAsync("/Staff/CheckIn");
        Assert.Equal(HttpStatusCode.OK, checkInResp.StatusCode);

        // 3. Admin Accounts -> OK
        var adminAccountsResp = await client.GetAsync("/Admin/Accounts");
        Assert.Equal(HttpStatusCode.OK, adminAccountsResp.StatusCode);

        // 4. Admin Check MVC API -> 200 OK with authorized payload
        var adminCheckResp = await client.GetAsync("/Account/AdminCheck");
        Assert.Equal(HttpStatusCode.OK, adminCheckResp.StatusCode);
        var json = await adminCheckResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("authorized", doc.RootElement.GetProperty("status").GetString());
    }

    #endregion

    #region 3. Facility Scope Enforcement Tests

    [Fact]
    public async Task FacilityScope_ManagerAssignedToFacilityA_CanDeleteAtA_DeniedAtB_AndDeniedAtAAfterAssignmentRevocation()
    {
        // Arrange: Create Facility A and Facility B, and an extra unit A2 at Facility A
        var hierarchyA = await _fixture.SeedFacilityHierarchyAsync("fac_a");
        var hierarchyB = await _fixture.SeedFacilityHierarchyAsync("fac_b");
        var unitA2 = await _fixture.SeedAdditionalStorageUnitAsync(
            hierarchyA.Facility.id, hierarchyA.Area.id, hierarchyA.UnitType.id, $"UNIT-A2-{Guid.NewGuid():N}".Substring(0, 16));

        // Seed Facility Manager assigned exclusively to Facility A
        var managerEmail = $"manager_faca_{Guid.NewGuid():N}@example.test";
        var managerPassword = "ManagerPassword123!";
        var managerUser = await _fixture.SeedUserAsync(
            managerEmail, managerPassword, RoleConstants.FacilityManager, UserStatusConstants.Active, "Manager Fac A",
            assignedFacilityId: hierarchyA.Facility.id, assignedRole: RoleConstants.FacilityManager);

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, managerEmail, managerPassword);

        // Connect via SignalR Hub over loopback TCP with Manager Cookie
        var receivedDeletedUnitIds = new List<string>();
        var hubConnection = new HubConnectionBuilder()
            .WithUrl($"{_fixture.ServerUrl}/storageUnitHub", options =>
            {
                options.Headers.Add("Cookie", authCookie);
            })
            .Build();

        hubConnection.On<string>("Receiver_DeleteStorageUnit", id =>
        {
            lock (receivedDeletedUnitIds)
            {
                receivedDeletedUnitIds.Add(id);
            }
        });

        await hubConnection.StartAsync();

        // 1. Same-session action at Facility A SUCCEEDS (Authorized Role & In-Scope Facility)
        var deletedId = await hubConnection.InvokeAsync<string>("HubDelete_StorageUnit", hierarchyA.Unit.id.ToString(), (string?)null);
        Assert.Equal(hierarchyA.Unit.id.ToString(), deletedId);

        // Wait briefly for broadcast event
        var eventReceived = SpinWait.SpinUntil(() =>
        {
            lock (receivedDeletedUnitIds) { return receivedDeletedUnitIds.Contains(hierarchyA.Unit.id.ToString()); }
        }, TimeSpan.FromSeconds(3));
        Assert.True(eventReceived, "Expected Receiver_DeleteStorageUnit broadcast was not received for Unit A.");

        // Assert durable DB state: Unit A is permanently deleted from database
        using (var db = _fixture.CreateDbContext())
        {
            var unitAInDb = await db.storage_units.FindAsync(hierarchyA.Unit.id);
            Assert.Null(unitAInDb);
        }

        // 2. Action at Facility B (cross-facility) is DENIED by facility scope check in BLL
        var exB = await Assert.ThrowsAsync<HubException>(() =>
            hubConnection.InvokeAsync<string>("HubDelete_StorageUnit", hierarchyB.Unit.id.ToString(), (string?)null));
        Assert.True(
            exB.Message.Contains($"User does not have access to facility ID {hierarchyB.Facility.id}", StringComparison.OrdinalIgnoreCase) ||
            exB.Message.Contains($"Manager is not assigned as manager at facility ID {hierarchyB.Facility.id}", StringComparison.OrdinalIgnoreCase),
            $"Unexpected exception message: {exB.Message}");

        // Assert durable DB state: Unit B remains untouched
        using (var db = _fixture.CreateDbContext())
        {
            var unitBInDb = await db.storage_units.FindAsync(hierarchyB.Unit.id);
            Assert.NotNull(unitBInDb);
            Assert.Equal("available", unitBInDb.physical_status);
        }

        // Verify broadcast event was NEVER emitted for Unit B
        lock (receivedDeletedUnitIds)
        {
            Assert.DoesNotContain(hierarchyB.Unit.id.ToString(), receivedDeletedUnitIds);
        }

        // 3. Remove Manager's assignment to Facility A in database (scope revocation)
        using (var db = _fixture.CreateDbContext())
        {
            var assignment = await db.staff_facility_assignments
                .FirstOrDefaultAsync(s => s.employee_id == managerUser.id && s.facility_id == hierarchyA.Facility.id);
            if (assignment != null)
            {
                db.staff_facility_assignments.Remove(assignment);
                await db.SaveChangesAsync();
            }
        }

        // 4. Same-session action at Facility A on Unit A2 is now DENIED due to revoked assignment in DB
        var exA2 = await Assert.ThrowsAsync<HubException>(() =>
            hubConnection.InvokeAsync<string>("HubDelete_StorageUnit", unitA2.id.ToString(), (string?)null));
        Assert.True(
            exA2.Message.Contains($"User does not have access to facility ID {hierarchyA.Facility.id}", StringComparison.OrdinalIgnoreCase) ||
            exA2.Message.Contains($"Manager is not assigned as manager at facility ID {hierarchyA.Facility.id}", StringComparison.OrdinalIgnoreCase),
            $"Unexpected exception message: {exA2.Message}");

        // Assert durable DB state: Unit A2 remains untouched
        using (var db = _fixture.CreateDbContext())
        {
            var unitA2InDb = await db.storage_units.FindAsync(unitA2.id);
            Assert.NotNull(unitA2InDb);
            Assert.Equal("available", unitA2InDb.physical_status);
        }

        // Verify broadcast event was NEVER emitted for Unit A2
        lock (receivedDeletedUnitIds)
        {
            Assert.DoesNotContain(unitA2.id.ToString(), receivedDeletedUnitIds);
        }

        await hubConnection.StopAsync();
    }

    [Fact]
    public async Task FacilityScope_StaffAssignedToFacilityA_CannotDeleteUnitInFacilityB_RejectedByRole()
    {
        // Arrange: Create two facilities A and B
        var hierarchyA = await _fixture.SeedFacilityHierarchyAsync("fac_staff_a");
        var hierarchyB = await _fixture.SeedFacilityHierarchyAsync("fac_staff_b");

        // Staff is assigned exclusively to Facility A
        var staffEmail = $"staff_faca_{Guid.NewGuid():N}@example.test";
        var staffPassword = "StaffPassword123!";
        await _fixture.SeedUserAsync(staffEmail, staffPassword, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Staff Fac A",
            assignedFacilityId: hierarchyA.Facility.id, assignedRole: RoleConstants.FacilityStaff);

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, staffEmail, staffPassword);

        // Connect via SignalR Hub to attempt delete on unit in Facility B
        var hubConnection = new HubConnectionBuilder()
            .WithUrl($"{_fixture.ServerUrl}/storageUnitHub", options =>
            {
                options.Headers.Add("Cookie", authCookie);
            })
            .Build();

        await hubConnection.StartAsync();

        // Staff role has canManage == false in StorageUnitHub, rejected immediately with HubException
        var ex = await Assert.ThrowsAsync<HubException>(() =>
            hubConnection.InvokeAsync<string>("HubDelete_StorageUnit", hierarchyB.Unit.id.ToString(), (string?)null));

        Assert.Contains("Nhân viên thường không có quyền", ex.Message, StringComparison.OrdinalIgnoreCase);

        await hubConnection.StopAsync();
    }

    #endregion

    #region 4. IDOR (Insecure Direct Object Reference) Protection Tests

    [Fact]
    public async Task IDOR_CustomerCannotAccessOrCancelAnotherCustomersReservation()
    {
        // Arrange: Seed two distinct customers A and B
        var hierarchy = await _fixture.SeedFacilityHierarchyAsync("idor");

        var customerAEmail = $"cust_a_{Guid.NewGuid():N}@example.test";
        var customerBEmail = $"cust_b_{Guid.NewGuid():N}@example.test";
        var password = "SecurePassword123!";

        var userA = await _fixture.SeedUserAsync(customerAEmail, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, "Khách Hàng A");
        var userB = await _fixture.SeedUserAsync(customerBEmail, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, "Khách Hàng B");

        // Reservation belongs to Customer A
        var resA = await _fixture.SeedReservationAsync(userA.id, hierarchy.Facility.id, hierarchy.UnitType.id, hierarchy.Rate.id);

        // Customer B logs in via real HTTP
        var cookieContainerB = new CookieContainer();
        using var clientB = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainerB);
        await _fixture.LoginViaHttpAsync(clientB, customerBEmail, password);

        // 1. Customer B attempts GET /Reservations/Details?id={resA.id}
        // Expected: ForbiddenException is caught by PageModel -> Forbid() -> 302 Redirect to /Account/Forbidden
        var detailsResp = await clientB.GetAsync($"/Reservations/Details?id={resA.id}");
        Assert.Equal(HttpStatusCode.Redirect, detailsResp.StatusCode);
        Assert.Equal("/Account/Forbidden", GetPath(detailsResp.Headers.Location));

        // 2. Customer B attempts AJAX GET /Reservations/Details?id={resA.id}&handler=Status
        var ajaxStatusReq = new HttpRequestMessage(HttpMethod.Get, $"/Reservations/Details?id={resA.id}&handler=Status");
        ajaxStatusReq.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var ajaxStatusResp = await clientB.SendAsync(ajaxStatusReq);

        // Must be rejected with 403 Forbidden or redirect to Forbidden, never 200 with Customer A's data
        Assert.True(
            ajaxStatusResp.StatusCode == HttpStatusCode.Forbidden ||
            (ajaxStatusResp.StatusCode == HttpStatusCode.Redirect && GetPath(ajaxStatusResp.Headers.Location)?.Contains("Forbidden") == true),
            $"Customer B received unexpected status {ajaxStatusResp.StatusCode} accessing Customer A's reservation.");
    }

    #endregion

    #region 5. Dynamic Claim Revalidation & Account Locking (OnValidatePrincipal)

    [Fact]
    public async Task OnValidatePrincipal_UserLockedInDatabase_ImmediatelyRevokesSessionOnNextRequest()
    {
        // Arrange: User logs in and acquires valid auth cookie
        var email = $"lock_test_{Guid.NewGuid():N}@example.test";
        var password = "UserPassword123!";
        var user = await _fixture.SeedUserAsync(email, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, "Khách Khóa");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, email, password);

        // 1. Initial request with valid active status succeeds
        var initialResp = await client.GetAsync("/Account/SecuredData");
        Assert.Equal(HttpStatusCode.OK, initialResp.StatusCode);

        // 2. Mutate user status to "disabled" directly in database
        using (var db = _fixture.CreateDbContext())
        {
            var dbUser = await db.users.FirstAsync(u => u.id == user.id);
            dbUser.status = "disabled";
            dbUser.updated_at = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // 3. Next HTTP request with the exact same cookie: OnValidatePrincipal immediately rejects
        var nextResp = await client.GetAsync("/Account/SecuredData");

        // Assert: 302 Redirect to /Account/Login (session terminated immediately, not waiting for 5-min cookie expiry)
        Assert.Equal(HttpStatusCode.Redirect, nextResp.StatusCode);
        Assert.Equal("/Account/Login", GetPath(nextResp.Headers.Location));
    }

    [Fact]
    public async Task OnValidatePrincipal_EmployeeTerminated_ImmediatelyRevokesSessionOnNextRequest()
    {
        // Arrange
        var hierarchy = await _fixture.SeedFacilityHierarchyAsync("emp_term");
        var email = $"term_staff_{Guid.NewGuid():N}@example.test";
        var password = "StaffPassword123!";
        var user = await _fixture.SeedUserAsync(email, password, RoleConstants.FacilityStaff, UserStatusConstants.Active, "Nhân Viên Nghỉ Việc",
            assignedFacilityId: hierarchy.Facility.id, assignedRole: RoleConstants.FacilityStaff);

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        await _fixture.LoginViaHttpAsync(client, email, password);

        // 1. Initial check succeeds
        var initialResp = await client.GetAsync("/StorageUnits");
        Assert.Equal(HttpStatusCode.OK, initialResp.StatusCode);

        // 2. Mutate employee employment_status to "terminated"
        using (var db = _fixture.CreateDbContext())
        {
            var empProfile = await db.employee_profiles.FirstAsync(ep => ep.user_id == user.id);
            empProfile.employment_status = "terminated";
            empProfile.updated_at = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // 3. Next request: OnValidatePrincipal immediately revokes session
        var nextResp = await client.GetAsync("/StorageUnits");
        Assert.Equal(HttpStatusCode.Redirect, nextResp.StatusCode);
        Assert.Equal("/Account/Login", GetPath(nextResp.Headers.Location));
    }

    [Fact]
    public async Task OnValidatePrincipal_RoleChanged_SilentlyRefreshesClaimsAndIssuesNewCookie()
    {
        // Arrange: User begins with storage_customer role
        var email = $"role_promote_{Guid.NewGuid():N}@example.test";
        var password = "PromotePassword123!";
        var user = await _fixture.SeedUserAsync(email, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, "Nâng Quyền");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        await _fixture.LoginViaHttpAsync(client, email, password);

        // 1. Initially denied on /Account/AdminCheck
        var checkResp1 = await client.GetAsync("/Account/AdminCheck");
        Assert.Equal(HttpStatusCode.Redirect, checkResp1.StatusCode);
        Assert.Equal("/Account/Forbidden", GetPath(checkResp1.Headers.Location));

        // 2. Grant system_administrator role in database
        using (var db = _fixture.CreateDbContext())
        {
            var adminRole = await db.roles.FirstAsync(r => r.code == RoleConstants.SystemAdministrator);
            db.user_roles.Add(new user_role
            {
                user_id = user.id,
                role_id = adminRole.id,
                granted_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // 3. Next request to /Account/SecuredData: OnValidatePrincipal refreshes claims and sets ShouldRenew = true
        var refreshResp = await client.GetAsync("/Account/SecuredData");
        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);

        // Verify Set-Cookie was issued to client with updated claims ticket
        var hasNewCookie = refreshResp.Headers.TryGetValues("Set-Cookie", out var setCookies) &&
                           setCookies.Any(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.True(hasNewCookie, "Server did not issue renewed Set-Cookie header upon role change.");

        // 4. Client can now successfully access /Account/AdminCheck
        var checkResp2 = await client.GetAsync("/Account/AdminCheck");
        Assert.Equal(HttpStatusCode.OK, checkResp2.StatusCode);
    }

    [Fact]
    public async Task OnValidatePrincipal_RoleRemoved_ImmediatelyDeniesAccessOnExistingCookieAndHub()
    {
        // Arrange: User begins with system_administrator role
        var email = $"role_demote_{Guid.NewGuid():N}@example.test";
        var password = "DemotePassword123!";
        var user = await _fixture.SeedUserAsync(email, password, RoleConstants.SystemAdministrator, UserStatusConstants.Active, "Admin Giáng Quyền");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, email, password, "/", cookieContainer);

        // 1. Initial access: Admin can access /Account/AdminCheck (HTTP 200)
        var checkResp1 = await client.GetAsync("/Account/AdminCheck");
        Assert.Equal(HttpStatusCode.OK, checkResp1.StatusCode);

        // 2. Remove system_administrator role in database and replace with storage_customer
        using (var db = _fixture.CreateDbContext())
        {
            var userRoles = await db.user_roles.Where(ur => ur.user_id == user.id).ToListAsync();
            db.user_roles.RemoveRange(userRoles);

            var custRole = await db.roles.FirstAsync(r => r.code == RoleConstants.StorageCustomer);
            db.user_roles.Add(new user_role
            {
                user_id = user.id,
                role_id = custRole.id,
                granted_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // 3. Next HTTP request with ORIGINAL cookie: OnValidatePrincipal immediately revokes admin claim and sets ShouldRenew = true
        var checkResp2 = await client.GetAsync("/Account/AdminCheck");
        // Must be rejected by AdminOnly policy -> 302 Redirect to /Account/Forbidden
        Assert.Equal(HttpStatusCode.Redirect, checkResp2.StatusCode);
        Assert.Equal("/Account/Forbidden", GetPath(checkResp2.Headers.Location));

        // Verify server issued a renewed Set-Cookie containing the demoted principal
        var hasNewCookie = checkResp2.Headers.TryGetValues("Set-Cookie", out var setCookies) &&
                           setCookies.Any(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.True(hasNewCookie, "Server did not issue renewed Set-Cookie header upon role removal.");

        // 4. AJAX request with cookie receives 403 Forbidden JSON
        var ajaxReq = new HttpRequestMessage(HttpMethod.Get, "/Account/AdminCheck");
        ajaxReq.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var ajaxResp = await client.SendAsync(ajaxReq);
        Assert.Equal(HttpStatusCode.Forbidden, ajaxResp.StatusCode);

        // 5. SignalR Hub operation with existing cookie is rejected
        var hierarchy = await _fixture.SeedFacilityHierarchyAsync("role_demote_fac");
        var hubConnection = new HubConnectionBuilder()
            .WithUrl($"{_fixture.ServerUrl}/storageUnitHub", options =>
            {
                options.Headers.Add("Cookie", authCookie);
            })
            .Build();

        await hubConnection.StartAsync();

        var hubEx = await Assert.ThrowsAsync<HubException>(() =>
            hubConnection.InvokeAsync<string>("HubDelete_StorageUnit", hierarchy.Unit.id.ToString(), (string?)null));
        Assert.Contains("Nhân viên thường không có quyền xóa ô kho", hubEx.Message);

        await hubConnection.StopAsync();
    }

    #endregion

    #region 6. Expiration & Sliding Expiration Tests

    [Fact]
    public void Cookie_Configuration_Has5MinLifetime_AndSlidingExpirationTrue()
    {
        // Assert DI options directly against production configuration
        var optionsSnapshot = _fixture.Services.GetRequiredService<IOptionsSnapshot<CookieAuthenticationOptions>>();
        var options = optionsSnapshot.Get(CookieAuthExtensions.CookieScheme);

        Assert.Equal(CookieAuthExtensions.CookieName, options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal(TimeSpan.FromMinutes(5), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
        Assert.Equal(CookieAuthExtensions.LoginPath, options.LoginPath);
        Assert.Equal(CookieAuthExtensions.AccessDeniedPath, options.AccessDeniedPath);
    }

    [Fact]
    public async Task ExpiredCookie_IsRejected_AndRedirectsToLogin()
    {
        // Arrange: Craft an expired ticket using the application's real DataProtector
        var email = $"expire_{Guid.NewGuid():N}@example.test";
        var password = "ExpirePassword123!";
        var user = await _fixture.SeedUserAsync(email, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, "Khách Hết Hạn");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.id.ToString()),
            new Claim(ClaimTypes.Name, "Khách Hết Hạn"),
            new Claim(ClaimTypes.Role, RoleConstants.StorageCustomer)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthExtensions.CookieScheme);
        var principal = new ClaimsPrincipal(identity);

        // Issued in the past and expired 10 minutes ago
        var properties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddMinutes(-15),
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        var ticket = new AuthenticationTicket(principal, properties, CookieAuthExtensions.CookieScheme);

        var dataProtectionProvider = _fixture.Services.GetRequiredService<IDataProtectionProvider>();
        var protector = dataProtectionProvider.CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", CookieAuthExtensions.CookieScheme, "v2");
        var ticketDataFormat = new TicketDataFormat(protector);
        var encryptedTicket = ticketDataFormat.Protect(ticket);

        // Act: Send request with expired cookie
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false);
        var request = new HttpRequestMessage(HttpMethod.Get, "/Account/SecuredData");
        request.Headers.Add("Cookie", $"{CookieAuthExtensions.CookieName}={encryptedTicket}");
        var response = await client.SendAsync(request);

        // Assert: ASP.NET Core cookie authentication handler treats ticket as expired and redirects to /Account/Login
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", GetPath(response.Headers.Location));
    }

    [Fact]
    public async Task Cookie_NearExpiryTicket_TriggersSlidingExpirationRenewal_OverRealTransport()
    {
        // Arrange: User seeded in DB
        var email = $"sliding_renew_{Guid.NewGuid():N}@example.test";
        var password = "SlidingPassword123!";
        var user = await _fixture.SeedUserAsync(email, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, "Khách Sliding");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.id.ToString()),
            new Claim(ClaimTypes.Name, "Khách Sliding"),
            new Claim(ClaimTypes.Role, RoleConstants.StorageCustomer)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthExtensions.CookieScheme);
        var principal = new ClaimsPrincipal(identity);

        // Total lifetime is 5 minutes.
        // Set IssuedUtc to 3 minutes ago, and ExpiresUtc to 2 minutes in the future.
        // Since elapsed time (3 mins) > 50% of the 5-minute ExpireTimeSpan (2.5 mins),
        // ASP.NET Core CookieAuthenticationHandler triggers sliding expiration renewal (ShouldRenew = true).
        var properties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddMinutes(-3),
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(2)
        };
        var ticket = new AuthenticationTicket(principal, properties, CookieAuthExtensions.CookieScheme);

        var dataProtectionProvider = _fixture.Services.GetRequiredService<IDataProtectionProvider>();
        var protector = dataProtectionProvider.CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", CookieAuthExtensions.CookieScheme, "v2");
        var ticketDataFormat = new TicketDataFormat(protector);
        var encryptedTicket = ticketDataFormat.Protect(ticket);

        // Act 1: Send request to /Account/SecuredData carrying near-expiry cookie
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false);
        var request = new HttpRequestMessage(HttpMethod.Get, "/Account/SecuredData");
        request.Headers.Add("Cookie", $"{CookieAuthExtensions.CookieName}={encryptedTicket}");
        var response = await client.SendAsync(request);

        // Assert 1: Request is authenticated (200 OK)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert 2: Kestrel issued a renewed Set-Cookie header
        var hasRenewedCookie = response.Headers.TryGetValues("Set-Cookie", out var setCookies) &&
                               setCookies.Any(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.True(hasRenewedCookie, "Kestrel did not issue a renewed Set-Cookie header when ticket elapsed > 50% of ExpireTimeSpan.");

        var renewedCookieHeader = setCookies.First(c => c.Contains(CookieAuthExtensions.CookieName)).Split(';')[0];

        // Act 2: Send subsequent request using the newly renewed cookie
        var subsequentReq = new HttpRequestMessage(HttpMethod.Get, "/Account/SecuredData");
        subsequentReq.Headers.Add("Cookie", renewedCookieHeader);
        var subsequentResp = await client.SendAsync(subsequentReq);

        // Assert 3: Subsequent request with renewed cookie is fully authenticated
        Assert.Equal(HttpStatusCode.OK, subsequentResp.StatusCode);
    }

    #endregion

    #region 7. Logout & Replay Resistance Tests

    [Fact]
    public async Task Logout_GetDoesNotSignOut_PostWithAntiforgeryClearsAuthCookie()
    {
        // Arrange
        var email = $"logout_{Guid.NewGuid():N}@example.test";
        var password = "LogoutPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, "Khách Đăng Xuất");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        await _fixture.LoginViaHttpAsync(client, email, password, "/", cookieContainer);

        // 1. GET /Account/Logout does NOT log out (returns 200 View)
        var getLogoutResp = await client.GetAsync("/Account/Logout");
        Assert.Equal(HttpStatusCode.OK, getLogoutResp.StatusCode);

        // Verify still authenticated
        var checkResp = await client.GetAsync("/Account/SecuredData");
        Assert.Equal(HttpStatusCode.OK, checkResp.StatusCode);

        // 2. Extract CSRF token from /Account/Logout
        var (csrfToken, csrfCookie) = await _fixture.GetAntiforgeryTokensAsync(client, "/Account/Logout", cookieContainer);

        // 3. POST /Account/Logout with CSRF token
        var postLogoutReq = new HttpRequestMessage(HttpMethod.Post, "/Account/Logout");
        postLogoutReq.Headers.Add("Cookie", csrfCookie);
        postLogoutReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = csrfToken
        });

        var postLogoutResp = await client.SendAsync(postLogoutReq);

        // Assert: 302 Redirect to Login and Set-Cookie clearing the auth ticket
        Assert.Equal(HttpStatusCode.Redirect, postLogoutResp.StatusCode);
        Assert.Equal("/Account/Login", GetPath(postLogoutResp.Headers.Location));

        var setCookies = postLogoutResp.Headers.GetValues("Set-Cookie").ToList();
        var clearedAuthCookie = setCookies.FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.NotNull(clearedAuthCookie);
        Assert.True(
            clearedAuthCookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase) ||
            clearedAuthCookie.Contains($"{CookieAuthExtensions.CookieName}=;", StringComparison.OrdinalIgnoreCase),
            "Logout did not issue cookie expiration header.");
    }

    #endregion

    #region 8. Antiforgery / CSRF, Origin & Form Binding Tests

    [Fact]
    public async Task Antiforgery_PostWithoutToken_Returns400BadRequest()
    {
        // Arrange
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = "someuser@example.test",
            ["Password"] = "SomePass123!"
            // Missing __RequestVerificationToken
        });

        // Act
        var response = await client.SendAsync(request);

        // Assert: 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Antiforgery_PostWithForgedToken_Returns400BadRequest()
    {
        // Arrange
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false);
        var (_, cookieHeader) = await _fixture.GetAntiforgeryTokensAsync(client, "/Account/Login");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        request.Headers.Add("Cookie", cookieHeader);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = "someuser@example.test",
            ["Password"] = "SomePass123!",
            ["__RequestVerificationToken"] = "FORGED_ATTACKER_TOKEN_9999"
        });

        // Act
        var response = await client.SendAsync(request);

        // Assert: 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ExternalReturnUrl_IsRejected_AndRedirectsToHome()
    {
        // Arrange: Attempt Open Redirect attack
        var email = $"open_redir_{Guid.NewGuid():N}@example.test";
        var password = "RedirPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, "Khách Redirect");

        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false);
        var (token, cookieHeader) = await _fixture.GetAntiforgeryTokensAsync(client, "/Account/Login");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        request.Headers.Add("Cookie", cookieHeader);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "https://attacker.evil.com/phishing"
        });

        // Act
        var response = await client.SendAsync(request);

        // Assert: 302 Redirect to / (Url.IsLocalUrl rejected the external attacker URL)
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", GetPath(response.Headers.Location));
    }

    [Fact]
    public async Task Origin_UntrustedOrigin_SignalR_Negotiate_Returns403Forbidden()
    {
        // Arrange: Authenticated user sends negotiate request with untrusted cross-origin
        var email = $"origin_neg_{Guid.NewGuid():N}@example.test";
        var password = "OriginPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.FacilityManager, UserStatusConstants.Active, "Origin Neg Tester");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, email, password, "/", cookieContainer);

        const string maliciousOrigin = "https://malicious-attacker.evil.com";

        // Act: POST /storageUnitHub/negotiate with untrusted Origin
        var authReq = new HttpRequestMessage(HttpMethod.Post, "/storageUnitHub/negotiate?negotiateVersion=1");
        authReq.Headers.Add("Cookie", authCookie);
        authReq.Headers.Add("Origin", maliciousOrigin);
        var response = await client.SendAsync(authReq);

        // Assert: 403 Forbidden is returned by UseStorageUnitHubOriginRestriction
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Cross-origin access to /storageUnitHub is forbidden", body);
    }

    [Fact]
    public async Task Origin_UntrustedOrigin_WebSocketHandshake_IsRejected_NoMutationOrBroadcast()
    {
        // Arrange: Seed Facility A with storage unit
        var hierarchy = await _fixture.SeedFacilityHierarchyAsync("origin_ws_fac");

        var email = $"origin_ws_{Guid.NewGuid():N}@example.test";
        var password = "OriginPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.FacilityManager, UserStatusConstants.Active, "Origin WS Tester",
            assignedFacilityId: hierarchy.Facility.id, assignedRole: RoleConstants.FacilityManager);

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, email, password, "/", cookieContainer);

        const string maliciousOrigin = "https://malicious-attacker.evil.com";

        // Act: Direct WebSocket handshake carrying valid auth cookie but untrusted malicious Origin
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Cookie", authCookie);
        ws.Options.SetRequestHeader("Origin", maliciousOrigin);
        var wsUri = new Uri(_fixture.ServerUrl.Replace("http://", "ws://") + "/storageUnitHub");

        var connectEx = await Assert.ThrowsAsync<WebSocketException>(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await ws.ConnectAsync(wsUri, cts.Token);
        });

        // Assert: Handshake rejected with HTTP 403 Forbidden
        Assert.Contains("403", connectEx.Message);
        Assert.NotEqual(WebSocketState.Open, ws.State);

        // Assert durable DB state: Storage unit remains untouched in database
        using (var db = _fixture.CreateDbContext())
        {
            var unitInDb = await db.storage_units.FindAsync(hierarchy.Unit.id);
            Assert.NotNull(unitInDb);
            Assert.Equal("available", unitInDb.physical_status);
        }
    }

    [Fact]
    public async Task Origin_LegitimateSameOrigin_WebSocketHandshake_IsAccepted()
    {
        // Arrange: Authenticated user with legitimate same-origin
        var email = $"origin_ok_{Guid.NewGuid():N}@example.test";
        var password = "OriginPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.FacilityManager, UserStatusConstants.Active, "Origin OK Tester");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, email, password, "/", cookieContainer);

        // Act: Direct WebSocket handshake carrying same-origin matching server URL
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Cookie", authCookie);
        ws.Options.SetRequestHeader("Origin", _fixture.ServerUrl);
        var wsUri = new Uri(_fixture.ServerUrl.Replace("http://", "ws://") + "/storageUnitHub");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await ws.ConnectAsync(wsUri, cts.Token);

        // Assert: Legitimate same-origin connection is accepted and reaches Open state
        Assert.Equal(WebSocketState.Open, ws.State);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Legitimate test finish", CancellationToken.None);
    }

    [Fact]
    public async Task Origin_NoOriginHeader_DirectClient_IsAccepted()
    {
        // Arrange: Authenticated user connecting from non-browser / direct client without Origin header
        var email = $"origin_none_{Guid.NewGuid():N}@example.test";
        var password = "OriginPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.FacilityManager, UserStatusConstants.Active, "Origin None Tester");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, email, password, "/", cookieContainer);

        // Act: Direct WebSocket handshake WITHOUT Origin header
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Cookie", authCookie);
        var wsUri = new Uri(_fixture.ServerUrl.Replace("http://", "ws://") + "/storageUnitHub");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await ws.ConnectAsync(wsUri, cts.Token);

        // Assert: Non-browser client without Origin header is allowed to connect
        Assert.Equal(WebSocketState.Open, ws.State);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Direct client finish", CancellationToken.None);
    }

    [Fact]
    public async Task Origin_WrongScheme_SignalR_NegotiateAndWebSocket_RejectedWith403()
    {
        var email = $"origin_scheme_{Guid.NewGuid():N}@example.test";
        var password = "OriginPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.FacilityManager, UserStatusConstants.Active, "Origin Scheme Tester");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, email, password, "/", cookieContainer);

        // Server is http://127.0.0.1:{port}; wrong scheme is https://127.0.0.1:{port}
        var wrongSchemeOrigin = _fixture.ServerUrl.Replace("http://", "https://");

        // 1. Negotiate with wrong scheme
        var authReq = new HttpRequestMessage(HttpMethod.Post, "/storageUnitHub/negotiate?negotiateVersion=1");
        authReq.Headers.Add("Cookie", authCookie);
        authReq.Headers.Add("Origin", wrongSchemeOrigin);
        var response = await client.SendAsync(authReq);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // 2. Direct WebSocket handshake with wrong scheme
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Cookie", authCookie);
        ws.Options.SetRequestHeader("Origin", wrongSchemeOrigin);
        var wsUri = new Uri(_fixture.ServerUrl.Replace("http://", "ws://") + "/storageUnitHub");

        var connectEx = await Assert.ThrowsAsync<WebSocketException>(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await ws.ConnectAsync(wsUri, cts.Token);
        });
        Assert.Contains("403", connectEx.Message);
        Assert.NotEqual(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task Origin_LoopbackAlias_SignalR_NegotiateAndWebSocket_RejectedWith403()
    {
        var email = $"origin_alias_{Guid.NewGuid():N}@example.test";
        var password = "OriginPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.FacilityManager, UserStatusConstants.Active, "Origin Alias Tester");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, email, password, "/", cookieContainer);

        // Server is http://127.0.0.1:{port}; loopback alias is http://localhost:{port} (different origin under RFC 6454)
        var loopbackAliasOrigin = _fixture.ServerUrl.Replace("127.0.0.1", "localhost");

        // 1. Negotiate with loopback alias
        var authReq = new HttpRequestMessage(HttpMethod.Post, "/storageUnitHub/negotiate?negotiateVersion=1");
        authReq.Headers.Add("Cookie", authCookie);
        authReq.Headers.Add("Origin", loopbackAliasOrigin);
        var response = await client.SendAsync(authReq);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // 2. Direct WebSocket handshake with loopback alias
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Cookie", authCookie);
        ws.Options.SetRequestHeader("Origin", loopbackAliasOrigin);
        var wsUri = new Uri(_fixture.ServerUrl.Replace("http://", "ws://") + "/storageUnitHub");

        var connectEx = await Assert.ThrowsAsync<WebSocketException>(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await ws.ConnectAsync(wsUri, cts.Token);
        });
        Assert.Contains("403", connectEx.Message);
        Assert.NotEqual(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task Origin_WrongPort_SignalR_NegotiateAndWebSocket_RejectedWith403()
    {
        var email = $"origin_port_{Guid.NewGuid():N}@example.test";
        var password = "OriginPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.FacilityManager, UserStatusConstants.Active, "Origin Port Tester");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, email, password, "/", cookieContainer);

        // Server is http://127.0.0.1:{port}; wrong port is http://127.0.0.1:{port + 1}
        var uri = new Uri(_fixture.ServerUrl);
        var wrongPortOrigin = $"{uri.Scheme}://{uri.Host}:{uri.Port + 1}";

        // 1. Negotiate with wrong port
        var authReq = new HttpRequestMessage(HttpMethod.Post, "/storageUnitHub/negotiate?negotiateVersion=1");
        authReq.Headers.Add("Cookie", authCookie);
        authReq.Headers.Add("Origin", wrongPortOrigin);
        var response = await client.SendAsync(authReq);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // 2. Direct WebSocket handshake with wrong port
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Cookie", authCookie);
        ws.Options.SetRequestHeader("Origin", wrongPortOrigin);
        var wsUri = new Uri(_fixture.ServerUrl.Replace("http://", "ws://") + "/storageUnitHub");

        var connectEx = await Assert.ThrowsAsync<WebSocketException>(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await ws.ConnectAsync(wsUri, cts.Token);
        });
        Assert.Contains("403", connectEx.Message);
        Assert.NotEqual(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task Origin_SameOrigin_Negotiate_Returns200_AndDefaultPortNormalizationMatches()
    {
        var email = $"origin_same_{Guid.NewGuid():N}@example.test";
        var password = "OriginPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.FacilityManager, UserStatusConstants.Active, "Origin Same Tester");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, email, password, "/", cookieContainer);

        // 1. Same-Origin Negotiate returns 200 OK over real Kestrel
        var authReq = new HttpRequestMessage(HttpMethod.Post, "/storageUnitHub/negotiate?negotiateVersion=1");
        authReq.Headers.Add("Cookie", authCookie);
        authReq.Headers.Add("Origin", _fixture.ServerUrl);
        var response = await client.SendAsync(authReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 2. Verify default port normalization logic via unit assertions on IsAllowedHubOrigin
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("selfstorage.example.test", 80);
        Assert.True(HubOriginSecurityExtensions.IsAllowedHubOrigin(httpContext.Request, "http://selfstorage.example.test"));
        Assert.True(HubOriginSecurityExtensions.IsAllowedHubOrigin(httpContext.Request, "http://selfstorage.example.test:80"));
        Assert.False(HubOriginSecurityExtensions.IsAllowedHubOrigin(httpContext.Request, "https://selfstorage.example.test")); // wrong scheme
        Assert.False(HubOriginSecurityExtensions.IsAllowedHubOrigin(httpContext.Request, "http://selfstorage.example.test:8080")); // wrong port

        var httpsContext = new DefaultHttpContext();
        httpsContext.Request.Scheme = "https";
        httpsContext.Request.Host = new HostString("selfstorage.example.test", 443);
        Assert.True(HubOriginSecurityExtensions.IsAllowedHubOrigin(httpsContext.Request, "https://selfstorage.example.test"));
        Assert.True(HubOriginSecurityExtensions.IsAllowedHubOrigin(httpsContext.Request, "https://selfstorage.example.test:443"));
        Assert.False(HubOriginSecurityExtensions.IsAllowedHubOrigin(httpsContext.Request, "http://selfstorage.example.test")); // wrong scheme
    }

    #endregion

    #region 9. Negative Cases & Zero Information Leakage Tests

    [Fact]
    public async Task Negative_SqlInjectionPayload_InLoginForm_DoesNotCrashOrLeakSql()
    {
        // Arrange: Form submission containing aggressive SQL injection vectors
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false);
        var (token, cookieHeader) = await _fixture.GetAntiforgeryTokensAsync(client, "/Account/Login");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        request.Headers.Add("Cookie", cookieHeader);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = "admin' OR '1'='1'--",
            ["Password"] = "' UNION SELECT 1,2,3,4,5--",
            ["__RequestVerificationToken"] = token
        });

        // Act
        var response = await client.SendAsync(request);

        // Assert: Safe handling (200 with validation message in view)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // Must NOT leak database or ORM internals
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("syntax error", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack trace", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at System.", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Negative_NonExistentPage_Returns404_CustomStatusCodePage_NoStackTrace()
    {
        // Arrange
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false);

        // Act
        var response = await client.GetAsync("/NonExistent_Random_Path_12345");

        // Assert: 404 handled gracefully by UseStatusCodePagesWithReExecute
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("404", html);
        Assert.Contains("Không Tìm Thấy Trang", html);

        // Must NOT leak internals
        Assert.DoesNotContain("at Microsoft.AspNetCore", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at System.", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Negative_ZeroSecretsOrPasswordHashesOrPinInResponses()
    {
        // Arrange: Register customer with password
        var email = $"leak_check_{Guid.NewGuid():N}@example.test";
        var password = "SecretPassword123!";
        await _fixture.SeedUserAsync(email, password, RoleConstants.StorageCustomer, UserStatusConstants.Active, "Khách Không Leak");

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        await _fixture.LoginViaHttpAsync(client, email, password);

        // Fetch multiple pages
        var paths = new[] { "/Account/Profile", "/Account/SecuredData", "/Reservations", "/Home/Error", "/Home/StatusCode/404" };
        foreach (var path in paths)
        {
            var resp = await client.GetAsync(path);
            var content = await resp.Content.ReadAsStringAsync();

            // Assert: Absolutely NO password hashes, salt, secrets or internal PINs in outputs
            Assert.DoesNotContain("$argon2", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AQAAAA", content, StringComparison.OrdinalIgnoreCase); // Common PBKDF2 base64 header
            Assert.DoesNotContain("JWT_SECRET_KEY", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DefaultConnection", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    #endregion

    #region 10. Real SignalR Hub Contract over Loopback TCP (/storageUnitHub)

    [Fact]
    public async Task SignalR_Contract_RouteAndMethodsPreserved_OverRealKestrel()
    {
        // Arrange: Seed manager for facility
        var hierarchy = await _fixture.SeedFacilityHierarchyAsync("hub_real");
        var managerEmail = $"mgr_hub_{Guid.NewGuid():N}@example.test";
        var password = "ManagerPass123!";
        await _fixture.SeedUserAsync(managerEmail, password, RoleConstants.FacilityManager, UserStatusConstants.Active, "Quản Lý Realtime",
            assignedFacilityId: hierarchy.Facility.id, assignedRole: RoleConstants.FacilityManager);

        var cookieContainer = new CookieContainer();
        using var client = _fixture.CreateHttpClient(allowAutoRedirect: false, cookieContainer: cookieContainer);
        var (authCookie, _) = await _fixture.LoginViaHttpAsync(client, managerEmail, password);

        // Connect to real Kestrel loopback endpoint /storageUnitHub
        var hubConnection = new HubConnectionBuilder()
            .WithUrl($"{_fixture.ServerUrl}/storageUnitHub", options =>
            {
                options.Headers.Add("Cookie", authCookie);
            })
            .Build();

        StorageUnitRealtimeEventDto? receivedCreatedEvent = null;
        var createEventTcs = new TaskCompletionSource<StorageUnitRealtimeEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register listener for Receiver_CreateStorageUnit broadcast
        hubConnection.On<StorageUnitRealtimeEventDto>("Receiver_CreateStorageUnit", item =>
        {
            receivedCreatedEvent = item;
            createEventTcs.TrySetResult(item);
        });

        await hubConnection.StartAsync();

        // 1. Invocable: HubCreate_StorageUnit
        var newUnitCode = $"RT-REAL-{Guid.NewGuid():N}".Substring(0, 16).ToUpperInvariant();
        var createPayload = JsonSerializer.Serialize(new
        {
            FacilityId = hierarchy.Facility.id,
            UnitTypeId = hierarchy.UnitType.id,
            AreaId = hierarchy.Area.id,
            UnitCode = newUnitCode,
            FloorLabel = "Tầng 1",
            ZoneLabel = "Dãy A",
            Notes = "Tạo qua test Kestrel thực"
        });

        var createdDto = await hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", createPayload);
        Assert.NotNull(createdDto);
        Assert.Equal(newUnitCode, createdDto.unitCode);

        // Wait for server broadcast
        var completed = await Task.WhenAny(createEventTcs.Task, Task.Delay(5000));
        Assert.True(completed == createEventTcs.Task, "Receiver_CreateStorageUnit broadcast was not received.");
        Assert.NotNull(receivedCreatedEvent);
        Assert.Equal(newUnitCode, receivedCreatedEvent.unitCode);

        // 2. Invocable: HubUpdate_StorageUnit
        StorageUnitRealtimeEventDto? receivedUpdatedEvent = null;
        var updateEventTcs = new TaskCompletionSource<StorageUnitRealtimeEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        hubConnection.On<StorageUnitRealtimeEventDto>("Receiver_UpdateStorageUnit", item =>
        {
            receivedUpdatedEvent = item;
            updateEventTcs.TrySetResult(item);
        });

        var updatePayload = JsonSerializer.Serialize(new
        {
            Id = createdDto.id,
            UnitCode = newUnitCode,
            AreaId = hierarchy.Area.id,
            FloorLabel = "Tầng 2 Đã Sửa",
            ZoneLabel = "Dãy B Mới",
            Notes = "Cập nhật qua test Kestrel thực",
            ExpectedUpdatedAt = createdDto.updatedAt
        });

        var updatedDto = await hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubUpdate_StorageUnit", updatePayload);
        Assert.NotNull(updatedDto);

        var completedUpdate = await Task.WhenAny(updateEventTcs.Task, Task.Delay(5000));
        Assert.True(completedUpdate == updateEventTcs.Task, "Receiver_UpdateStorageUnit broadcast was not received.");
        Assert.NotNull(receivedUpdatedEvent);

        // 3. Invocable: HubDelete_StorageUnit
        string? receivedDeletedId = null;
        var deleteEventTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hubConnection.On<string>("Receiver_DeleteStorageUnit", id =>
        {
            receivedDeletedId = id;
            deleteEventTcs.TrySetResult(id);
        });

        var deleteResult = await hubConnection.InvokeAsync<string>("HubDelete_StorageUnit", createdDto.id.ToString(), updatedDto.updatedAt);
        Assert.Equal(createdDto.id.ToString(), deleteResult);

        var completedDelete = await Task.WhenAny(deleteEventTcs.Task, Task.Delay(5000));
        Assert.True(completedDelete == deleteEventTcs.Task, "Receiver_DeleteStorageUnit broadcast was not received.");
        Assert.Equal(createdDto.id.ToString(), receivedDeletedId);

        await hubConnection.StopAsync();
    }

    [Fact]
    public async Task OpenWebSocket_ClosesWhenAuthenticationTicketExpires_AndFreshTicketCanReconnect()
    {
        // 1. Arrange: Seed active manager with facility assignment
        var email = $"signalr_expiry_{Guid.NewGuid():N}@example.test";
        var password = "SignalRExpiryPass123!";
        var hierarchy = await _fixture.SeedFacilityHierarchyAsync("Hà Nội Expiry Hub");
        var managerUser = await _fixture.SeedUserAsync(
            email, password, RoleConstants.FacilityManager, UserStatusConstants.Active, "Expiry Hub Manager",
            assignedFacilityId: hierarchy.Facility.id, assignedRole: RoleConstants.FacilityManager);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, managerUser.id.ToString()),
            new Claim(ClaimTypes.Name, "Expiry Hub Manager"),
            new Claim(ClaimTypes.Role, RoleConstants.FacilityManager),
            new Claim("facility_scope", hierarchy.Facility.id.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthExtensions.CookieScheme);
        var principal = new ClaimsPrincipal(identity);

        // Create short-lived ticket: valid for 2 seconds
        var shortLivedProperties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(2),
            AllowRefresh = false
        };
        var shortLivedTicket = new AuthenticationTicket(principal, shortLivedProperties, CookieAuthExtensions.CookieScheme);

        var dataProtectionProvider = _fixture.Services.GetRequiredService<IDataProtectionProvider>();
        var protector = dataProtectionProvider.CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", CookieAuthExtensions.CookieScheme, "v2");
        var ticketDataFormat = new TicketDataFormat(protector);
        var shortLivedCookie = ticketDataFormat.Protect(shortLivedTicket);

        // 2. Establish WebSocket connection over loopback TCP with short-lived ticket
        var closedTcs = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hubConnection = new HubConnectionBuilder()
            .WithUrl($"{_fixture.ServerUrl}/storageUnitHub", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.Headers.Add("Cookie", $"{CookieAuthExtensions.CookieName}={shortLivedCookie}");
                options.Cookies.Add(new Cookie(CookieAuthExtensions.CookieName, shortLivedCookie, "/", new Uri(_fixture.ServerUrl).Host));
            })
            .Build();

        hubConnection.Closed += ex =>
        {
            closedTcs.TrySetResult(ex);
            return Task.CompletedTask;
        };

        await hubConnection.StartAsync();
        Assert.Equal(HubConnectionState.Connected, hubConnection.State);

        // 3. Wait across expiry (ticket expires in 2s, wait up to 6s for server to close connection)
        var closedTask = await Task.WhenAny(closedTcs.Task, Task.Delay(TimeSpan.FromSeconds(6)));
        Assert.True(closedTask == closedTcs.Task, "WebSocket connection did not close after authentication ticket expired.");
        Assert.Equal(HubConnectionState.Disconnected, hubConnection.State);

        // 4. Inability to invoke hub method afterward
        await Assert.ThrowsAnyAsync<Exception>(() =>
            hubConnection.InvokeAsync<string>("HubDelete_StorageUnit", hierarchy.Unit.id.ToString(), (string?)null));

        // 5. Inability to receive private events afterward (publish an event, verify disconnected connection never receives it)
        var receivedEventAfterClose = false;
        hubConnection.On<string>("Receiver_DeleteStorageUnit", _ =>
        {
            receivedEventAfterClose = true;
        });
        using (var scope = _fixture.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IStorageUnitRealtimePublisher>();
            await publisher.PublishDeletedAsync(99999, hierarchy.Facility.id);
        }
        await Task.Delay(500);
        Assert.False(receivedEventAfterClose, "Disconnected connection received a private event after ticket expiry.");

        // 6. Verify fresh valid ticket can reconnect normally
        var freshProperties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            AllowRefresh = true
        };
        var freshTicket = new AuthenticationTicket(principal, freshProperties, CookieAuthExtensions.CookieScheme);
        var freshCookie = ticketDataFormat.Protect(freshTicket);

        var freshHubConnection = new HubConnectionBuilder()
            .WithUrl($"{_fixture.ServerUrl}/storageUnitHub", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.Headers.Add("Cookie", $"{CookieAuthExtensions.CookieName}={freshCookie}");
                options.Cookies.Add(new Cookie(CookieAuthExtensions.CookieName, freshCookie, "/", new Uri(_fixture.ServerUrl).Host));
            })
            .Build();

        await freshHubConnection.StartAsync();
        Assert.Equal(HubConnectionState.Connected, freshHubConnection.State);

        // Verify fresh connection can invoke hub method and receive response
        var deleteResult = await freshHubConnection.InvokeAsync<string>("HubDelete_StorageUnit", hierarchy.Unit.id.ToString(), (string?)null);
        Assert.Equal(hierarchy.Unit.id.ToString(), deleteResult);

        await freshHubConnection.StopAsync();
    }

    #endregion
}
