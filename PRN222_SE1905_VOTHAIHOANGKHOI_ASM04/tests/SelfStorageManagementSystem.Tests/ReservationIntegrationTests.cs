using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class ReservationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ReservationIntegrationTests(CustomWebApplicationFactory factory)
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

    private async Task<(user User10, user User20)> SeedDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // Roles
        if (!await db.roles.AnyAsync(r => r.code == RoleConstants.StorageCustomer))
        {
            db.roles.Add(new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" });
        }

        // Customer 1
        var u10 = await db.users.FirstOrDefaultAsync(u => u.id == 10);
        if (u10 == null)
        {
            u10 = new user
            {
                id = 10,
                email = "cust10@test.com",
                password_hash = hasher.HashPassword("TestPass123!"),
                status = UserStatusConstants.Active,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.users.Add(u10);
            db.user_roles.Add(new user_role { user_id = 10, role_id = 1, granted_at = DateTimeOffset.UtcNow });
            db.customer_profiles.Add(new customer_profile
            {
                user_id = 10,
                full_name = "Customer Ten",
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
        }

        // Customer 2
        var u20 = await db.users.FirstOrDefaultAsync(u => u.id == 20);
        if (u20 == null)
        {
            u20 = new user
            {
                id = 20,
                email = "cust20@test.com",
                password_hash = hasher.HashPassword("TestPass123!"),
                status = UserStatusConstants.Active,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.users.Add(u20);
            db.user_roles.Add(new user_role { user_id = 20, role_id = 1, granted_at = DateTimeOffset.UtcNow });
            db.customer_profiles.Add(new customer_profile
            {
                user_id = 20,
                full_name = "Customer Twenty",
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
        }

        // Facility 801
        if (!await db.facilities.AnyAsync(f => f.id == 801))
        {
            var fac = new facility
            {
                id = 801,
                code = "FAC-801",
                name = "Cơ sở Test 801",
                address_line = "123 Đường Test",
                district = "Quận 1",
                city = "TP HCM",
                status = "active",
                timezone = "Asia/Ho_Chi_Minh",
                opening_time = new TimeOnly(7, 0),
                closing_time = new TimeOnly(22, 0)
            };
            db.facilities.Add(fac);

            // Facility Area
            var area = new facility_area
            {
                id = 801,
                facility_id = 801,
                code = "AREA-801",
                name = "Khu Vực 1",
                area_type = "indoor",
                display_order = 1,
                map_metadata = "{}",
                is_active = true
            };
            db.facility_areas.Add(area);

            // Unit Type 801
            var ut = new unit_type
            {
                id = 801,
                code = "UT-801",
                name = "Kho Test 801",
                width_m = 2.0m,
                length_m = 2.0m,
                height_m = 2.5m,
                area_m2 = 4.0m,
                climate_controlled = false,
                is_active = true
            };
            db.unit_types.Add(ut);

            // Facility Rate 801
            var rate = new facility_rate
            {
                id = 801,
                facility_id = 801,
                unit_type_id = 801,
                monthly_rate = 1_000_000m,
                deposit_amount = 1_000_000m,
                booking_fee = 50_000m,
                valid_from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                created_at = DateTimeOffset.UtcNow
            };
            db.facility_rates.Add(rate);

            // Storage Units (10 available units to provide sufficient capacity across concurrent tests)
            for (int i = 1; i <= 10; i++)
            {
                var unit = new storage_unit
                {
                    id = 800 + i,
                    facility_id = 801,
                    area_id = 801,
                    unit_type_id = 801,
                    unit_code = $"U-801-{i}",
                    floor_label = "1",
                    physical_status = "available",
                    is_listed = true,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                db.storage_units.Add(unit);
            }
        }

        await db.SaveChangesAsync();
        return (u10, u20);
    }

    [Fact]
    public async Task CreateReservation_WithoutToken_ReturnsUnauthorized()
    {
        var request = new CreateReservationRequest
        {
            FacilityId = 801,
            UnitTypeId = 801,
            RentalStartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            RentalEndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(31))
        };

        var response = await _client.PostAsJsonAsync("/api/reservations", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateReservation_WithValidRequest_ReturnsCreatedWithHoldDetails()
    {
        var (u10, _) = await SeedDataAsync();
        var token = GenerateToken(u10, RoleConstants.StorageCustomer);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var endDate = startDate.AddMonths(1);

        var request = new CreateReservationRequest
        {
            FacilityId = 801,
            UnitTypeId = 801,
            RentalStartDate = startDate,
            RentalEndDate = endDate
        };

        var response = await _client.PostAsJsonAsync("/api/reservations", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<ApiResponse<ReservationDetailResponse>>(JsonOpts);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.NotNull(content.Data);

        var r = content.Data;
        Assert.Equal("pending", r.Status);
        Assert.NotNull(r.ReservationCode);
        Assert.Equal(1_000_000m, r.MonthlyRateSnapshot);
        Assert.Equal(1_000_000m, r.DepositSnapshot);
        Assert.Equal(50_000m, r.BookingFeeSnapshot);
        Assert.Equal(2_050_000m, r.QuotedTotal); // 1,000,000 (deposit) + 1,000,000 (first month) + 50,000 (booking fee)
        Assert.True(r.HoldUntil > DateTimeOffset.UtcNow);
        Assert.True(r.RemainingSeconds > 0);
        Assert.True(r.RemainingSeconds <= 900);
    }

    [Fact]
    public async Task GetReservationById_ForbiddenForOtherCustomer()
    {
        var (u10, u20) = await SeedDataAsync();
        var cust1Token = GenerateToken(u10, RoleConstants.StorageCustomer);
        var cust2Token = GenerateToken(u20, RoleConstants.StorageCustomer);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cust1Token);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var request = new CreateReservationRequest
        {
            FacilityId = 801,
            UnitTypeId = 801,
            RentalStartDate = startDate,
            RentalEndDate = startDate.AddMonths(2)
        };
        var createResp = await _client.PostAsJsonAsync("/api/reservations", request);
        var createResult = await createResp.Content.ReadFromJsonAsync<ApiResponse<ReservationDetailResponse>>(JsonOpts);
        var resId = createResult!.Data!.Id;

        // Customer 2 tries to access Customer 1's reservation
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cust2Token);
        var getResp = await _client.GetAsync($"/api/reservations/{resId}");
        Assert.Equal(HttpStatusCode.Forbidden, getResp.StatusCode);

        // Customer 1 can access it
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cust1Token);
        var getSuccess = await _client.GetAsync($"/api/reservations/{resId}");
        Assert.Equal(HttpStatusCode.OK, getSuccess.StatusCode);
    }

    [Fact]
    public async Task CancelReservation_CancelsSuccessfullyAndIsIdempotent()
    {
        var (u10, _) = await SeedDataAsync();
        var token = GenerateToken(u10, RoleConstants.StorageCustomer);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var createResp = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            FacilityId = 801,
            UnitTypeId = 801,
            RentalStartDate = startDate,
            RentalEndDate = startDate.AddMonths(1)
        });
        var createResult = await createResp.Content.ReadFromJsonAsync<ApiResponse<ReservationDetailResponse>>(JsonOpts);
        var resId = createResult!.Data!.Id;

        // First cancellation
        var cancelResp = await _client.PostAsJsonAsync($"/api/reservations/{resId}/cancel", new CancelReservationRequest
        {
            Reason = "No longer needed"
        });
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
        var cancelResult = await cancelResp.Content.ReadFromJsonAsync<ApiResponse<ReservationDetailResponse>>(JsonOpts);
        Assert.Equal("cancelled", cancelResult!.Data!.Status);

        // Second cancellation (idempotent)
        var cancelResp2 = await _client.PostAsJsonAsync($"/api/reservations/{resId}/cancel", new CancelReservationRequest
        {
            Reason = "Repeat cancel call"
        });
        Assert.Equal(HttpStatusCode.OK, cancelResp2.StatusCode);
        var cancelResult2 = await cancelResp2.Content.ReadFromJsonAsync<ApiResponse<ReservationDetailResponse>>(JsonOpts);
        Assert.Equal("cancelled", cancelResult2!.Data!.Status);
    }

    [Fact]
    public async Task CreateReservation_WithNonCustomerRole_ReturnsForbidden()
    {
        await SeedDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();

        // Ensure user 30 has staff role only
        var u30 = await db.users.FirstOrDefaultAsync(u => u.id == 30);
        if (u30 == null)
        {
            var staffRole = await db.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.FacilityStaff);
            if (staffRole == null)
            {
                staffRole = new role { id = 2, code = RoleConstants.FacilityStaff, display_name = "Staff" };
                db.roles.Add(staffRole);
            }

            u30 = new user
            {
                id = 30,
                email = "staff30@test.com",
                password_hash = "hash",
                status = UserStatusConstants.Active,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.users.Add(u30);
            db.user_roles.Add(new user_role { user_id = 30, role_id = staffRole.id, granted_at = DateTimeOffset.UtcNow });
            db.customer_profiles.Add(new customer_profile
            {
                user_id = 30,
                full_name = "Staff with profile",
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var staffToken = GenerateToken(u30, RoleConstants.FacilityStaff);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var request = new CreateReservationRequest
        {
            FacilityId = 801,
            UnitTypeId = 801,
            RentalStartDate = startDate,
            RentalEndDate = startDate.AddMonths(1)
        };

        var response = await _client.PostAsJsonAsync("/api/reservations", request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CancelReservation_WhenConfirmed_ReturnsConflict()
    {
        var (u10, _) = await SeedDataAsync();
        var token = GenerateToken(u10, RoleConstants.StorageCustomer);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15));
        var createResp = await _client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            FacilityId = 801,
            UnitTypeId = 801,
            RentalStartDate = startDate,
            RentalEndDate = startDate.AddMonths(1)
        });
        var createResult = await createResp.Content.ReadFromJsonAsync<ApiResponse<ReservationDetailResponse>>(JsonOpts);
        var resId = createResult!.Data!.Id;

        // Simulate payment confirmed in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var res = await db.reservations.FirstAsync(r => r.id == resId);
            res.status = "confirmed";
            res.confirmed_at = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var cancelResp = await _client.PostAsJsonAsync($"/api/reservations/{resId}/cancel", new CancelReservationRequest
        {
            Reason = "Attempt to cancel confirmed reservation"
        });

        Assert.Equal(HttpStatusCode.Conflict, cancelResp.StatusCode);
    }

    [Fact]
    public async Task GetMyReservations_ReturnsCustomerReservationList()
    {
        var (u10, _) = await SeedDataAsync();
        var token = GenerateToken(u10, RoleConstants.StorageCustomer);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/reservations/mine?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<ReservationListItemResponse>>>(JsonOpts);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.NotNull(content.Data);
        Assert.True(content.Data.Items.Count > 0);
    }
}
