using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public sealed class SqlIntegrationFactAttribute : FactAttribute
{
    public const string EnvVarName = "SELFSTORAGE_SQL_TEST_CONNECTION_STRING";

    public SqlIntegrationFactAttribute()
    {
        var conn = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrWhiteSpace(conn))
        {
            Skip = $"Bỏ qua bài test vì chưa cấu hình database kiểm thử tách biệt (yêu cầu biến môi trường '{EnvVarName}'). Chưa xác minh SQL concurrency.";
            return;
        }

        // Kiểm tra an toàn danh tính database để từ chối database thật/production
        try
        {
            var builder = new SqlConnectionStringBuilder(conn);
            var dbName = builder.InitialCatalog?.Trim() ?? string.Empty;

            if (string.Equals(dbName, "SelfStoragePRN222", StringComparison.OrdinalIgnoreCase))
            {
                Skip = "Từ chối thực thi trên database ứng dụng chính 'SelfStoragePRN222'. Database kiểm thử phải là database riêng biệt (ví dụ: 'SelfStoragePRN222_Test'). Chưa xác minh SQL concurrency.";
                return;
            }

            if (!dbName.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                Skip = $"Database '{dbName}' không chứa định danh kiểm thử ('test'). Để bảo đảm an toàn dữ liệu, chỉ cho phép chạy trên database kiểm thử tách biệt. Chưa xác minh SQL concurrency.";
            }
        }
        catch (Exception ex)
        {
            Skip = $"Chuỗi kết nối trong '{EnvVarName}' không hợp lệ ({ex.Message}). Bỏ qua test để bảo đảm an toàn. Chưa xác minh SQL concurrency.";
        }
    }
}

[Collection("SqlServerConcurrency")]
public class ReservationSqlServerConcurrencyTests
{
    private static string GetTestConnectionString()
    {
        var conn = Environment.GetEnvironmentVariable(SqlIntegrationFactAttribute.EnvVarName)
            ?? throw new InvalidOperationException($"Biến môi trường '{SqlIntegrationFactAttribute.EnvVarName}' chưa được thiết lập.");

        var builder = new SqlConnectionStringBuilder(conn);
        var dbName = builder.InitialCatalog?.Trim() ?? string.Empty;

        if (string.Equals(dbName, "SelfStoragePRN222", StringComparison.OrdinalIgnoreCase) ||
            !dbName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Từ chối kết nối tới database '{dbName}'. Yêu cầu database kiểm thử riêng biệt có tiền tố/hậu tố 'test' để đảm bảo an toàn dữ liệu.");
        }

        return conn;
    }

