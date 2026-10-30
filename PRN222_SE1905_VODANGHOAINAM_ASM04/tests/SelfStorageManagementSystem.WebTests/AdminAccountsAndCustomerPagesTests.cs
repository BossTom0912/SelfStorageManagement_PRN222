using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.WebTests.Fixtures;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

public class AdminAccountsAndCustomerPagesTests : IClassFixture<CheckInWebTestFixture>
{
    private readonly CheckInWebTestFixture _factory;

    public AdminAccountsAndCustomerPagesTests(CheckInWebTestFixture factory)
    {
        _factory = factory;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(html, @"value=""([^""]+)"" name=""__RequestVerificationToken""");
        }
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    #region 1. Phân quyền & Chặn truy cập chéo (Access Control & Cross-Access)

    [Fact]
    public async Task Anonymous_AdminAccounts_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Admin/Accounts");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Customer_AdminAccounts_ReturnsForbidden()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/Admin/Accounts");

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString.Contains("Forbidden") == true),
            "Khách hàng thuê kho không được phép truy cập trang quản trị tài khoản");
    }

    [Fact]
    public async Task Staff_AdminAccounts_ReturnsForbidden()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.StaffAssigned.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/Admin/Accounts");

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString.Contains("Forbidden") == true),
            "Nhân viên cơ sở không được phép truy cập trang quản trị tài khoản");
    }

    [Fact]
    public async Task Anonymous_MyUnits_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/MyUnits");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Admin_MyUnits_ReturnsForbidden()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/MyUnits");

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString.Contains("Forbidden") == true),
            "Quản trị viên không có quyền truy cập trang My Units dành riêng cho khách hàng");
    }

    [Fact]
    public async Task Anonymous_Notifications_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Notifications");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);
    }

    #endregion

    #region 2. Quản Trị Danh Sách Tài Khoản (/Admin/Accounts)

    [Fact]
    public async Task Admin_AdminAccounts_Index_ReturnsSuccessWithAccountsList()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/Admin/Accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Quản Trị Tài Khoản", html);
        Assert.Contains(seed.AdminUser.email, html);
        Assert.Contains(seed.CustomerUser.email, html);
        Assert.Contains("Thêm Nhân Viên Mới", html);
        Assert.Contains("adminAccountsMessageArea", html);
    }

    [Fact]
    public async Task Admin_AdminAccounts_FilterBySearchTerm_ReturnsFilteredAccounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync($"/Admin/Accounts?SearchTerm={seed.CustomerUser.email}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(seed.CustomerUser.email, html);
        Assert.DoesNotContain(seed.StaffAssigned.email, html);
    }

    [Fact]
    public async Task Admin_AdminAccounts_FilterByStatus_ReturnsFilteredAccounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/Admin/Accounts?Status=active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(seed.AdminUser.email, html);
    }

    [Fact]
    public async Task Admin_AdminAccounts_FilterByRoleCode_ReturnsFilteredAccounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync($"/Admin/Accounts?RoleCode={RoleConstants.SystemAdministrator}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(seed.AdminUser.email, html);
        Assert.DoesNotContain(seed.CustomerUser.email, html);
    }

    [Fact]
    public async Task Admin_AdminAccounts_Pagination_CalculatesCorrectPages()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/Admin/Accounts?PageNumber=1&PageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Trang 1 /", html);
    }

    #endregion

    #region 3. Khởi Tạo Tài Khoản Nhân Viên (/Admin/Accounts/Create)

    [Fact]
    public async Task Admin_CreateStaffAccount_GetPage_ReturnsSuccessWithFacilities()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/Admin/Accounts/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Khởi Tạo Tài Khoản Nhân Viên", html);
        Assert.Contains(seed.Facility.name, html);
    }

    [Fact]
    public async Task Admin_CreateStaffAccount_PostValidData_CreatesStaffWithProfileAndRole()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var getPage = await client.GetAsync("/Admin/Accounts/Create");
        var html = await getPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var newStaffEmail = $"newstaff_{suffix}@example.com";
        var newEmpCode = $"EMP-{suffix.ToUpperInvariant()}";

        var postData = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Email"] = newStaffEmail,
            ["Input.Password"] = "Password123!",
            ["Input.PhoneNumber"] = "0987654321",
            ["Input.FullName"] = $"Nhân Viên Mới {suffix}",
            ["Input.EmployeeCode"] = newEmpCode,
            ["Input.HireDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["Input.RoleCode"] = RoleConstants.FacilityStaff,
            ["Input.InitialFacilityId"] = seed.Facility.id.ToString()
        };

        var postResponse = await client.PostAsync("/Admin/Accounts/Create", new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);
        Assert.Contains("/Admin/Accounts/Details", postResponse.Headers.Location?.OriginalString);

        // Verify in DB
        using var db = _factory.CreateDbContext();
        var createdUser = await db.users
            .Include(u => u.employee_profile)
            .Include(u => u.user_roleusers)
                .ThenInclude(ur => ur.role)
            .FirstOrDefaultAsync(u => u.email == newStaffEmail);

        Assert.NotNull(createdUser);
        Assert.NotNull(createdUser.employee_profile);
        Assert.Equal(newEmpCode, createdUser.employee_profile.employee_code);
        Assert.Contains(createdUser.user_roleusers, ur => ur.role.code == RoleConstants.FacilityStaff);

        // Verify initial facility assignment
        var assignment = await db.staff_facility_assignments
            .FirstOrDefaultAsync(a => a.employee_id == createdUser.id && a.facility_id == seed.Facility.id);
        Assert.NotNull(assignment);
        Assert.Equal(RoleConstants.FacilityStaff, assignment.assignment_role);
    }

    [Fact]
    public async Task Admin_CreateStaffAccount_DuplicateEmail_ShowsConflictError()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var getPage = await client.GetAsync("/Admin/Accounts/Create");
        var html = await getPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var postData = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Email"] = seed.CustomerUser.email, // Duplicate email!
            ["Input.Password"] = "Password123!",
            ["Input.FullName"] = "Duplicate Name",
            ["Input.EmployeeCode"] = $"DUP-{suffix}",
            ["Input.HireDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["Input.RoleCode"] = RoleConstants.FacilityStaff
        };

        var postResponse = await client.PostAsync("/Admin/Accounts/Create", new FormUrlEncodedContent(postData));
        var postHtml = await postResponse.Content.ReadAsStringAsync();

        Assert.True(
            postHtml.Contains("Email is already registered") || postResponse.StatusCode == HttpStatusCode.OK,
            "Hệ thống phải bắt lỗi trùng email và hiển thị cảnh báo cho admin");
    }

    #endregion

    #region 4. Chi Tiết Tài Khoản & Các Thao Tác Quản Trị (/Admin/Accounts/Details)

    [Fact]
    public async Task Admin_AccountDetails_GetPage_ReturnsSuccessWithDetails()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync($"/Admin/Accounts/Details?id={seed.StaffAssigned.id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(seed.StaffAssigned.email, html);
        Assert.Contains(seed.Facility.name, html);
        Assert.Contains("Phân Công Cơ Sở", html);
    }

    [Fact]
    public async Task Admin_AccountDetails_NonExistentId_ReturnsNotFound()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/Admin/Accounts/Details?id=999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_AccountDetails_UpdateStatus_ToLocked_Succeeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var getPage = await client.GetAsync($"/Admin/Accounts/Details?id={seed.CustomerUser.id}");
        var html = await getPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var postData = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["StatusInput.Status"] = "locked"
        };

        var postResponse = await client.PostAsync($"/Admin/Accounts/Details?id={seed.CustomerUser.id}&handler=UpdateStatus", new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);

        // Verify in DB
        using var db = _factory.CreateDbContext();
        var user = await db.users.FindAsync(seed.CustomerUser.id);
        Assert.NotNull(user);
        Assert.Equal("locked", user.status);
    }

    [Fact]
    public async Task Admin_AccountDetails_UpdateStatus_SoleAdminLockout_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        // Ensure seed.AdminUser is the sole active system administrator in the test database
        using (var db = _factory.CreateDbContext())
        {
            var otherAdmins = await db.user_roles
                .Include(ur => ur.user)
                .Where(ur => ur.role.code == RoleConstants.SystemAdministrator && ur.user_id != seed.AdminUser.id)
                .Select(ur => ur.user)
                .ToListAsync();
            foreach (var oa in otherAdmins)
            {
                oa.status = "disabled";
            }
            await db.SaveChangesAsync();
        }

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var getPage = await client.GetAsync($"/Admin/Accounts/Details?id={seed.AdminUser.id}");
        var html = await getPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var postData = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["StatusInput.Status"] = "locked"
        };

        var postResponse = await client.PostAsync($"/Admin/Accounts/Details?id={seed.AdminUser.id}&handler=UpdateStatus", new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var postHtml = await postResponse.Content.ReadAsStringAsync();

        Assert.Contains("Cannot deactivate or lock the only remaining active system administrator account", postHtml);

        // Verify status remains active
        using var dbCheck = _factory.CreateDbContext();
        var admin = await dbCheck.users.FindAsync(seed.AdminUser.id);
        Assert.NotNull(admin);
        Assert.Equal(UserStatusConstants.Active, admin.status);
    }

    [Fact]
    public async Task Admin_AccountDetails_ManageRoles_Succeeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var getPage = await client.GetAsync($"/Admin/Accounts/Details?id={seed.StaffAssigned.id}");
        var html = await getPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var postData = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("RolesInput.SelectedRoles", RoleConstants.FacilityStaff),
            new("RolesInput.SelectedRoles", RoleConstants.FacilityManager)
        };

        var postResponse = await client.PostAsync($"/Admin/Accounts/Details?id={seed.StaffAssigned.id}&handler=ManageRoles", new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);

        // Verify roles updated in DB
        using var db = _factory.CreateDbContext();
        var roles = await db.user_roles
            .Include(ur => ur.role)
            .Where(ur => ur.user_id == seed.StaffAssigned.id)
            .Select(ur => ur.role.code)
            .ToListAsync();

        Assert.Contains(RoleConstants.FacilityStaff, roles);
        Assert.Contains(RoleConstants.FacilityManager, roles);
    }

    [Fact]
    public async Task Admin_AccountDetails_ManageRoles_SoleAdminRevocation_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        // Ensure seed.AdminUser is the sole active system administrator in the test database
        using (var db = _factory.CreateDbContext())
        {
            var otherAdmins = await db.user_roles
                .Include(ur => ur.user)
                .Where(ur => ur.role.code == RoleConstants.SystemAdministrator && ur.user_id != seed.AdminUser.id)
                .Select(ur => ur.user)
                .ToListAsync();
            foreach (var oa in otherAdmins)
            {
                oa.status = "disabled";
            }
            await db.SaveChangesAsync();
        }

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var getPage = await client.GetAsync($"/Admin/Accounts/Details?id={seed.AdminUser.id}");
        var html = await getPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        // Attempt to remove system_administrator, keeping only storage_customer
        var postData = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("RolesInput.SelectedRoles", RoleConstants.StorageCustomer)
        };

        var postResponse = await client.PostAsync($"/Admin/Accounts/Details?id={seed.AdminUser.id}&handler=ManageRoles", new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var postHtml = await postResponse.Content.ReadAsStringAsync();

        Assert.Contains("Cannot revoke the system administrator role from the only remaining system administrator", postHtml);

        // Verify role not revoked
        using var dbCheck = _factory.CreateDbContext();
        var hasAdmin = await dbCheck.user_roles
            .Include(ur => ur.role)
            .AnyAsync(ur => ur.user_id == seed.AdminUser.id && ur.role.code == RoleConstants.SystemAdministrator);
        Assert.True(hasAdmin);
    }

    [Fact]
    public async Task Admin_AccountDetails_ManageRoles_AssignEmployeeRoleWithoutProfile_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var getPage = await client.GetAsync($"/Admin/Accounts/Details?id={seed.CustomerUser.id}");
        var html = await getPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        // Attempt to give facility_staff role to customer who has NO employee_profile
        var postData = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("RolesInput.SelectedRoles", RoleConstants.FacilityStaff)
        };

        var postResponse = await client.PostAsync($"/Admin/Accounts/Details?id={seed.CustomerUser.id}&handler=ManageRoles", new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var postHtml = await postResponse.Content.ReadAsStringAsync();

        Assert.Contains("Cannot assign employee roles to an account without an employee profile", postHtml);
    }

    [Fact]
    public async Task Admin_AccountDetails_AssignFacility_Succeeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var getPage = await client.GetAsync($"/Admin/Accounts/Details?id={seed.StaffUnassigned.id}");
        var html = await getPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var now = DateTimeOffset.UtcNow;
        var postData = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["AssignmentInput.FacilityId"] = seed.Facility.id.ToString(),
            ["AssignmentInput.AssignmentRole"] = RoleConstants.FacilityStaff,
            ["AssignmentInput.StartsAt"] = now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            ["AssignmentInput.EndsAt"] = now.AddDays(30).ToString("yyyy-MM-ddTHH:mm:sszzz")
        };

        var postResponse = await client.PostAsync($"/Admin/Accounts/Details?id={seed.StaffUnassigned.id}&handler=AssignFacility", new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);

        using var db = _factory.CreateDbContext();
        var assignment = await db.staff_facility_assignments
            .FirstOrDefaultAsync(a => a.employee_id == seed.StaffUnassigned.id && a.facility_id == seed.Facility.id);

        Assert.NotNull(assignment);
        Assert.Equal(RoleConstants.FacilityStaff, assignment.assignment_role);
    }

    [Fact]
    public async Task Admin_AccountDetails_AssignFacility_Overlapping_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var getPage = await client.GetAsync($"/Admin/Accounts/Details?id={seed.StaffAssigned.id}");
        var html = await getPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        // StaffAssigned already has an active assignment for seed.Facility with RoleConstants.FacilityStaff!
        var now = DateTimeOffset.UtcNow;
        var postData = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["AssignmentInput.FacilityId"] = seed.Facility.id.ToString(),
            ["AssignmentInput.AssignmentRole"] = RoleConstants.FacilityStaff,
            ["AssignmentInput.StartsAt"] = now.ToString("yyyy-MM-ddTHH:mm:sszzz")
        };

        var postResponse = await client.PostAsync($"/Admin/Accounts/Details?id={seed.StaffAssigned.id}&handler=AssignFacility", new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var postHtml = await postResponse.Content.ReadAsStringAsync();

        Assert.Contains("overlapping assignments", postHtml);
    }

    [Fact]
    public async Task Admin_AccountDetails_TerminateFacilityAssignment_Succeeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        // Find existing assignment of StaffAssigned
        using var db = _factory.CreateDbContext();
        var assignment = await db.staff_facility_assignments
            .FirstAsync(a => a.employee_id == seed.StaffAssigned.id && a.facility_id == seed.Facility.id);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var getPage = await client.GetAsync($"/Admin/Accounts/Details?id={seed.StaffAssigned.id}");
        var html = await getPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var postData = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        };

        var postResponse = await client.PostAsync(
            $"/Admin/Accounts/Details?id={seed.StaffAssigned.id}&assignmentId={assignment.id}&handler=TerminateAssignment",
            new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);

        using var dbCheck = _factory.CreateDbContext();
        var terminated = await dbCheck.staff_facility_assignments.FindAsync(assignment.id);
        Assert.NotNull(terminated);
        Assert.NotNull(terminated.ends_at);
        Assert.True(terminated.ends_at <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Admin_AccountDetails_TerminateAlreadyExpiredAssignment_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        using var db = _factory.CreateDbContext();
        var pastAssignment = new staff_facility_assignment
        {
            employee_id = seed.StaffAssigned.id,
            facility_id = seed.Facility.id,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = DateTimeOffset.UtcNow.AddDays(-10),
            ends_at = DateTimeOffset.UtcNow.AddDays(-1), // already ended!
            assigned_by = seed.AdminUser.id,
            created_at = DateTimeOffset.UtcNow.AddDays(-10)
        };
        db.staff_facility_assignments.Add(pastAssignment);
        await db.SaveChangesAsync();

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);
        var getPage = await client.GetAsync($"/Admin/Accounts/Details?id={seed.StaffAssigned.id}");
        var html = await getPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var postData = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        };

        var postResponse = await client.PostAsync(
            $"/Admin/Accounts/Details?id={seed.StaffAssigned.id}&assignmentId={pastAssignment.id}&handler=TerminateAssignment",
            new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var postHtml = await postResponse.Content.ReadAsStringAsync();

        Assert.Contains("This assignment has already ended", postHtml);
    }

    #endregion

    #region 5. Khách Hàng - Ô Kho Của Tôi (/MyUnits)

    [Fact]
    public async Task Customer_MyUnits_Index_ReturnsCustomerUnits()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        // Set agreement to active with active unit allocation
        using (var db = _factory.CreateDbContext())
        {
            var agr = await db.rental_agreements.FindAsync(seed.Agreement.id);
            agr!.status = "active";
            agr.checked_in_at = DateTimeOffset.UtcNow;

            var alloc = new unit_allocation
            {
                agreement_id = agr.id,
                storage_unit_id = seed.Unit1.id,
                allocation_kind = "assigned",
                allocation_start_date = DateOnly.FromDateTime(DateTime.UtcNow),
                allocation_end_date = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
                created_at = DateTimeOffset.UtcNow,
                status = "active"
            };
            db.unit_allocations.Add(alloc);
            await db.SaveChangesAsync();
        }

        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/MyUnits");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Ô Kho Của Tôi", html);
        Assert.Contains(seed.Unit1.unit_code, html);
        Assert.Contains(seed.Agreement.agreement_no, html);
        Assert.Contains("Biên Bản Bàn Giao", html);
        Assert.Contains("myUnitsMessageArea", html);
    }

    [Fact]
    public async Task Customer_MyUnits_Pagination_Works()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/MyUnits?PageNumber=1&PageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region 6. Chi Tiết Bàn Giao & Bảo Vệ Mã PIN (/MyUnits/HandoverDetails)

    [Fact]
    public async Task Customer_MyUnits_HandoverDetails_ReturnsHandoverMetadata_WithoutPlaintextPin()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        // Seed active handover, allocation, inspection, and access credential
        using (var db = _factory.CreateDbContext())
        {
            var agr = await db.rental_agreements.FindAsync(seed.Agreement.id);
            agr!.status = "active";
            agr.checked_in_at = DateTimeOffset.UtcNow;

            var alloc = new unit_allocation
            {
                agreement_id = agr.id,
                storage_unit_id = seed.Unit1.id,
                allocation_kind = "assigned",
                allocation_start_date = DateOnly.FromDateTime(DateTime.UtcNow),
                allocation_end_date = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
                created_at = DateTimeOffset.UtcNow,
                status = "active"
            };
            db.unit_allocations.Add(alloc);
            await db.SaveChangesAsync();

            var insp = new inspection
            {
                facility_id = seed.Facility.id,
                storage_unit_id = seed.Unit1.id,
                agreement_id = agr.id,
                inspected_by = seed.StaffAssigned.id,
                inspection_type = "check_in",
                status = "completed",
                overall_condition = "good",
                created_at = DateTimeOffset.UtcNow
            };
            db.inspections.Add(insp);
            await db.SaveChangesAsync();

            var handover = new handover_record
            {
                agreement_id = agr.id,
                unit_allocation_id = alloc.id,
                inspection_id = insp.id,
                handled_by = seed.StaffAssigned.id,
                handover_type = "check_in",
                notes = "Đã bàn giao chìa khóa và kiểm tra sạch sẽ.",
                created_at = DateTimeOffset.UtcNow
            };
            db.handover_records.Add(handover);

            var cred = new access_credential
            {
                agreement_id = agr.id,
                credential_type = "pin_code",
                secret_digest = "HashedSecretPin123456",
                status = "active",
                issued_at = DateTimeOffset.UtcNow,
                expires_at = DateTimeOffset.UtcNow.AddMonths(1),
                created_at = DateTimeOffset.UtcNow
            };
            db.access_credentials.Add(cred);

            await db.SaveChangesAsync();
        }

        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync($"/MyUnits/HandoverDetails?agreementId={seed.Agreement.id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // 1. Metadata bàn giao hợp lệ
        Assert.Contains(seed.Agreement.agreement_no, html);
        Assert.Contains(seed.Unit1.unit_code, html);
        Assert.Contains(seed.Facility.name, html);
        Assert.Contains("Đã bàn giao chìa khóa", html);
        Assert.Contains("Tốt (good)", html);
        Assert.Contains("pinSecurityNoticeBox", html);

        // 2. CRITICAL SECURITY CHECK: Tuyệt đối KHÔNG có nút bấm hay form hiển thị plaintext PIN
        Assert.DoesNotContain("HashedSecretPin123456", html);
        Assert.DoesNotContain("btnShowPin", html);
        Assert.DoesNotContain("btnRevealPin", html);
        Assert.DoesNotContain("btnRetrievePin", html);
        Assert.DoesNotContain("Xem mã PIN", html);
        Assert.DoesNotContain("Hiện mã PIN", html);
        Assert.Contains("Zero-Persistence", html);
        Assert.Contains("hoàn toàn không hỗ trợ tính năng xem lại mã PIN trực tuyến", html);
    }

    [Fact]
    public async Task Customer_CrossCustomer_HandoverDetails_CannotAccessOtherCustomerData()
    {
        var suffixA = Guid.NewGuid().ToString("N")[..6];
        var seedA = await _factory.SeedFullCheckInEnvironmentAsync(suffixA);

        var suffixB = Guid.NewGuid().ToString("N")[..6];
        var seedB = await _factory.SeedFullCheckInEnvironmentAsync(suffixB);

        // Customer B tries to view Customer A's Handover Details
        var clientB = await _factory.CreateAuthenticatedClientAsync(seedB.CustomerUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await clientB.GetAsync($"/MyUnits/HandoverDetails?agreementId={seedA.Agreement.id}");

        // Finding T14-F01 Fix: Must return HTTP 404 NotFound
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // Error card is displayed, no metadata leaked!
        Assert.Contains("Không Thể Truy Cập Biên Bản Bàn Giao", html);
        Assert.Contains("hợp đồng không thuộc quyền sở hữu của bạn", html);
        Assert.DoesNotContain(seedA.Agreement.agreement_no, html);
        Assert.DoesNotContain(seedA.Facility.name, html);
    }

    [Fact]
    public async Task Customer_HandoverDetails_NonExistentAgreementId_ReturnsNotFound()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, CheckInWebTestFixture.DefaultPassword);
        var nonExistentId = 999_999_999L;
        var response = await client.GetAsync($"/MyUnits/HandoverDetails?agreementId={nonExistentId}");

        // Finding T14-F01 Fix: Must return HTTP 404 NotFound with identical safe error message
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Không Thể Truy Cập Biên Bản Bàn Giao", html);
        Assert.Contains("hợp đồng không thuộc quyền sở hữu của bạn", html);
        Assert.DoesNotContain("Exception", html);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public async Task Customer_HandoverDetails_InvalidAgreementId_ReturnsNotFound(long invalidId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync($"/MyUnits/HandoverDetails?agreementId={invalidId}");

        // Finding T14-F01 Fix: Must return HTTP 404 NotFound with identical safe error message
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Không Thể Truy Cập Biên Bản Bàn Giao", html);
        Assert.Contains("hợp đồng không thuộc quyền sở hữu của bạn", html);
        Assert.DoesNotContain("Exception", html);
    }

    #endregion

    #region 7. Khách Hàng - Danh Sách Thông Báo (/Notifications)

    [Fact]
    public async Task Customer_Notifications_ReturnsCustomerNotifications()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        // Seed notifications for customer
        using (var db = _factory.CreateDbContext())
        {
            var notif = new notification
            {
                user_id = seed.CustomerUser.id,
                channel = "in_app",
                template_code = "CHECKIN_SUCCESS",
                subject = $"Chào mừng bạn đến với kho {seed.Facility.name}",
                payload = "Bàn giao kho hoàn tất. Chúc bạn sử dụng dịch vụ vui vẻ!",
                status = "sent",
                scheduled_at = DateTimeOffset.UtcNow,
                sent_at = DateTimeOffset.UtcNow,
                created_at = DateTimeOffset.UtcNow
            };
            db.notifications.Add(notif);
            await db.SaveChangesAsync();
        }

        var client = await _factory.CreateAuthenticatedClientAsync(seed.CustomerUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await client.GetAsync("/Notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Thông Báo Của Tôi", html);
        Assert.Contains($"Chào mừng bạn đến với kho {seed.Facility.name}", html);
        Assert.Contains("CHECKIN_SUCCESS", html);
        Assert.Contains("notificationsMessageArea", html);
    }

    [Fact]
    public async Task Customer_CrossCustomer_Notifications_CannotSeeOtherCustomerNotifications()
    {
        var suffixA = Guid.NewGuid().ToString("N")[..6];
        var seedA = await _factory.SeedFullCheckInEnvironmentAsync(suffixA);

        var suffixB = Guid.NewGuid().ToString("N")[..6];
        var seedB = await _factory.SeedFullCheckInEnvironmentAsync(suffixB);

        // Seed private notification for customer A only
        using (var db = _factory.CreateDbContext())
        {
            var notifA = new notification
            {
                user_id = seedA.CustomerUser.id,
                channel = "in_app",
                template_code = "SECRET_DISCOUNT",
                subject = $"Thông Báo Bí Mật Khách Hàng A {suffixA}",
                payload = "Mã ưu đãi riêng tư cho A",
                status = "sent",
                scheduled_at = DateTimeOffset.UtcNow,
                sent_at = DateTimeOffset.UtcNow,
                created_at = DateTimeOffset.UtcNow
            };
            db.notifications.Add(notifA);
            await db.SaveChangesAsync();
        }

        // Customer B visits /Notifications
        var clientB = await _factory.CreateAuthenticatedClientAsync(seedB.CustomerUser.email, CheckInWebTestFixture.DefaultPassword);
        var response = await clientB.GetAsync("/Notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain($"Thông Báo Bí Mật Khách Hàng A {suffixA}", html);
        Assert.DoesNotContain("Mã ưu đãi riêng tư cho A", html);
    }

    #endregion

    #region 8. Bảo Vệ Chống Giả Mạo Yêu Cầu (Antiforgery Enforcement)

    [Fact]
    public async Task Admin_Mutations_RequireAntiforgeryToken()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var seed = await _factory.SeedFullCheckInEnvironmentAsync(suffix);

        var client = await _factory.CreateAuthenticatedClientAsync(seed.AdminUser.email, CheckInWebTestFixture.DefaultPassword);

        // POST without token should be rejected (400 Bad Request)
        var postDataWithoutToken = new Dictionary<string, string>
        {
            ["StatusInput.Status"] = "locked"
        };

        var response = await client.PostAsync(
            $"/Admin/Accounts/Details?id={seed.CustomerUser.id}&handler=UpdateStatus",
            new FormUrlEncodedContent(postDataWithoutToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion
}
