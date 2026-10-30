using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.WebTests.Fixtures;

/// <summary>
/// WebApplicationFactory fixture for T12 Payments, Invoices, Agreements, and Gateways (VNPAY / Demo).
/// Configured with Port 7041 endpoints, in-memory DbContext, VNPAY signing secret, and comprehensive seed data.
/// </summary>
public class PaymentWebTestFixture : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString("N");
    public const string TestVnpayHashSecret = "VNPAYTESTSECRETKEY12345678901234567890";
    public const string TestVnpayTmnCode = "TESTTMN1";
    public const string DefaultPassword = "Password123!";

    public string EnvironmentName { get; set; } = "Testing";
    public bool AllowDemoSimulatorConfig { get; set; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = EnvironmentName,
                ["Payment:AllowDemoSimulator"] = AllowDemoSimulatorConfig ? "true" : "false",
                ["Payment:DemoBaseUrl"] = "https://localhost:7041",
                ["Vnpay:TmnCode"] = TestVnpayTmnCode,
                ["Vnpay:HashSecret"] = TestVnpayHashSecret,
                ["Vnpay:ReturnUrl"] = "https://localhost:7041/api/payments/vnpay/return"
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
                options.UseInMemoryDatabase($"SelfStorage_PaymentTests_{_dbName}");
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

    public async Task<PaymentSeedResult> SeedPaymentEnvironmentAsync(string testSuffix)
    {
        await SeedRolesAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var endDate = today.AddMonths(1);

        // 1. Facility
        var facility = new facility
        {
            code = $"FAC-{testSuffix.ToUpperInvariant()}",
            name = $"Cơ sở Test Thanh Toán {testSuffix}",
            address_line = "123 Đường Test, Quận 1",
            city = "TP. Hồ Chí Minh",
            timezone = "Asia/Ho_Chi_Minh",
            status = "active",
            created_at = now,
            updated_at = now
        };
        db.facilities.Add(facility);
        await db.SaveChangesAsync();

        // 2. Policy Version (rental_terms)
        var policy = new policy_version
        {
            policy_type = "rental_terms",
            version = $"v{testSuffix}",
            content = "Điều khoản hợp đồng thuê kho tự quản chuẩn PRN222.",
            valid_from = today.AddMonths(-1),
            valid_to = today.AddMonths(12),
            created_at = now
        };
        db.policy_versions.Add(policy);

        // 3. Unit Type & Rate
        var unitType = new unit_type
        {
            code = $"UT-{testSuffix.ToUpperInvariant()}",
            name = $"Loại Kho Test {testSuffix}",
            area_m2 = 4.0m,
            climate_controlled = false,
            is_active = true,
            created_at = now,
            updated_at = now
        };
        db.unit_types.Add(unitType);
        await db.SaveChangesAsync();

        var rate = new facility_rate
        {
            facility_id = facility.id,
            facility = facility,
            unit_type_id = unitType.id,
            unit_type = unitType,
            monthly_rate = 1000000m,
            deposit_amount = 1000000m,
            booking_fee = 0m,
            valid_from = today.AddMonths(-1),
            created_at = now
        };
        db.facility_rates.Add(rate);

        // 4. Storage Unit
        var unit = new storage_unit
        {
            unit_code = $"U-{testSuffix.ToUpperInvariant()}-01",
            facility_id = facility.id,
            facility = facility,
            unit_type_id = unitType.id,
            unit_type = unitType,
            physical_status = "available",
            is_listed = true,
            floor_label = "Tầng 1",
            zone_label = "A",
            created_at = now,
            updated_at = now
        };
        db.storage_units.Add(unit);

        // 5. Promotion Voucher (200,000 VND discount)
        var promoCode = $"PROMO_{testSuffix.ToUpperInvariant()}";
        var promo = new promotion
        {
            code = promoCode,
            name = $"Khuyến mãi test {testSuffix}",
            discount_type = "fixed_amount",
            discount_value = 200000m,
            max_discount_amount = 200000m,
            valid_from = now.AddDays(-10),
            valid_to = now.AddDays(30),
            is_active = true,
            created_at = now
        };
        db.promotions.Add(promo);

        // Helper to create user
        async Task<(user User, employee_profile? Emp, customer_profile? Cust)> CreateUserWithRoleAsync(
            string email, string roleCode, string? employeeCode = null)
        {
            var roleEntity = await db.roles.FirstAsync(r => r.code == roleCode);

            var u = new user
            {
                email = email.Trim().ToLowerInvariant(),
                password_hash = hasher.HashPassword(DefaultPassword),
                status = "active",
                phone_number = "0901234567",
                created_at = now,
                updated_at = now
            };
            db.users.Add(u);
            await db.SaveChangesAsync();

            var ur = new user_role
            {
                user_id = u.id,
                user = u,
                role_id = roleEntity.id,
                role = roleEntity,
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
                    full_name = $"Khách hàng {email.Split('@')[0]}",
                    identity_number = "079201009999",
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
                    employee_code = employeeCode ?? $"EMP-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
                    full_name = $"Nhân viên {email.Split('@')[0]}",
                    hire_date = today.AddYears(-1),
                    employment_status = "active",
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
        var (custUser, _, custProfile) = await CreateUserWithRoleAsync($"cust.{testSuffix}@example.com", RoleConstants.StorageCustomer);
        var (custOther, _, _) = await CreateUserWithRoleAsync($"cust.other.{testSuffix}@example.com", RoleConstants.StorageCustomer);
        var (staffUser, staffEmp, _) = await CreateUserWithRoleAsync($"staff.{testSuffix}@example.com", RoleConstants.FacilityStaff, $"STF-{testSuffix.ToUpperInvariant()}");
        var (managerUser, mgrEmp, _) = await CreateUserWithRoleAsync($"mgr.{testSuffix}@example.com", RoleConstants.FacilityManager, $"MGR-{testSuffix.ToUpperInvariant()}");
        var (adminUser, _, _) = await CreateUserWithRoleAsync($"admin.{testSuffix}@example.com", RoleConstants.SystemAdministrator, $"ADM-{testSuffix.ToUpperInvariant()}");

        // Staff assignments
        var staffAssign = new staff_facility_assignment
        {
            employee_id = staffUser.id,
            employee = staffEmp!,
            facility_id = facility.id,
            facility = facility,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = now.AddMonths(-1),
            ends_at = now.AddMonths(12),
            created_at = now
        };
        db.staff_facility_assignments.Add(staffAssign);

        var mgrAssign = new staff_facility_assignment
        {
            employee_id = managerUser.id,
            employee = mgrEmp!,
            facility_id = facility.id,
            facility = facility,
            assignment_role = RoleConstants.FacilityManager,
            starts_at = now.AddMonths(-1),
            ends_at = now.AddMonths(12),
            created_at = now
        };
        db.staff_facility_assignments.Add(mgrAssign);
        await db.SaveChangesAsync();

        // 6. Active Reservation (Held, 15m)
        var reservation = new reservation
        {
            reservation_code = $"RSV-{testSuffix.ToUpperInvariant()}",
            customer_id = custUser.id,
            customer = custProfile,
            facility_id = facility.id,
            facility = facility,
            unit_type_id = unitType.id,
            unit_type = unitType,
            facility_rate_id = rate.id,
            facility_rate = rate,
            start_date = today,
            end_date = endDate,
            status = "awaiting_deposit",
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            booking_fee_snapshot = 0m,
            quoted_total = 2000000m,
            hold_until = now.AddMinutes(15),
            created_at = now,
            updated_at = now
        };
        db.reservations.Add(reservation);

        // 7. Expired Reservation
        var expiredReservation = new reservation
        {
            reservation_code = $"RSV-EXP-{testSuffix.ToUpperInvariant()}",
            customer_id = custUser.id,
            customer = custProfile,
            facility_id = facility.id,
            facility = facility,
            unit_type_id = unitType.id,
            unit_type = unitType,
            facility_rate_id = rate.id,
            facility_rate = rate,
            start_date = today,
            end_date = endDate,
            status = "cancelled",
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            booking_fee_snapshot = 0m,
            quoted_total = 2000000m,
            hold_until = now.AddMinutes(-10),
            created_at = now.AddMinutes(-30),
            updated_at = now.AddMinutes(-10)
        };
        db.reservations.Add(expiredReservation);

        // 8. Other customer's reservation
        var otherReservation = new reservation
        {
            reservation_code = $"RSV-OTH-{testSuffix.ToUpperInvariant()}",
            customer_id = custOther.id,
            facility_id = facility.id,
            facility = facility,
            unit_type_id = unitType.id,
            unit_type = unitType,
            facility_rate_id = rate.id,
            facility_rate = rate,
            start_date = today,
            end_date = endDate,
            status = "awaiting_deposit",
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            booking_fee_snapshot = 0m,
            quoted_total = 2000000m,
            hold_until = now.AddMinutes(15),
            created_at = now,
            updated_at = now
        };
        db.reservations.Add(otherReservation);

        await db.SaveChangesAsync();

        return new PaymentSeedResult
        {
            Facility = facility,
            UnitType = unitType,
            Unit = unit,
            PolicyVersion = policy,
            Promotion = promo,
            CustomerUser = custUser,
            OtherCustomerUser = custOther,
            StaffUser = staffUser,
            ManagerUser = managerUser,
            AdminUser = adminUser,
            ActiveReservation = reservation,
            ExpiredReservation = expiredReservation,
            OtherCustomerReservation = otherReservation
        };
    }

    public static Dictionary<string, string> GenerateVnpayIpnParameters(
        long paymentId,
        decimal amount,
        string responseCode = "00",
        string hashSecret = TestVnpayHashSecret,
        string tmnCode = TestVnpayTmnCode,
        string currency = "VND",
        bool tamperSignature = false,
        string? transactionNo = null)
    {
        var rawAmount = (long)(amount * 100);
        var gmt7Now = DateTime.UtcNow.AddHours(7).ToString("yyyyMMddHHmmss");

        var p = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = rawAmount.ToString(),
            ["vnp_BankCode"] = "NCB",
            ["vnp_BankTranNo"] = $"VNP{transactionNo ?? "14073849"}",
            ["vnp_CardType"] = "ATM",
            ["vnp_Command"] = "pay",
            ["vnp_CurrCode"] = currency,
            ["vnp_OrderInfo"] = $"Thanh toan don {paymentId}",
            ["vnp_PayDate"] = gmt7Now,
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_TmnCode"] = tmnCode,
            ["vnp_TransactionNo"] = transactionNo ?? "14073849",
            ["vnp_TransactionStatus"] = responseCode == "00" ? "00" : "02",
            ["vnp_TxnRef"] = paymentId.ToString(),
            ["vnp_Version"] = "2.1.0"
        };

        var hashBuilder = new StringBuilder();
        foreach (var (k, v) in p)
        {
            if (hashBuilder.Length > 0) hashBuilder.Append('&');
            hashBuilder.Append(WebUtility.UrlEncode(k)).Append('=').Append(WebUtility.UrlEncode(v));
        }

        var signature = VnpayGateway.ComputeHmacSha512(hashSecret, hashBuilder.ToString());
        if (tamperSignature)
        {
            signature = signature.Replace('A', 'B').Replace('0', '1');
        }

        var result = new Dictionary<string, string>(p)
        {
            ["vnp_SecureHash"] = signature,
            ["vnp_SecureHashType"] = "HmacSHA512"
        };

        return result;
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

public class PaymentSeedResult
{
    public facility Facility { get; set; } = null!;
    public unit_type UnitType { get; set; } = null!;
    public storage_unit Unit { get; set; } = null!;
    public policy_version PolicyVersion { get; set; } = null!;
    public promotion Promotion { get; set; } = null!;
    public user CustomerUser { get; set; } = null!;
    public user OtherCustomerUser { get; set; } = null!;
    public user StaffUser { get; set; } = null!;
    public user ManagerUser { get; set; } = null!;
    public user AdminUser { get; set; } = null!;
    public reservation ActiveReservation { get; set; } = null!;
    public reservation ExpiredReservation { get; set; } = null!;
    public reservation OtherCustomerReservation { get; set; } = null!;
}
