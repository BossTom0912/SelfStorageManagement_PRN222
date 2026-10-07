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
            Skip = $"Bỏ qua bài test vì chưa cấu hình database kiểm thử tách biệt (yêu cầu biến môi trường '{EnvVarName}'). Không chạy trên database ứng dụng để tránh rủi ro xóa hoặc làm sai lệch dữ liệu.";
        }
    }
}

public class ReservationSqlServerConcurrencyTests
{
    private static string GetTestConnectionString()
    {
        return Environment.GetEnvironmentVariable(SqlIntegrationFactAttribute.EnvVarName)
            ?? throw new InvalidOperationException($"Biến môi trường '{SqlIntegrationFactAttribute.EnvVarName}' chưa được thiết lập.");
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

        // Kiểm tra kết nối tới database kiểm thử. Nếu cấu hình sai, báo lỗi rõ ràng thay vì giả lập pass.
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
        var cleanupUnitTypeIds = new List<long>();
        var cleanupUserIds = new List<long>();

        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var testStartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15));
        var testEndDate = testStartDate.AddMonths(1);

        long facilityId = 1; // Sử dụng facility id = 1 có sẵn trong DB test
        long testUnitTypeId = 0;
        long custUser1Id = 0;
        long custUser2Id = 0;

        try
        {
            // 1. Tự seed dữ liệu kiểm thử ĐỘC LẬP - Tuyệt đối không xóa bất kỳ reservation có sẵn nào
            await using (var seedDb = CreateDbContext(connectionString))
            {
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

                // Tạo 1 unit_type test riêng biệt để không can thiệp vào loại kho khác
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

                // Tạo CHÍNH XÁC 1 storage_unit khả dụng cho loại kho test này -> Sức chứa còn lại đúng bằng 1
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

            // 2. Chạy 2 task đặt chỗ song song cạnh tranh đúng 1 suất khả dụng duy nhất
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

            var successCount = outcomes.Count(o => o.Success);
            var failureCount = outcomes.Count(o => !o.Success);

            // Kiểm tra: ĐÚNG 1 thành công và ĐÚNG 1 thất bại
            Assert.Equal(1, successCount);
            Assert.Equal(1, failureCount);

            var successfulOutcome = outcomes.First(o => o.Success);
            var failedOutcome = outcomes.First(o => !o.Success);

            Assert.NotNull(successfulOutcome.Result);
            cleanupReservationIds.Add(successfulOutcome.Result.Id);

            // Thất bại phải là do hết sức chứa (ConflictException / HTTP 409)
            Assert.NotNull(failedOutcome.Exception);
            Assert.IsType<ConflictException>(failedOutcome.Exception);

            // 3. Xác minh trong database: Chỉ đúng 1 reservation được lưu
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
            // Dọn dẹp chỉ đúng những bản ghi kiểm thử do chính bài test này sinh ra
            try
            {
                await using var cleanupDb = CreateDbContext(connectionString);
                if (cleanupReservationIds.Any())
                {
                    var resToRemove = await cleanupDb.reservations.Where(r => cleanupReservationIds.Contains(r.id)).ToListAsync();
                    cleanupDb.reservations.RemoveRange(resToRemove);
                    await cleanupDb.SaveChangesAsync();
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
            }
            catch
            {
                // Không để lỗi dọn dẹp che khuất kết quả kiểm thử chính
            }
        }
    }
}
