using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.WebTests.Fixtures;

public class CheckInWebTestFixture : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString("N");
    public const string TestPinHmacSecret = "CheckInTestHmacSecret32BytesLong!";
    public const string DefaultPassword = "Password123!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CheckIn:PinHmacSecret"] = TestPinHmacSecret
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddSingleton(System.Text.Encodings.Web.HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All));

            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<SelfStorageDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<SelfStorageDbContext>(options =>
            {
                options.UseInMemoryDatabase($"SelfStorage_CheckInTests_{_dbName}");
            });
        });
    }

    public SelfStorageDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
    }

    public async Task SeedRolesAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();

        var rolesToSeed = new List<(short id, string code, string name)>
        {
            (1, RoleConstants.StorageCustomer, "Customer"),
            (2, RoleConstants.FacilityStaff, "Facility Staff"),
            (3, RoleConstants.FacilityManager, "Facility Manager"),
            (4, RoleConstants.BusinessOperationsManager, "Operations Manager"),
            (5, RoleConstants.SystemAdministrator, "System Administrator")
        };

        var existingCodes = await db.roles.Select(r => r.code).ToListAsync();
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

    public async Task<CheckInSeedResult> SeedFullCheckInEnvironmentAsync(string testSuffix)
    {
        await SeedRolesAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var endDate = today.AddMonths(1);

        // Facility
        var facility = new facility
        {
            code = $"FAC-{testSuffix.ToUpperInvariant()}",
            name = $"Cơ Sở Kiểm Thử {testSuffix}",
            address_line = "123 Vo Van Ngan",
            city = "Ho Chi Minh City",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh",
            created_at = now,
            updated_at = now
        };
        db.facilities.Add(facility);

        // Unit Type
        var unitType = new unit_type
        {
            code = $"UT-{testSuffix.ToUpperInvariant()}",
            name = $"Kho Standard {testSuffix}",
            area_m2 = 4.0m,
            climate_controlled = false,
            is_active = true,
            created_at = now,
            updated_at = now
        };
        db.unit_types.Add(unitType);

        // Policy Version
        var policy = new policy_version
        {
            policy_type = "rental_terms",
            version = $"v-{testSuffix}",
            content = "{}",
            valid_from = new DateOnly(2020, 1, 1),
            created_at = now
        };
        db.policy_versions.Add(policy);
        await db.SaveChangesAsync();

        // 3 Storage Units
        var unit1 = new storage_unit
        {
            facility_id = facility.id,
            unit_type_id = unitType.id,
            unit_code = $"U-{testSuffix}-01",
            physical_status = "available",
            is_listed = true,
            floor_label = "Tầng 1",
            zone_label = "A",
            created_at = now,
            updated_at = now
        };
        var unit2 = new storage_unit
        {
            facility_id = facility.id,
            unit_type_id = unitType.id,
            unit_code = $"U-{testSuffix}-02",
            physical_status = "available",
            is_listed = true,
            floor_label = "Tầng 1",
            zone_label = "B",
            created_at = now,
            updated_at = now
        };
        var unit3 = new storage_unit
        {
            facility_id = facility.id,
            unit_type_id = unitType.id,
            unit_code = $"U-{testSuffix}-03",
            physical_status = "available",
            is_listed = true,
            floor_label = "Tầng 2",
            zone_label = "C",
            created_at = now,
            updated_at = now
        };
        db.storage_units.AddRange(unit1, unit2, unit3);
        await db.SaveChangesAsync();

        // Helper to create users
        async Task<(user User, employee_profile? EmpProfile, customer_profile? CustProfile)> CreateUserWithRoleAsync(
            string email, string roleCode, string status = UserStatusConstants.Active, string employmentStatus = "active", string? idNumber = null)
        {
            var targetRole = await db.roles.FirstAsync(r => r.code == roleCode);
            var u = new user
            {
                email = email.Trim().ToLowerInvariant(),
                password_hash = hasher.HashPassword(DefaultPassword),
                status = status,
                phone_number = "0901234567",
                created_at = now,
                updated_at = now
            };
            db.users.Add(u);
            await db.SaveChangesAsync();

            var ur = new user_role
            {
                user_id = u.id,
                role_id = targetRole.id,
                role = targetRole,
                user = u,
                granted_at = now
            };
            db.user_roles.Add(ur);
            u.user_roleusers.Add(ur);

            employee_profile? emp = null;
            customer_profile? cust = null;

            if (roleCode == RoleConstants.StorageCustomer)
            {
                cust = new customer_profile
                {
                    user_id = u.id,
                    user = u,
                    full_name = $"Khách Hàng {u.id}",
                    identity_number = idNumber ?? "079201001234",
                    created_at = now,
                    updated_at = now
                };
                db.customer_profiles.Add(cust);
                u.customer_profile = cust;
            }
            else
            {
                emp = new employee_profile
                {
                    user_id = u.id,
                    user = u,
                    employee_code = $"EMP_{u.id:D4}",
                    full_name = $"Nhân Viên {u.id}",
                    hire_date = today.AddYears(-1),
                    employment_status = employmentStatus,
                    created_at = now,
                    updated_at = now
                };
                db.employee_profiles.Add(emp);
                u.employee_profile = emp;
            }

            await db.SaveChangesAsync();
            return (u, emp, cust);
        }

        // Users
        var (custUser, _, custProfile) = await CreateUserWithRoleAsync($"cust.{testSuffix}@example.com", RoleConstants.StorageCustomer, idNumber: "079201001234");
        var (staffAssigned, staffAssignedEmp, _) = await CreateUserWithRoleAsync($"staff.assigned.{testSuffix}@example.com", RoleConstants.FacilityStaff);
        var (staffUnassigned, _, _) = await CreateUserWithRoleAsync($"staff.unassigned.{testSuffix}@example.com", RoleConstants.FacilityStaff);
        var (staffTerminated, staffTerminatedEmp, _) = await CreateUserWithRoleAsync($"staff.terminated.{testSuffix}@example.com", RoleConstants.FacilityStaff, employmentStatus: "terminated");
        var (managerAssigned, managerAssignedEmp, _) = await CreateUserWithRoleAsync($"manager.assigned.{testSuffix}@example.com", RoleConstants.FacilityManager);
        var (adminUser, _, _) = await CreateUserWithRoleAsync($"admin.{testSuffix}@example.com", RoleConstants.SystemAdministrator);
        var (opsUser, _, _) = await CreateUserWithRoleAsync($"ops.{testSuffix}@example.com", RoleConstants.BusinessOperationsManager);

        // Facility assignments
        var assignStaff = new staff_facility_assignment
        {
            employee_id = staffAssigned.id,
            employee = staffAssignedEmp!,
            facility_id = facility.id,
            facility = facility,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = now.AddMonths(-1),
            ends_at = now.AddMonths(12),
            created_at = now
        };
        db.staff_facility_assignments.Add(assignStaff);
        staffAssignedEmp?.staff_facility_assignments.Add(assignStaff);
        staffAssigned.staff_facility_assignments.Add(assignStaff);

        var assignTerminated = new staff_facility_assignment
        {
            employee_id = staffTerminated.id,
            employee = staffTerminatedEmp!,
            facility_id = facility.id,
            facility = facility,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = now.AddMonths(-1),
            ends_at = now.AddMonths(12),
            created_at = now
        };
        db.staff_facility_assignments.Add(assignTerminated);
        staffTerminatedEmp?.staff_facility_assignments.Add(assignTerminated);
        staffTerminated.staff_facility_assignments.Add(assignTerminated);

        var assignManager = new staff_facility_assignment
        {
            employee_id = managerAssigned.id,
            employee = managerAssignedEmp!,
            facility_id = facility.id,
            facility = facility,
            assignment_role = RoleConstants.FacilityManager,
            starts_at = now.AddMonths(-1),
            ends_at = now.AddMonths(12),
            created_at = now
        };
        db.staff_facility_assignments.Add(assignManager);
        managerAssignedEmp?.staff_facility_assignments.Add(assignManager);
        managerAssigned.staff_facility_assignments.Add(assignManager);

        await db.SaveChangesAsync();

        // Reservation
        var resCode = $"RSV-{testSuffix.ToUpperInvariant()}";
        var reservation = new reservation
        {
            reservation_code = resCode,
            customer_id = custUser.id,
            customer = custProfile,
            facility_id = facility.id,
            facility = facility,
            unit_type_id = unitType.id,
            unit_type = unitType,
            start_date = today,
            end_date = endDate,
            status = "confirmed",
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            created_at = now,
            updated_at = now
        };
        db.reservations.Add(reservation);
        await db.SaveChangesAsync();

        // Rental Agreement
        var agrNo = $"AGR-{testSuffix.ToUpperInvariant()}";
        var agreement = new rental_agreement
        {
            agreement_no = agrNo,
            reservation_id = reservation.id,
            reservation = reservation,
            customer_id = custUser.id,
            customer = custProfile,
            facility_id = facility.id,
            facility = facility,
            policy_version_id = policy.id,
            start_date = today,
            end_date = endDate,
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            deposit_balance = 1000000m,
            status = "scheduled",
            created_at = now,
            updated_at = now
        };
        db.rental_agreements.Add(agreement);
        reservation.rental_agreement = agreement;
        await db.SaveChangesAsync();

        // Paid Invoice
        var invoice = new invoice
        {
            invoice_no = $"INV-{testSuffix.ToUpperInvariant()}",
            reservation_id = reservation.id,
            reservation = reservation,
            agreement_id = agreement.id,
            agreement = agreement,
            customer_id = custUser.id,
            currency = "VND",
            subtotal_amount = 1000000m,
            discount_amount = 0m,
            tax_amount = 0m,
            total_amount = 1000000m,
            paid_amount = 1000000m,
            status = "paid",
            issue_date = today,
            due_date = today.AddDays(3),
            created_at = now
        };
        db.invoices.Add(invoice);
        agreement.invoices.Add(invoice);
        await db.SaveChangesAsync();

        return new CheckInSeedResult
        {
            Facility = facility,
            UnitType = unitType,
            Unit1 = unit1,
            Unit2 = unit2,
            Unit3 = unit3,
            CustomerUser = custUser,
            StaffAssigned = staffAssigned,
            StaffUnassigned = staffUnassigned,
            StaffTerminated = staffTerminated,
            ManagerAssigned = managerAssigned,
            AdminUser = adminUser,
            OpsUser = opsUser,
            Reservation = reservation,
            Agreement = agreement,
            Invoice = invoice,
            ReservationCode = resCode,
            CustomerIdentityNumber = "079201001234"
        };
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var (token, afCookie) = await GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login");
        request.Headers.Add("Cookie", afCookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/"
        });

        var response = await client.SendAsync(request);
        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        var authCookie = setCookies.FirstOrDefault(c => c.Contains("SelfStoragePRN222.Web.Auth"));
        var authCookieVal = authCookie?.Split(';')[0] ?? string.Empty;

        var fullCookie = string.IsNullOrEmpty(afCookie) ? authCookieVal : $"{afCookie}; {authCookieVal}";
        client.DefaultRequestHeaders.Add("Cookie", fullCookie);
        return client;
    }

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

        return (match.Success ? match.Groups[1].Value : string.Empty, cookieHeader);
    }
}

public class CheckInSeedResult
{
    public facility Facility { get; set; } = null!;
    public unit_type UnitType { get; set; } = null!;
    public storage_unit Unit1 { get; set; } = null!;
    public storage_unit Unit2 { get; set; } = null!;
    public storage_unit Unit3 { get; set; } = null!;
    public user CustomerUser { get; set; } = null!;
    public user StaffAssigned { get; set; } = null!;
    public user StaffUnassigned { get; set; } = null!;
    public user StaffTerminated { get; set; } = null!;
    public user ManagerAssigned { get; set; } = null!;
    public user AdminUser { get; set; } = null!;
    public user OpsUser { get; set; } = null!;
    public reservation Reservation { get; set; } = null!;
    public rental_agreement Agreement { get; set; } = null!;
    public invoice Invoice { get; set; } = null!;
    public string ReservationCode { get; set; } = string.Empty;
    public string CustomerIdentityNumber { get; set; } = string.Empty;
}
