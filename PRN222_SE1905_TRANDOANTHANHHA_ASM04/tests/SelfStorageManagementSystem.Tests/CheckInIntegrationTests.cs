using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class CheckInIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public CheckInIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private string GenerateToken(user user, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var tokenGen = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var (token, _) = tokenGen.GenerateToken(user, new[] { role });
        return token;
    }

    private async Task<(user Staff, user Customer, user OtherCustomer, reservation Reservation, rental_agreement Agreement, storage_unit Unit)> SeedDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var testSuffix = Guid.NewGuid().ToString("N")[..6];

        // Roles
        if (!await db.roles.AnyAsync(r => r.code == RoleConstants.StorageCustomer))
            db.roles.Add(new role { id = 1001, code = RoleConstants.StorageCustomer, display_name = "Customer" });
        if (!await db.roles.AnyAsync(r => r.code == RoleConstants.FacilityStaff))
            db.roles.Add(new role { id = 1002, code = RoleConstants.FacilityStaff, display_name = "Facility Staff" });

        // Facility
        var facility = new facility
        {
            code = $"FAC-{testSuffix}",
            name = $"Co So Test {testSuffix}",
            address_line = "123 Vo Van Ngan",
            city = "Ho Chi Minh City",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        };
        db.facilities.Add(facility);

        // Unit Type
        var unitType = new unit_type
        {
            code = $"UT-{testSuffix}",
            name = "Kho Standard",
            area_m2 = 4.0m,
            climate_controlled = false,
            is_active = true
        };
        db.unit_types.Add(unitType);

        // Policy
        var policy = new policy_version
        {
            policy_type = "rental_terms",
            version = $"v{testSuffix}",
            content = "{}",
            valid_from = new DateOnly(2020, 1, 1),
            created_at = DateTimeOffset.UtcNow
        };
        db.policy_versions.Add(policy);
        await db.SaveChangesAsync();

        // Storage Unit
        var unit = new storage_unit
        {
            facility_id = facility.id,
            unit_type_id = unitType.id,
            unit_code = $"U-{testSuffix}",
            physical_status = "available",
            is_listed = true,
            floor_label = "Tang 1"
        };
        db.storage_units.Add(unit);

        // Customer User
        var custUser = new user
        {
            email = $"cust_{testSuffix}@test.com",
            password_hash = hasher.HashPassword("TestPass123!"),
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(custUser);
        await db.SaveChangesAsync();

        db.customer_profiles.Add(new customer_profile
        {
            user_id = custUser.id,
            full_name = $"Khach Hang {testSuffix}",
            identity_number = "079201009999",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });
        db.user_roles.Add(new user_role { user_id = custUser.id, role_id = 1001, granted_at = DateTimeOffset.UtcNow });

        // Other Customer
        var otherCust = new user
        {
            email = $"other_{testSuffix}@test.com",
            password_hash = hasher.HashPassword("TestPass123!"),
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(otherCust);
        await db.SaveChangesAsync();

        db.customer_profiles.Add(new customer_profile
        {
            user_id = otherCust.id,
            full_name = $"Khach Khac {testSuffix}",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });
        db.user_roles.Add(new user_role { user_id = otherCust.id, role_id = 1001, granted_at = DateTimeOffset.UtcNow });

        // Staff User
        var staffUser = new user
        {
            email = $"staff_{testSuffix}@test.com",
            password_hash = hasher.HashPassword("TestPass123!"),
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.users.Add(staffUser);
        await db.SaveChangesAsync();

        db.employee_profiles.Add(new employee_profile
        {
            user_id = staffUser.id,
            employee_code = $"EMP-{testSuffix}",
            full_name = $"Nhan Vien {testSuffix}",
            employment_status = "active",
            hire_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });
        db.user_roles.Add(new user_role { user_id = staffUser.id, role_id = 1002, granted_at = DateTimeOffset.UtcNow });

        db.staff_facility_assignments.Add(new staff_facility_assignment
        {
            employee_id = staffUser.id,
            facility_id = facility.id,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = DateTimeOffset.UtcNow.AddMonths(-1),
            created_at = DateTimeOffset.UtcNow
        });

        // Reservation & Agreement & Invoice
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rsv = new reservation
        {
            reservation_code = $"RSV-{testSuffix}",
            customer_id = custUser.id,
            facility_id = facility.id,
            unit_type_id = unitType.id,
            start_date = today,
            end_date = today.AddMonths(1),
            status = "confirmed",
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            created_at = DateTimeOffset.UtcNow.AddDays(-1),
            updated_at = DateTimeOffset.UtcNow.AddDays(-1)
        };
        db.reservations.Add(rsv);
        await db.SaveChangesAsync();

        var agr = new rental_agreement
        {
            agreement_no = $"AGR-{testSuffix}",
            reservation_id = rsv.id,
            customer_id = custUser.id,
            facility_id = facility.id,
            policy_version_id = policy.id,
            start_date = today,
            end_date = today.AddMonths(1),
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            deposit_balance = 1000000m,
            status = "scheduled",
            created_at = DateTimeOffset.UtcNow.AddDays(-1),
            updated_at = DateTimeOffset.UtcNow.AddDays(-1)
        };
        db.rental_agreements.Add(agr);
        await db.SaveChangesAsync();

        var inv = new invoice
        {
            invoice_no = $"INV-{testSuffix}",
            customer_id = custUser.id,
            reservation_id = rsv.id,
            agreement_id = agr.id,
            issue_date = today,
            due_date = today.AddDays(7),
            currency = "VND",
            subtotal_amount = 2000000m,
            total_amount = 2000000m,
            paid_amount = 2000000m,
            status = "paid",
            created_at = DateTimeOffset.UtcNow.AddDays(-1)
        };
        db.invoices.Add(inv);
        await db.SaveChangesAsync();

        return (staffUser, custUser, otherCust, rsv, agr, unit);
    }

    [Fact]
    public async Task SearchCheckIn_Anonymous_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/staff/check-in/search?code=RSV-123");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SearchCheckIn_CustomerRole_ReturnsForbidden()
    {
        var (_, customer, _, rsv, _, _) = await SeedDataAsync();
        var token = GenerateToken(customer, RoleConstants.StorageCustomer);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/staff/check-in/search?code={rsv.reservation_code}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FullCheckInFlow_EndToEnd_SucceedsWithPinAndNoStoreHeader()
    {
        var (staff, customer, otherCust, rsv, agr, unit) = await SeedDataAsync();
        var staffToken = GenerateToken(staff, RoleConstants.FacilityStaff);
        var custToken = GenerateToken(customer, RoleConstants.StorageCustomer);
        var otherCustToken = GenerateToken(otherCust, RoleConstants.StorageCustomer);

        // 1. Staff Search Check-In
        using (var searchReq = new HttpRequestMessage(HttpMethod.Get, $"/api/staff/check-in/search?code={rsv.reservation_code}"))
        {
            searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
            var searchRes = await _client.SendAsync(searchReq);
            Assert.Equal(HttpStatusCode.OK, searchRes.StatusCode);

            var apiRes = await searchRes.Content.ReadFromJsonAsync<ApiResponse<CheckInSearchResponse>>(JsonOpts);
            Assert.NotNull(apiRes);
            Assert.NotNull(apiRes.Data);
            Assert.False(apiRes.Data.CanProceedToCheckIn);
            Assert.NotEmpty(apiRes.Data.ReasonsCannotProceed);
        }

        // 2. Staff Get Eligible Units
        using (var unitsReq = new HttpRequestMessage(HttpMethod.Get, $"/api/staff/check-in/{rsv.id}/eligible-units"))
        {
            unitsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
            var unitsRes = await _client.SendAsync(unitsReq);
            Assert.Equal(HttpStatusCode.OK, unitsRes.StatusCode);

            var apiRes = await unitsRes.Content.ReadFromJsonAsync<ApiResponse<List<EligibleUnitResponse>>>(JsonOpts);
            Assert.NotNull(apiRes);
            Assert.Contains(apiRes.Data!, u => u.UnitId == unit.id);
        }

        // 3. Staff Verify Identity
        using (var verifReq = new HttpRequestMessage(HttpMethod.Post, $"/api/staff/check-in/{rsv.id}/verify-id"))
        {
            verifReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
            verifReq.Content = JsonContent.Create(new VerifyIdentityRequest
            {
                VerificationMethod = "government_id",
                DocumentNumber = "079201009999",
                IsConfirmedMatch = true,
                Notes = "Doi chieu ban goc CMND/CCCD hop le"
            });

            var verifRes = await _client.SendAsync(verifReq);
            Assert.Equal(HttpStatusCode.OK, verifRes.StatusCode);

            var apiRes = await verifRes.Content.ReadFromJsonAsync<ApiResponse<IdentityVerificationResponse>>(JsonOpts);
            Assert.NotNull(apiRes);
            Assert.Equal("verified", apiRes.Data!.Result);
        }

        // 4. Staff Assign Unit
        long allocationId;
        using (var assignReq = new HttpRequestMessage(HttpMethod.Post, $"/api/staff/check-in/{rsv.id}/assign-unit"))
        {
            assignReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
            assignReq.Content = JsonContent.Create(new AssignUnitRequest
            {
                StorageUnitId = unit.id
            });

            var assignRes = await _client.SendAsync(assignReq);
            Assert.Equal(HttpStatusCode.OK, assignRes.StatusCode);

            var apiRes = await assignRes.Content.ReadFromJsonAsync<ApiResponse<UnitAllocationResponse>>(JsonOpts);
            Assert.NotNull(apiRes);
            Assert.Equal("active", apiRes.Data!.Status);
            allocationId = apiRes.Data.AllocationId;
        }

        // 5. Staff Perform Inspection
        long inspectionId;
        using (var inspReq = new HttpRequestMessage(HttpMethod.Post, $"/api/staff/check-in/{rsv.id}/inspection"))
        {
            inspReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
            inspReq.Content = JsonContent.Create(new UnitInspectionRequest
            {
                CurrentAllocationId = allocationId,
                Items = new List<InspectionItemRequest>
                {
                    new() { ItemName = "cửa", Condition = "good" },
                    new() { ItemName = "khóa", Condition = "good" },
                    new() { ItemName = "đèn", Condition = "good" },
                    new() { ItemName = "vệ sinh", Condition = "good" }
                },
                OverallSummary = "Kho sach se, cua va khoa hoat dong tot"
            });

            var inspRes = await _client.SendAsync(inspReq);
            Assert.Equal(HttpStatusCode.OK, inspRes.StatusCode);

            var apiRes = await inspRes.Content.ReadFromJsonAsync<ApiResponse<InspectionResponse>>(JsonOpts);
            Assert.NotNull(apiRes);
            Assert.Equal("completed", apiRes.Data!.Status);
            inspectionId = apiRes.Data.InspectionId;
        }

        // 6. Staff Complete Check-In
        long credentialId;
        using (var compReq = new HttpRequestMessage(HttpMethod.Post, $"/api/staff/check-in/{rsv.id}/complete"))
        {
            compReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
            compReq.Content = JsonContent.Create(new CompleteCheckInRequest
            {
                CurrentAllocationId = allocationId,
                CurrentInspectionId = inspectionId,
                CustomerAcknowledged = true,
                Notes = "Khach hang dong y nhan ban giao tai quay"
            });

            var compRes = await _client.SendAsync(compReq);
            Assert.Equal(HttpStatusCode.OK, compRes.StatusCode);

            // Verify Cache-Control: no-store header
            Assert.True(compRes.Headers.CacheControl?.NoStore);

            var apiRes = await compRes.Content.ReadFromJsonAsync<ApiResponse<HandoverCompleteResponse>>(JsonOpts);
            Assert.NotNull(apiRes);
            Assert.NotNull(apiRes.Data!.Pin);
            Assert.Equal(6, apiRes.Data.Pin.Length);
            Assert.NotNull(apiRes.Data.CredentialId);
            credentialId = apiRes.Data.CredentialId.Value;
        }

        // 7. Staff Reissue PIN
        using (var reissueReq = new HttpRequestMessage(HttpMethod.Post, $"/api/staff/check-in/{rsv.id}/reissue-pin"))
        {
            reissueReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
            reissueReq.Content = JsonContent.Create(new ReissuePinRequest
            {
                CurrentCredentialId = credentialId,
                DocumentNumber = "079201009999",
                IsConfirmedMatch = true,
                Reason = "Khach quen ma PIN"
            });

            var reissueRes = await _client.SendAsync(reissueReq);
            Assert.Equal(HttpStatusCode.OK, reissueRes.StatusCode);

            // Verify Cache-Control: no-store header
            Assert.True(reissueRes.Headers.CacheControl?.NoStore);

            var apiRes = await reissueRes.Content.ReadFromJsonAsync<ApiResponse<PinReissueResponse>>(JsonOpts);
            Assert.NotNull(apiRes);
            Assert.NotNull(apiRes.Data!.Pin);
            Assert.Equal(6, apiRes.Data.Pin.Length);
        }

        // 8. Customer Access /api/me/units
        using (var custUnitsReq = new HttpRequestMessage(HttpMethod.Get, "/api/me/units"))
        {
            custUnitsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", custToken);
            var custUnitsRes = await _client.SendAsync(custUnitsReq);
            Assert.Equal(HttpStatusCode.OK, custUnitsRes.StatusCode);

            var apiRes = await custUnitsRes.Content.ReadFromJsonAsync<ApiResponse<PagedResult<CustomerUnitListItemResponse>>>(JsonOpts);
            Assert.NotNull(apiRes);
            Assert.Equal(1, apiRes.Data!.TotalCount);
            Assert.Equal(unit.unit_code, apiRes.Data.Items.First().UnitCode);
        }

        // 9. Customer Access /api/me/units/{agreementId}/handover
        using (var custHandoverReq = new HttpRequestMessage(HttpMethod.Get, $"/api/me/units/{agr.id}/handover"))
        {
            custHandoverReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", custToken);
            var custHandoverRes = await _client.SendAsync(custHandoverReq);
            Assert.Equal(HttpStatusCode.OK, custHandoverRes.StatusCode);

            var apiRes = await custHandoverRes.Content.ReadFromJsonAsync<ApiResponse<CustomerHandoverResponse>>(JsonOpts);
            Assert.NotNull(apiRes);
            Assert.Equal(agr.agreement_no, apiRes.Data!.AgreementNo);
            Assert.Equal(unit.unit_code, apiRes.Data.StorageUnitCode);
        }

        // 10. Other Customer Accessing Same Handover -> 404 (NotFoundException)
        using (var otherHandoverReq = new HttpRequestMessage(HttpMethod.Get, $"/api/me/units/{agr.id}/handover"))
        {
            otherHandoverReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherCustToken);
            var otherHandoverRes = await _client.SendAsync(otherHandoverReq);
            Assert.Equal(HttpStatusCode.NotFound, otherHandoverRes.StatusCode);
        }

        // 11. Customer Notifications
        using (var notifReq = new HttpRequestMessage(HttpMethod.Get, "/api/me/notifications"))
        {
            notifReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", custToken);
            var notifRes = await _client.SendAsync(notifReq);
            Assert.Equal(HttpStatusCode.OK, notifRes.StatusCode);

            var apiRes = await notifRes.Content.ReadFromJsonAsync<ApiResponse<PagedResult<CustomerNotificationResponse>>>(JsonOpts);
            Assert.NotNull(apiRes);
            Assert.NotEmpty(apiRes.Data!.Items);
            Assert.All(apiRes.Data.Items, n => Assert.Equal("in_app", n.Channel));
        }
    }
}