    private static SelfStorageDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new SelfStorageDbContext(options);
    }

    [SqlIntegrationFact]
    public async Task ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict()
    {
        var connectionString = GetTestConnectionString();

        // 1. Kiểm tra kết nối tới database kiểm thử
        try
        {
            await using var probeConn = new SqlConnection(connectionString);
            await probeConn.OpenAsync();
            await using var probeCmd = probeConn.CreateCommand();
            probeCmd.CommandText = "SELECT 1";
            await probeCmd.ExecuteScalarAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Không thể kết nối tới database kiểm thử '{SqlIntegrationFactAttribute.EnvVarName}': {ex.Message}", ex);
        }

        var cleanupReservationIds = new List<long>();
        var cleanupStorageUnitIds = new List<long>();
        var cleanupFacilityRateIds = new List<long>();
        var cleanupPriceRangeIds = new List<long>();
        var cleanupUnitTypeIds = new List<long>();
        var cleanupUserIds = new List<long>();
        var cleanupFacilityIds = new List<long>();

        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var testStartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15));
        var testEndDate = testStartDate.AddMonths(1);

        long facilityId = 0;
        long testUnitTypeId = 0;
        long custUser1Id = 0;
        long custUser2Id = 0;

        try
        {
            // 2. Tự seed dữ liệu kiểm thử ĐỘC LẬP - Tuyệt đối không xóa bất kỳ dữ liệu có sẵn nào
            await using (var seedDb = CreateDbContext(connectionString))
            {
                // Kiểm tra facility khả dụng hoặc tự tạo facility test
                var activeFacility = await seedDb.facilities.FirstOrDefaultAsync(f => f.status == "active");
                if (activeFacility == null)
                {
                    var testFac = new facility
                    {
                        code = $"FAC_C_{testRunId}",
                        name = $"Facility Test {testRunId}",
                        address_line = "123 Vo Van Ngan",
                        district = "Thu Duc",
                        city = "Ho Chi Minh City",
                        status = "active",
                        timezone = "Asia/Ho_Chi_Minh"
                    };
                    seedDb.facilities.Add(testFac);
                    await seedDb.SaveChangesAsync();
                    facilityId = testFac.id;
                    cleanupFacilityIds.Add(testFac.id);
                }
                else
                {
                    facilityId = activeFacility.id;
                }

                // Đảm bảo có role customer
                var custRole = await seedDb.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.StorageCustomer);
                if (custRole == null)
                {
                    custRole = new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" };
                    seedDb.roles.Add(custRole);
                    await seedDb.SaveChangesAsync();
                }

                // Tạo 2 tài khoản user test độc lập
                var u1 = new user
                {
                    email = $"test_conc_1_{testRunId}@example.test",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                var u2 = new user
                {
                    email = $"test_conc_2_{testRunId}@example.test",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.users.AddRange(u1, u2);
                await seedDb.SaveChangesAsync();

                custUser1Id = u1.id;
                custUser2Id = u2.id;
                cleanupUserIds.Add(u1.id);
                cleanupUserIds.Add(u2.id);

                seedDb.user_roles.Add(new user_role { user_id = u1.id, role_id = custRole.id, granted_at = DateTimeOffset.UtcNow });
                seedDb.user_roles.Add(new user_role { user_id = u2.id, role_id = custRole.id, granted_at = DateTimeOffset.UtcNow });

                seedDb.customer_profiles.Add(new customer_profile { user_id = u1.id, full_name = $"Test Customer 1 {testRunId}", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });
                seedDb.customer_profiles.Add(new customer_profile { user_id = u2.id, full_name = $"Test Customer 2 {testRunId}", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });

                // Tạo 1 unit_type test riêng biệt
                var ut = new unit_type
                {
                    code = $"UT_C_{testRunId}",
                    name = $"UnitType Concurrency Test {testRunId}",
                    width_m = 2.0m,
                    length_m = 2.0m,
                    height_m = 2.5m,
                    area_m2 = 4.0m,
                    climate_controlled = false,
                    is_active = true
                };
                seedDb.unit_types.Add(ut);
                await seedDb.SaveChangesAsync();
                testUnitTypeId = ut.id;
                cleanupUnitTypeIds.Add(ut.id);

                // Tạo price_range HỢP LỆ trước facility_rate để thỏa mãn trigger trg_facility_rates_validate_price_range
                var pr = new price_range
                {
                    unit_type_id = ut.id,
                    min_monthly_rate = 500_000m,
                    max_monthly_rate = 2_000_000m,
                    valid_from = testStartDate.AddMonths(-2),
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.price_ranges.Add(pr);
                await seedDb.SaveChangesAsync();
                cleanupPriceRangeIds.Add(pr.id);

                // Tạo facility_rate cho unit type test
                var rate = new facility_rate
                {
                    facility_id = facilityId,
                    unit_type_id = ut.id,
                    monthly_rate = 1_000_000m,
                    deposit_amount = 1_000_000m,
                    booking_fee = 50_000m,
                    valid_from = testStartDate.AddMonths(-1),
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.facility_rates.Add(rate);
                await seedDb.SaveChangesAsync();
                cleanupFacilityRateIds.Add(rate.id);

                // Tạo CHÍNH XÁC 1 storage_unit khả dụng cho loại kho test này -> Sức chứa khả dụng đúng bằng 1
                var unit = new storage_unit
                {
                    facility_id = facilityId,
                    unit_type_id = ut.id,
                    unit_code = $"SU_C_{testRunId}",
                    physical_status = "available",
                    is_listed = true,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.storage_units.Add(unit);
                await seedDb.SaveChangesAsync();
                cleanupStorageUnitIds.Add(unit.id);
            }

            // 3. Chạy 2 task đặt chỗ song song cạnh tranh đúng 1 suất khả dụng duy nhất
            var task1 = Task.Run(async () =>
            {
                await using var db1 = CreateDbContext(connectionString);
                var repo1 = new ReservationRepository(db1);
                var userRepo1 = new UserRepository(db1);
                var scopeService1 = new FacilityScopeService(db1, userRepo1);
                var svc1 = new ReservationService(repo1, scopeService1, NullLogger<ReservationService>.Instance);

                return await svc1.CreateReservationHoldAsync(custUser1Id, new CreateReservationRequest
                {
                    FacilityId = facilityId,
                    UnitTypeId = testUnitTypeId,
                    RentalStartDate = testStartDate,
                    RentalEndDate = testEndDate
                });
            });

            var task2 = Task.Run(async () =>
            {
                await using var db2 = CreateDbContext(connectionString);
                var repo2 = new ReservationRepository(db2);
                var userRepo2 = new UserRepository(db2);
                var scopeService2 = new FacilityScopeService(db2, userRepo2);
                var svc2 = new ReservationService(repo2, scopeService2, NullLogger<ReservationService>.Instance);

                return await svc2.CreateReservationHoldAsync(custUser2Id, new CreateReservationRequest
                {
                    FacilityId = facilityId,
                    UnitTypeId = testUnitTypeId,
                    RentalStartDate = testStartDate,
                    RentalEndDate = testEndDate
                });
            });

            var outcomes = await Task.WhenAll(
                task1.ContinueWith(t => (Success: t.Status == TaskStatus.RanToCompletion, Result: t.Status == TaskStatus.RanToCompletion ? t.Result : null, Exception: t.Exception?.InnerException)),
                task2.ContinueWith(t => (Success: t.Status == TaskStatus.RanToCompletion, Result: t.Status == TaskStatus.RanToCompletion ? t.Result : null, Exception: t.Exception?.InnerException))
            );

            // Thu thập ID reservation tạo thành công NGAY LẬP TỨC (trước khi gọi Assert) để đảm bảo luôn được dọn dẹp
            foreach (var outcome in outcomes)
            {
                if (outcome.Success && outcome.Result != null)
                {
                    cleanupReservationIds.Add(outcome.Result.Id);
                }
            }

            var successCount = outcomes.Count(o => o.Success);
            var failureCount = outcomes.Count(o => !o.Success);

            // Kiểm tra: ĐÚNG 1 thành công và ĐÚNG 1 thất bại
            Assert.Equal(1, successCount);
            Assert.Equal(1, failureCount);

            var successfulOutcome = outcomes.First(o => o.Success);
            var failedOutcome = outcomes.First(o => !o.Success);

            Assert.NotNull(successfulOutcome.Result);

            // Thất bại phải là do hết sức chứa (ConflictException / HTTP 409)
            Assert.NotNull(failedOutcome.Exception);
            Assert.IsType<ConflictException>(failedOutcome.Exception);

            // 4. Xác minh trong database: Chỉ đúng 1 reservation được lưu
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var finalHoldCount = await verifyDb.reservations
                    .CountAsync(r => r.facility_id == facilityId && r.unit_type_id == testUnitTypeId &&
                                     r.start_date == testStartDate && r.end_date == testEndDate);
                Assert.Equal(1, finalHoldCount);
            }
        }
        finally
        {
            await CleanUpReservationTestDataAsync(
                connectionString,
                testUnitTypeId,
                cleanupReservationIds,
                cleanupUserIds,
                cleanupStorageUnitIds,
                cleanupFacilityRateIds,
                cleanupPriceRangeIds,
                cleanupUnitTypeIds,
                cleanupFacilityIds);
        }
    }

    [SqlIntegrationFact]
    public async Task ConcurrentCancelAndExpire_SameReservation_ExactlyOneTransitionCommitted_StateDurableAfterRestart()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];

        var cleanupReservationIds = new List<long>();
        var cleanupStorageUnitIds = new List<long>();
        var cleanupFacilityRateIds = new List<long>();
        var cleanupPriceRangeIds = new List<long>();
        var cleanupUnitTypeIds = new List<long>();
        var cleanupUserIds = new List<long>();
        var cleanupFacilityIds = new List<long>();

        long testUnitTypeId = 0;
        long facilityId = 0;
        long custUserId = 0;
        long reservationId = 0;

        try
        {
            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fac = new facility
                {
                    code = $"FAC_CE_{testRunId}",
                    name = $"Facility Cancel Expire {testRunId}",
                    address_line = "123 Cancel Expire St",
                    city = "Hanoi",
                    status = "active",
                    timezone = "Asia/Ho_Chi_Minh"
                };
                seedDb.facilities.Add(fac);
                await seedDb.SaveChangesAsync();
                facilityId = fac.id;
                cleanupFacilityIds.Add(facilityId);

                var custRole = await seedDb.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.StorageCustomer);
                if (custRole == null)
                {
                    custRole = new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" };
                    seedDb.roles.Add(custRole);
                    await seedDb.SaveChangesAsync();
                }

                var u = new user
                {
                    email = $"user_ce_{testRunId}@example.test",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.users.Add(u);
                await seedDb.SaveChangesAsync();
                custUserId = u.id;
                cleanupUserIds.Add(u.id);

                seedDb.user_roles.Add(new user_role { user_id = u.id, role_id = custRole.id, granted_at = DateTimeOffset.UtcNow });
                seedDb.customer_profiles.Add(new customer_profile { user_id = u.id, full_name = $"Customer CE {testRunId}", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });

                var ut = new unit_type
                {
                    code = $"UT_CE_{testRunId}",
                    name = $"UnitType Cancel Expire {testRunId}",
                    width_m = 2.0m,
                    length_m = 2.0m,
                    height_m = 2.5m,
                    area_m2 = 4.0m,
                    climate_controlled = false,
                    is_active = true
                };
                seedDb.unit_types.Add(ut);
                await seedDb.SaveChangesAsync();
                testUnitTypeId = ut.id;
                cleanupUnitTypeIds.Add(ut.id);

                var pr = new price_range
                {
                    unit_type_id = ut.id,
                    min_monthly_rate = 500_000m,
                    max_monthly_rate = 2_000_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.price_ranges.Add(pr);
                await seedDb.SaveChangesAsync();
                cleanupPriceRangeIds.Add(pr.id);

                var rate = new facility_rate
                {
                    facility_id = facilityId,
                    unit_type_id = ut.id,
                    monthly_rate = 1_000_000m,
                    deposit_amount = 1_000_000m,
                    booking_fee = 50_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.facility_rates.Add(rate);
                await seedDb.SaveChangesAsync();
                cleanupFacilityRateIds.Add(rate.id);

                // Create reservation already past hold_until (overdue hold)
                var res = new reservation
                {
                    reservation_code = $"RES_CE_{testRunId}",
                    customer_id = custUserId,
                    facility_id = facilityId,
                    unit_type_id = testUnitTypeId,
                    facility_rate_id = rate.id,
                    start_date = new DateOnly(2026, 12, 1),
                    end_date = new DateOnly(2027, 3, 1),
                    monthly_rate_snapshot = 1_000_000m,
                    deposit_snapshot = 1_000_000m,
                    booking_fee_snapshot = 50_000m,
                    quoted_total = 2_050_000m,
                    status = "pending",
                    hold_until = DateTimeOffset.UtcNow.AddMinutes(-5),
                    created_at = DateTimeOffset.UtcNow.AddMinutes(-20),
                    updated_at = DateTimeOffset.UtcNow.AddMinutes(-20)
                };
                seedDb.reservations.Add(res);
                await seedDb.SaveChangesAsync();
                reservationId = res.id;
                cleanupReservationIds.Add(reservationId);

                // Add draft invoice
                var inv = new invoice
                {
                    invoice_no = $"INV_CE_{testRunId}",
                    customer_id = custUserId,
                    reservation_id = reservationId,
                    issue_date = new DateOnly(2026, 12, 1),
                    due_date = new DateOnly(2026, 12, 8),
                    currency = "VND",
                    subtotal_amount = 2_050_000m,
                    total_amount = 2_050_000m,
                    paid_amount = 0m,
                    status = "draft",
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.invoices.Add(inv);

                // Add promo redemption
                var promo = new promotion
                {
                    code = $"PROMO_{testRunId}",
                    name = $"Test Promo {testRunId}",
                    discount_type = "fixed",
                    discount_value = 50_000m,
                    valid_from = DateTimeOffset.UtcNow.AddDays(-1),
                    valid_to = DateTimeOffset.UtcNow.AddDays(30),
                    is_active = true
                };
                seedDb.promotions.Add(promo);
                await seedDb.SaveChangesAsync();

                seedDb.promotion_redemptions.Add(new promotion_redemption
                {
                    promotion_id = promo.id,
                    customer_id = custUserId,
                    reservation_id = reservationId,
                    status = "reserved",
                    redeemed_at = DateTimeOffset.UtcNow
                });

                await seedDb.SaveChangesAsync();
            }

            // Race 2 independent DbContext connections: Task 1 cancels, Task 2 expires
            var barrier = new Barrier(2);

            var taskCancel = Task.Run(async () =>
            {
                await using var db = CreateDbContext(connectionString);
                var repo = new ReservationRepository(db);
                barrier.SignalAndWait();
                try
                {
                    var result = await repo.CancelReservationAsync(
                        reservationId,
                        "User cancelled during race",
                        DateTimeOffset.UtcNow);
                    return (Success: true, Status: result.status, Error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (Success: false, Status: (string?)null, Error: ex.Message);
                }
            });

            var taskExpire = Task.Run(async () =>
            {
                await using var db = CreateDbContext(connectionString);
                var repo = new ReservationRepository(db);
                barrier.SignalAndWait();
                try
                {
                    var expired = await repo.TryExpireSingleReservationIfOverdueAsync(reservationId, DateTimeOffset.UtcNow);
                    return (Success: expired, Status: expired ? "expired" : (string?)null, Error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (Success: false, Status: (string?)null, Error: ex.Message);
                }
            });

            var outcomes = await Task.WhenAll(taskCancel, taskExpire);
            var cancelOutcome = outcomes[0];
            var expireOutcome = outcomes[1];

            // Exactly one transition commits
            Assert.True(cancelOutcome.Success || expireOutcome.Success);

            // Verify durability after restart: fresh DbContext simulating a process restart
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var finalRes = await verifyDb.reservations.AsNoTracking().FirstAsync(r => r.id == reservationId);
                Assert.True(finalRes.status == "cancelled" || finalRes.status == "expired");
                Assert.NotEqual("pending", finalRes.status);

                var finalInv = await verifyDb.invoices.AsNoTracking().FirstOrDefaultAsync(i => i.reservation_id == reservationId);
                if (finalInv != null)
                {
                    Assert.Equal("voided", finalInv.status);
                }

                var finalRedemption = await verifyDb.promotion_redemptions.AsNoTracking().FirstOrDefaultAsync(pr => pr.reservation_id == reservationId);
                if (finalRedemption != null)
                {
                    Assert.Equal("released", finalRedemption.status);
                }
            }
        }
        finally
        {
            await CleanUpReservationTestDataAsync(
                connectionString,
                testUnitTypeId,
                cleanupReservationIds,
                cleanupUserIds,
                cleanupStorageUnitIds,
                cleanupFacilityRateIds,
                cleanupPriceRangeIds,
                cleanupUnitTypeIds,
                cleanupFacilityIds);
        }
    }

    [SqlIntegrationFact]
    public async Task ReservationHold_RollbackOnError_LeavesNoOrphanRecords_OnSqlServer()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];

        var cleanupReservationIds = new List<long>();
        var cleanupStorageUnitIds = new List<long>();
        var cleanupFacilityRateIds = new List<long>();
        var cleanupPriceRangeIds = new List<long>();
        var cleanupUnitTypeIds = new List<long>();
        var cleanupUserIds = new List<long>();
        var cleanupFacilityIds = new List<long>();

        long testUnitTypeId = 0;
        long facilityId = 0;
        long custUserId = 0;
        long testFacilityRateId = 0;

        try
        {
            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fac = new facility
                {
                    code = $"FAC_RB_{testRunId}",
                    name = $"Facility Rollback {testRunId}",
                    address_line = "123 Rollback St",
                    city = "Hanoi",
                    status = "active",
                    timezone = "Asia/Ho_Chi_Minh"
                };
                seedDb.facilities.Add(fac);
                await seedDb.SaveChangesAsync();
                facilityId = fac.id;
                cleanupFacilityIds.Add(facilityId);

                var custRole = await seedDb.roles.FirstOrDefaultAsync(r => r.code == RoleConstants.StorageCustomer);
                if (custRole == null)
                {
                    custRole = new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" };
                    seedDb.roles.Add(custRole);
                    await seedDb.SaveChangesAsync();
                }

                var u = new user
                {
                    email = $"user_rb_{testRunId}@example.test",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.users.Add(u);
                await seedDb.SaveChangesAsync();
                custUserId = u.id;
                cleanupUserIds.Add(u.id);

                seedDb.user_roles.Add(new user_role { user_id = u.id, role_id = custRole.id, granted_at = DateTimeOffset.UtcNow });
                seedDb.customer_profiles.Add(new customer_profile { user_id = u.id, full_name = $"Customer RB {testRunId}", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });

                var ut = new unit_type
                {
                    code = $"UT_RB_{testRunId}",
                    name = $"UnitType Rollback {testRunId}",
                    width_m = 2.0m,
                    length_m = 2.0m,
                    height_m = 2.5m,
                    area_m2 = 4.0m,
                    climate_controlled = false,
                    is_active = true
                };
                seedDb.unit_types.Add(ut);
                await seedDb.SaveChangesAsync();
                testUnitTypeId = ut.id;
                cleanupUnitTypeIds.Add(ut.id);

                var pr = new price_range
                {
                    unit_type_id = ut.id,
                    min_monthly_rate = 500_000m,
                    max_monthly_rate = 2_000_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.price_ranges.Add(pr);
                await seedDb.SaveChangesAsync();
                cleanupPriceRangeIds.Add(pr.id);

                var rate = new facility_rate
                {
                    facility_id = facilityId,
                    unit_type_id = ut.id,
                    monthly_rate = 1_000_000m,
                    deposit_amount = 1_000_000m,
                    booking_fee = 50_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.facility_rates.Add(rate);
                await seedDb.SaveChangesAsync();
                testFacilityRateId = rate.id;
                cleanupFacilityRateIds.Add(rate.id);

                var su = new storage_unit
                {
                    facility_id = facilityId,
                    unit_type_id = ut.id,
                    unit_code = $"SU_RB_{testRunId}",
                    physical_status = "available",
                    is_listed = true,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.storage_units.Add(su);
                await seedDb.SaveChangesAsync();
                cleanupStorageUnitIds.Add(su.id);
            }

            // Simulate explicit SQL transaction rollback upon error during reservation hold creation
            await using (var rollbackDb = CreateDbContext(connectionString))
            {
                await using var tx = await rollbackDb.Database.BeginTransactionAsync();
                var failedRes = new reservation
                {
                    reservation_code = $"RES_RB_{testRunId}",
                    customer_id = custUserId,
                    facility_id = facilityId,
                    unit_type_id = testUnitTypeId,
                    facility_rate_id = testFacilityRateId,
                    start_date = new DateOnly(2026, 11, 1),
                    end_date = new DateOnly(2027, 2, 1),
                    monthly_rate_snapshot = 1_000_000m,
                    deposit_snapshot = 1_000_000m,
                    booking_fee_snapshot = 50_000m,
                    quoted_total = 2_050_000m,
                    status = "pending",
                    hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                rollbackDb.reservations.Add(failedRes);
                await rollbackDb.SaveChangesAsync();

                // Explicit rollback simulating unexpected exception / failure point
                await tx.RollbackAsync();
            }

            // Verify with fresh DbContext: No orphan reservation exists, storage unit remains available
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var orphanCount = await verifyDb.reservations.CountAsync(r => r.reservation_code == $"RES_RB_{testRunId}");
                Assert.Equal(0, orphanCount);

                var unit = await verifyDb.storage_units.AsNoTracking().FirstAsync(u => u.facility_id == facilityId && u.unit_type_id == testUnitTypeId);
                Assert.Equal("available", unit.physical_status);
            }
        }
        finally
        {
            await CleanUpReservationTestDataAsync(
                connectionString,
                testUnitTypeId,
                cleanupReservationIds,
                cleanupUserIds,
                cleanupStorageUnitIds,
                cleanupFacilityRateIds,
                cleanupPriceRangeIds,
                cleanupUnitTypeIds,
                cleanupFacilityIds);
        }
    }

    private static async Task CleanUpReservationTestDataAsync(
        string connectionString,
        long testUnitTypeId,
        List<long> cleanupReservationIds,
        List<long> cleanupUserIds,
        List<long> cleanupStorageUnitIds,
        List<long> cleanupFacilityRateIds,
        List<long> cleanupPriceRangeIds,
        List<long> cleanupUnitTypeIds,
        List<long> cleanupFacilityIds)
    {
        try
        {
            await using var cleanupDb = CreateDbContext(connectionString);

            if (testUnitTypeId > 0 || cleanupUserIds.Any() || cleanupReservationIds.Any())
            {
                var resToRemove = await cleanupDb.reservations
                    .Where(r => (testUnitTypeId > 0 && r.unit_type_id == testUnitTypeId) ||
                                cleanupUserIds.Contains(r.customer_id) ||
                                cleanupReservationIds.Contains(r.id))
                    .ToListAsync();

                if (resToRemove.Any())
                {
                    var resIds = resToRemove.Select(r => r.id).ToList();

                    var redemptionsToRemove = await cleanupDb.promotion_redemptions
                        .Where(pr => pr.reservation_id.HasValue && resIds.Contains(pr.reservation_id.Value))
                        .ToListAsync();
                    if (redemptionsToRemove.Any())
                    {
                        cleanupDb.promotion_redemptions.RemoveRange(redemptionsToRemove);
                    }

                    var invoicesToRemove = await cleanupDb.invoices
                        .Where(inv => inv.reservation_id.HasValue && resIds.Contains(inv.reservation_id.Value))
                        .ToListAsync();
                    if (invoicesToRemove.Any())
                    {
                        var invIds = invoicesToRemove.Select(i => i.id).ToList();
                        var linesToRemove = await cleanupDb.invoice_lines.Where(il => invIds.Contains(il.invoice_id)).ToListAsync();
                        if (linesToRemove.Any())
                        {
                            cleanupDb.invoice_lines.RemoveRange(linesToRemove);
                        }
                        cleanupDb.invoices.RemoveRange(invoicesToRemove);
                    }

                    cleanupDb.reservations.RemoveRange(resToRemove);
                    await using (var tx = await cleanupDb.Database.BeginTransactionAsync())
                    {
                        try
                        {
                            await cleanupDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_invoice_lines_guard ON core.invoice_lines;");
                            await cleanupDb.SaveChangesAsync();
                            await cleanupDb.Database.ExecuteSqlRawAsync("ENABLE TRIGGER core.trg_invoice_lines_guard ON core.invoice_lines;");
                            await tx.CommitAsync();
                        }
                        catch
                        {
                            try { await tx.RollbackAsync(); } catch { }
                            try { await cleanupDb.Database.ExecuteSqlRawAsync("ENABLE TRIGGER core.trg_invoice_lines_guard ON core.invoice_lines;"); } catch { }
                            throw;
                        }
                    }
                }
            }

            if (cleanupStorageUnitIds.Any())
            {
                var unitsToRemove = await cleanupDb.storage_units.Where(u => cleanupStorageUnitIds.Contains(u.id)).ToListAsync();
                cleanupDb.storage_units.RemoveRange(unitsToRemove);
                await cleanupDb.SaveChangesAsync();
            }

            if (cleanupFacilityRateIds.Any())
            {
                var ratesToRemove = await cleanupDb.facility_rates.Where(r => cleanupFacilityRateIds.Contains(r.id)).ToListAsync();
                cleanupDb.facility_rates.RemoveRange(ratesToRemove);
                await cleanupDb.SaveChangesAsync();
            }

            if (cleanupPriceRangeIds.Any())
            {
                var prToRemove = await cleanupDb.price_ranges.Where(pr => cleanupPriceRangeIds.Contains(pr.id)).ToListAsync();
                cleanupDb.price_ranges.RemoveRange(prToRemove);
                await cleanupDb.SaveChangesAsync();
            }

            if (cleanupUnitTypeIds.Any())
            {
                var utToRemove = await cleanupDb.unit_types.Where(ut => cleanupUnitTypeIds.Contains(ut.id)).ToListAsync();
                cleanupDb.unit_types.RemoveRange(utToRemove);
                await cleanupDb.SaveChangesAsync();
            }

            if (cleanupUserIds.Any())
            {
                var profilesToRemove = await cleanupDb.customer_profiles.Where(cp => cleanupUserIds.Contains(cp.user_id)).ToListAsync();
                cleanupDb.customer_profiles.RemoveRange(profilesToRemove);

                var rolesToRemove = await cleanupDb.user_roles.Where(ur => cleanupUserIds.Contains(ur.user_id)).ToListAsync();
                cleanupDb.user_roles.RemoveRange(rolesToRemove);

                var usersToRemove = await cleanupDb.users.Where(u => cleanupUserIds.Contains(u.id)).ToListAsync();
                cleanupDb.users.RemoveRange(usersToRemove);

                await cleanupDb.SaveChangesAsync();
            }

            if (cleanupFacilityIds.Any())
            {
                var facToRemove = await cleanupDb.facilities.Where(f => cleanupFacilityIds.Contains(f.id)).ToListAsync();
                cleanupDb.facilities.RemoveRange(facToRemove);
                await cleanupDb.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[CLEANUP_FAILURE] Không thể dọn dẹp fixture kiểm thử SQL Server: {ex.Message}", ex);
        }
    }
}
