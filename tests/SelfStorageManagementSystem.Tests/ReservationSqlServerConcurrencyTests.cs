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
            // Dọn dẹp an toàn theo đúng thứ tự khóa ngoại ngược lại
            try
            {
                await using var cleanupDb = CreateDbContext(connectionString);

                // 1. Dọn dẹp reservations thuộc bài test (dựa trên testUnitTypeId và cleanupUserIds để bao phủ cả khi Assert lỗi sớm)
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
                        var invoicesToRemove = await cleanupDb.invoices
                            .Where(inv => inv.reservation_id.HasValue && resIds.Contains(inv.reservation_id.Value))
                            .ToListAsync();
                        if (invoicesToRemove.Any())
                        {
                            cleanupDb.invoices.RemoveRange(invoicesToRemove);
                        }

                        cleanupDb.reservations.RemoveRange(resToRemove);
                        await cleanupDb.SaveChangesAsync();
                    }
                }

                // 2. Storage units
                if (cleanupStorageUnitIds.Any())
                {
                    var unitsToRemove = await cleanupDb.storage_units.Where(u => cleanupStorageUnitIds.Contains(u.id)).ToListAsync();
                    cleanupDb.storage_units.RemoveRange(unitsToRemove);
                    await cleanupDb.SaveChangesAsync();
                }

                // 3. Facility rates
                if (cleanupFacilityRateIds.Any())
                {
                    var ratesToRemove = await cleanupDb.facility_rates.Where(r => cleanupFacilityRateIds.Contains(r.id)).ToListAsync();
                    cleanupDb.facility_rates.RemoveRange(ratesToRemove);
                    await cleanupDb.SaveChangesAsync();
                }

                // 4. Price ranges
                if (cleanupPriceRangeIds.Any())
                {
                    var prToRemove = await cleanupDb.price_ranges.Where(pr => cleanupPriceRangeIds.Contains(pr.id)).ToListAsync();
                    cleanupDb.price_ranges.RemoveRange(prToRemove);
                    await cleanupDb.SaveChangesAsync();
                }

                // 5. Unit types
                if (cleanupUnitTypeIds.Any())
                {
                    var utToRemove = await cleanupDb.unit_types.Where(ut => cleanupUnitTypeIds.Contains(ut.id)).ToListAsync();
                    cleanupDb.unit_types.RemoveRange(utToRemove);
                    await cleanupDb.SaveChangesAsync();
                }

                // 6. User profiles & roles & users
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

                // 7. Test facilities (nếu có tạo mới)
                if (cleanupFacilityIds.Any())
                {
                    var facToRemove = await cleanupDb.facilities.Where(f => cleanupFacilityIds.Contains(f.id)).ToListAsync();
                    cleanupDb.facilities.RemoveRange(facToRemove);
                    await cleanupDb.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                // Báo lỗi dọn dẹp rõ ràng, không nuốt ngoại lệ
                throw new InvalidOperationException($"[CLEANUP_FAILURE] Không thể dọn dẹp fixture kiểm thử SQL Server: {ex.Message}", ex);
            }
        }
    }
}
