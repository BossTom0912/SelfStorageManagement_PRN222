using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

[Collection("SqlServerConcurrency")]
public class CheckInSqlServerConcurrencyTests
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
    public async Task ConcurrentAssignUnit_TwoReservationsCompetingForSameUnit_ExactlyOneSucceeds()
    {
        var connectionString = GetTestConnectionString();

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

        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();
        var cleanUpUnitIds = new List<long>();
        var cleanUpFacilityRateIds = new List<long>();
        var cleanUpPriceRangeIds = new List<long>();

        try
        {
            long facilityId;
            long unitTypeId;
            long unitId;
            long staffUserId;
            long res1Id;
            long res2Id;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fac = new facility
                {
                    code = $"FC_{testRunId}",
                    name = $"Facility {testRunId}",
                    address_line = "123 Concurrency St",
                    city = "Ho Chi Minh City",
                    status = "active",
                    timezone = "Asia/Ho_Chi_Minh"
                };
                seedDb.facilities.Add(fac);
                await seedDb.SaveChangesAsync();
                facilityId = fac.id;
                cleanUpFacilityIds.Add(facilityId);

                var ut = new unit_type
                {
                    code = $"UT_{testRunId}",
                    name = $"Type {testRunId}",
                    width_m = 2.0m,
                    length_m = 2.0m,
                    height_m = 2.5m,
                    climate_controlled = false,
                    is_active = true
                };
                seedDb.unit_types.Add(ut);
                await seedDb.SaveChangesAsync();
                unitTypeId = ut.id;
                cleanUpUnitTypeIds.Add(unitTypeId);

                var pr = new price_range
                {
                    unit_type_id = unitTypeId,
                    min_monthly_rate = 500_000m,
                    max_monthly_rate = 5_000_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    valid_to = null
                };
                seedDb.price_ranges.Add(pr);
                await seedDb.SaveChangesAsync();
                cleanUpPriceRangeIds.Add(pr.id);

                var rate = new facility_rate
                {
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    monthly_rate = 1_000_000m,
                    deposit_amount = 1_000_000m,
                    booking_fee = 50_000m,
                    valid_from = new DateOnly(2020, 1, 1)
                };
                seedDb.facility_rates.Add(rate);
                await seedDb.SaveChangesAsync();
                cleanUpFacilityRateIds.Add(rate.id);

                var su = new storage_unit
                {
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    unit_code = $"U_{testRunId}",
                    physical_status = "available",
                    is_listed = true,
                    floor_label = "Tang 1"
                };
                seedDb.storage_units.Add(su);
                await seedDb.SaveChangesAsync();
                unitId = su.id;
                cleanUpUnitIds.Add(unitId);

                // Staff
                var staff = new user
                {
                    email = $"staff_{testRunId}@test.com",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.users.Add(staff);
                await seedDb.SaveChangesAsync();
                staffUserId = staff.id;
                cleanUpUserIds.Add(staffUserId);

                seedDb.employee_profiles.Add(new employee_profile
                {
                    user_id = staffUserId,
                    employee_code = $"E_{testRunId}",
                    full_name = $"Staff {testRunId}",
                    employment_status = "active",
                    hire_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                });
                seedDb.staff_facility_assignments.Add(new staff_facility_assignment
                {
                    employee_id = staffUserId,
                    facility_id = facilityId,
                    assignment_role = RoleConstants.FacilityStaff,
                    starts_at = DateTimeOffset.UtcNow.AddMonths(-1),
                    created_at = DateTimeOffset.UtcNow
                });

                // Customer 1 & 2
                var cust1 = new user
                {
                    email = $"c1_{testRunId}@test.com",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                var cust2 = new user
                {
                    email = $"c2_{testRunId}@test.com",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.users.AddRange(cust1, cust2);
                await seedDb.SaveChangesAsync();
                cleanUpUserIds.Add(cust1.id);
                cleanUpUserIds.Add(cust2.id);

                seedDb.customer_profiles.Add(new customer_profile { user_id = cust1.id, full_name = "C1", identity_number = $"079_{testRunId}_1", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });
                seedDb.customer_profiles.Add(new customer_profile { user_id = cust2.id, full_name = "C2", identity_number = $"079_{testRunId}_2", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });

                // Policy
                var pol = await seedDb.policy_versions
                    .FirstOrDefaultAsync(p => p.policy_type == "rental_terms" && p.valid_from <= new DateOnly(2026, 1, 1));
                if (pol == null)
                {
                    pol = new policy_version
                    {
                        policy_type = "rental_terms",
                        version = $"2026.1_{testRunId}",
                        content = "{}",
                        valid_from = new DateOnly(2020, 1, 1),
                        created_at = DateTimeOffset.UtcNow
                    };
                    seedDb.policy_versions.Add(pol);
                    await seedDb.SaveChangesAsync();
                }

                // Reservations 1 and 2
                var r1 = new reservation
                {
                    reservation_code = $"R1_{testRunId}",
                    customer_id = cust1.id,
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    facility_rate_id = rate.id,
                    start_date = today,
                    end_date = today.AddMonths(1),
                    status = "confirmed",
                    monthly_rate_snapshot = 1000000m,
                    deposit_snapshot = 1000000m,
                    booking_fee_snapshot = 50000m,
                    discount_snapshot = 0m,
                    quoted_total = 2050000m,
                    hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                var r2 = new reservation
                {
                    reservation_code = $"R2_{testRunId}",
                    customer_id = cust2.id,
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    facility_rate_id = rate.id,
                    start_date = today,
                    end_date = today.AddMonths(1),
                    status = "confirmed",
                    monthly_rate_snapshot = 1000000m,
                    deposit_snapshot = 1000000m,
                    booking_fee_snapshot = 50000m,
                    discount_snapshot = 0m,
                    quoted_total = 2050000m,
                    hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.reservations.AddRange(r1, r2);
                await seedDb.SaveChangesAsync();
                res1Id = r1.id;
                res2Id = r2.id;
                cleanUpReservationIds.Add(res1Id);
                cleanUpReservationIds.Add(res2Id);

                // Agreements 1 and 2
                var a1 = new rental_agreement
                {
                    agreement_no = $"A1_{testRunId}",
                    reservation_id = res1Id,
                    customer_id = cust1.id,
                    facility_id = facilityId,
                    policy_version_id = pol.id,
                    start_date = today,
                    end_date = today.AddMonths(1),
                    monthly_rate_snapshot = 1000000m,
                    deposit_snapshot = 1000000m,
                    deposit_balance = 1000000m,
                    status = "scheduled",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                var a2 = new rental_agreement
                {
                    agreement_no = $"A2_{testRunId}",
                    reservation_id = res2Id,
                    customer_id = cust2.id,
                    facility_id = facilityId,
                    policy_version_id = pol.id,
                    start_date = today,
                    end_date = today.AddMonths(1),
                    monthly_rate_snapshot = 1000000m,
                    deposit_snapshot = 1000000m,
                    deposit_balance = 1000000m,
                    status = "scheduled",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.rental_agreements.AddRange(a1, a2);
                await seedDb.SaveChangesAsync();

                // Invoices paid
                var inv1 = new invoice
                {
                    invoice_no = $"I1_{testRunId}",
                    customer_id = cust1.id,
                    reservation_id = res1Id,
                    agreement_id = a1.id,
                    issue_date = today,
                    due_date = today.AddDays(7),
                    currency = "VND",
                    subtotal_amount = 2000000m,
                    total_amount = 2000000m,
                    paid_amount = 2000000m,
                    status = "paid",
                    created_at = DateTimeOffset.UtcNow
                };
                var inv2 = new invoice
                {
                    invoice_no = $"I2_{testRunId}",
                    customer_id = cust2.id,
                    reservation_id = res2Id,
                    agreement_id = a2.id,
                    issue_date = today,
                    due_date = today.AddDays(7),
                    currency = "VND",
                    subtotal_amount = 2000000m,
                    total_amount = 2000000m,
                    paid_amount = 2000000m,
                    status = "paid",
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.invoices.AddRange(inv1, inv2);

                // Pre-verify ID for both
                seedDb.identity_verifications.Add(new identity_verification
                {
                    reservation_id = res1Id,
                    verified_by = staffUserId,
                    verification_method = "government_id",
                    result = "verified",
                    verified_at = DateTimeOffset.UtcNow
                });
                seedDb.identity_verifications.Add(new identity_verification
                {
                    reservation_id = res2Id,
                    verified_by = staffUserId,
                    verification_method = "government_id",
                    result = "verified",
                    verified_at = DateTimeOffset.UtcNow
                });

                await seedDb.SaveChangesAsync();
            }

            // Race 2 staff assigning the SAME storage unit to 2 different reservations
            var barrier = new Barrier(2);

            var task1 = Task.Run(async () =>
            {
                await using var db = CreateDbContext(connectionString);
                var repo = new CheckInRepository(db);
                barrier.SignalAndWait();
                try
                {
                    var alloc = await repo.AssignUnitWithLockAsync(res1Id, unitId, staffUserId);
                    return (Success: true, Error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (Success: false, Error: ex.Message);
                }
            });

            var task2 = Task.Run(async () =>
            {
                await using var db = CreateDbContext(connectionString);
                var repo = new CheckInRepository(db);
                barrier.SignalAndWait();
                try
                {
                    var alloc = await repo.AssignUnitWithLockAsync(res2Id, unitId, staffUserId);
                    return (Success: true, Error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (Success: false, Error: ex.Message);
                }
            });

            var results = await Task.WhenAll(task1, task2);

            var successCount = results.Count(r => r.Success);
            var failCount = results.Count(r => !r.Success);

            // Exactly ONE must succeed and exactly ONE must fail with controlled conflict
            Assert.Equal(1, successCount);
            Assert.Equal(1, failCount);

            var failedResult = results.First(r => !r.Success);
            Assert.Contains("STORAGE_UNIT_NOT_AVAILABLE", failedResult.Error);

            // Verify database consistency
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var suAfter = await verifyDb.storage_units.FindAsync(unitId);
                Assert.NotNull(suAfter);
                Assert.Equal("reserved", suAfter.physical_status);

                var activeAllocs = await verifyDb.unit_allocations
                    .Where(ua => ua.storage_unit_id == unitId && ua.status == "active")
                    .ToListAsync();
                Assert.Single(activeAllocs);
            }
        }
        finally
        {
            await using var cleanDb = CreateDbContext(connectionString);
            if (cleanUpReservationIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.access_credentials WHERE agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.handover_records WHERE agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.inspection_items WHERE inspection_id IN (SELECT id FROM core.inspections WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.inspections WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_allocations WHERE storage_unit_id IN ({string.Join(",", cleanUpUnitIds)}) OR agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.invoices WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.identity_verifications WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.reservations WHERE id IN ({string.Join(",", cleanUpReservationIds)})");
            }
            if (cleanUpUnitIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_status_history WHERE storage_unit_id IN ({string.Join(",", cleanUpUnitIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.storage_units WHERE id IN ({string.Join(",", cleanUpUnitIds)})");
            }
            if (cleanUpFacilityRateIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.facility_rates WHERE id IN ({string.Join(",", cleanUpFacilityRateIds)})");
            }
            if (cleanUpPriceRangeIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.price_ranges WHERE id IN ({string.Join(",", cleanUpPriceRangeIds)})");
            }
            if (cleanUpUnitTypeIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_types WHERE id IN ({string.Join(",", cleanUpUnitTypeIds)})");
            }
            if (cleanUpFacilityIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.staff_facility_assignments WHERE facility_id IN ({string.Join(",", cleanUpFacilityIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.facilities WHERE id IN ({string.Join(",", cleanUpFacilityIds)})");
            }
            if (cleanUpUserIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.employee_profiles WHERE user_id IN ({string.Join(",", cleanUpUserIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.customer_profiles WHERE user_id IN ({string.Join(",", cleanUpUserIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.users WHERE id IN ({string.Join(",", cleanUpUserIds)})");
            }
        }
    }

    [SqlIntegrationFact]
    public async Task ConcurrentCompleteCheckIn_SameReservation_ExactlyOneExecutes_OtherHandlesSafely()
    {
        var connectionString = GetTestConnectionString();

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

        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();
        var cleanUpUnitIds = new List<long>();
        var cleanUpFacilityRateIds = new List<long>();
        var cleanUpPriceRangeIds = new List<long>();

        try
        {
            long facilityId;
            long unitTypeId;
            long unitId;
            long staffUserId;
            long customerUserId;
            long resId;
            long allocId;
            long inspId;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fac = new facility
                {
                    code = $"FC_C_{testRunId}",
                    name = $"Facility Complete {testRunId}",
                    address_line = "123 Complete St",
                    city = "Ho Chi Minh City",
                    status = "active",
                    timezone = "Asia/Ho_Chi_Minh"
                };
                seedDb.facilities.Add(fac);
                await seedDb.SaveChangesAsync();
                facilityId = fac.id;
                cleanUpFacilityIds.Add(facilityId);

                var ut = new unit_type
                {
                    code = $"UTC_{testRunId}",
                    name = $"Type Complete {testRunId}",
                    width_m = 2.0m,
                    length_m = 2.0m,
                    height_m = 2.5m,
                    climate_controlled = false,
                    is_active = true
                };
                seedDb.unit_types.Add(ut);
                await seedDb.SaveChangesAsync();
                unitTypeId = ut.id;
                cleanUpUnitTypeIds.Add(unitTypeId);

                var pr = new price_range
                {
                    unit_type_id = unitTypeId,
                    min_monthly_rate = 500_000m,
                    max_monthly_rate = 5_000_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    valid_to = null
                };
                seedDb.price_ranges.Add(pr);
                await seedDb.SaveChangesAsync();
                cleanUpPriceRangeIds.Add(pr.id);

                var rate = new facility_rate
                {
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    monthly_rate = 1_000_000m,
                    deposit_amount = 1_000_000m,
                    booking_fee = 50_000m,
                    valid_from = new DateOnly(2020, 1, 1)
                };
                seedDb.facility_rates.Add(rate);
                await seedDb.SaveChangesAsync();
                cleanUpFacilityRateIds.Add(rate.id);

                var su = new storage_unit
                {
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    unit_code = $"UC_{testRunId}",
                    physical_status = "reserved", // Already assigned
                    is_listed = true,
                    floor_label = "Tang 1"
                };
                seedDb.storage_units.Add(su);
                await seedDb.SaveChangesAsync();
                unitId = su.id;
                cleanUpUnitIds.Add(unitId);

                var staff = new user
                {
                    email = $"staff_c_{testRunId}@test.com",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                var cust = new user
                {
                    email = $"cust_c_{testRunId}@test.com",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.users.AddRange(staff, cust);
                await seedDb.SaveChangesAsync();
                staffUserId = staff.id;
                customerUserId = cust.id;
                cleanUpUserIds.Add(staff.id);
                cleanUpUserIds.Add(cust.id);

                seedDb.employee_profiles.Add(new employee_profile { user_id = staff.id, employee_code = $"EC_{testRunId}", full_name = "Staff C", employment_status = "active", hire_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)), created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });
                seedDb.staff_facility_assignments.Add(new staff_facility_assignment { employee_id = staff.id, facility_id = facilityId, assignment_role = RoleConstants.FacilityStaff, starts_at = DateTimeOffset.UtcNow.AddMonths(-1), created_at = DateTimeOffset.UtcNow });
                seedDb.customer_profiles.Add(new customer_profile { user_id = cust.id, full_name = "Cust C", identity_number = $"079_C_{testRunId}", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });

                var pol = await seedDb.policy_versions
                    .FirstOrDefaultAsync(p => p.policy_type == "rental_terms" && p.valid_from <= new DateOnly(2026, 1, 1));
                if (pol == null)
                {
                    pol = new policy_version
                    {
                        policy_type = "rental_terms",
                        version = $"2026.1_{testRunId}",
                        content = "{}",
                        valid_from = new DateOnly(2020, 1, 1),
                        created_at = DateTimeOffset.UtcNow
                    };
                    seedDb.policy_versions.Add(pol);
                    await seedDb.SaveChangesAsync();
                }

                var r = new reservation
                {
                    reservation_code = $"RC_{testRunId}",
                    customer_id = cust.id,
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    facility_rate_id = rate.id,
                    start_date = today,
                    end_date = today.AddMonths(1),
                    status = "confirmed",
                    monthly_rate_snapshot = 1000000m,
                    deposit_snapshot = 1000000m,
                    booking_fee_snapshot = 50000m,
                    discount_snapshot = 0m,
                    quoted_total = 2050000m,
                    hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.reservations.Add(r);
                await seedDb.SaveChangesAsync();
                resId = r.id;
                cleanUpReservationIds.Add(resId);

                var a = new rental_agreement
                {
                    agreement_no = $"AC_{testRunId}",
                    reservation_id = resId,
                    customer_id = cust.id,
                    facility_id = facilityId,
                    policy_version_id = pol.id,
                    start_date = today,
                    end_date = today.AddMonths(1),
                    monthly_rate_snapshot = 1000000m,
                    deposit_snapshot = 1000000m,
                    deposit_balance = 1000000m,
                    status = "scheduled",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.rental_agreements.Add(a);
                await seedDb.SaveChangesAsync();

                var inv = new invoice
                {
                    invoice_no = $"IC_{testRunId}",
                    customer_id = cust.id,
                    reservation_id = resId,
                    agreement_id = a.id,
                    issue_date = today,
                    due_date = today.AddDays(7),
                    currency = "VND",
                    subtotal_amount = 2000000m,
                    total_amount = 2000000m,
                    paid_amount = 2000000m,
                    status = "paid",
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.invoices.Add(inv);

                seedDb.identity_verifications.Add(new identity_verification
                {
                    reservation_id = resId,
                    verified_by = staffUserId,
                    verification_method = "government_id",
                    result = "verified",
                    verified_at = DateTimeOffset.UtcNow
                });

                var alloc = new unit_allocation
                {
                    storage_unit_id = unitId,
                    agreement_id = a.id,
                    allocation_start_date = today,
                    allocation_end_date = today.AddMonths(1),
                    allocation_kind = "rental",
                    status = "active",
                    assigned_by = staffUserId,
                    created_at = DateTimeOffset.UtcNow.AddMinutes(-5)
                };
                seedDb.unit_allocations.Add(alloc);
                await seedDb.SaveChangesAsync();
                allocId = alloc.id;

                var insp = new inspection
                {
                    facility_id = facilityId,
                    reservation_id = resId,
                    agreement_id = a.id,
                    storage_unit_id = unitId,
                    inspected_by = staffUserId,
                    inspection_type = "check_in",
                    overall_condition = "good",
                    status = "completed",
                    inspected_at = DateTimeOffset.UtcNow,
                    created_at = DateTimeOffset.UtcNow
                };
                insp.inspection_items.Add(new inspection_item { item_name = "cửa", condition = "good" });
                insp.inspection_items.Add(new inspection_item { item_name = "khóa", condition = "good" });
                insp.inspection_items.Add(new inspection_item { item_name = "đèn", condition = "good" });
                insp.inspection_items.Add(new inspection_item { item_name = "vệ sinh", condition = "good" });
                seedDb.inspections.Add(insp);
                await seedDb.SaveChangesAsync();
                inspId = insp.id;
            }

            var barrier = new Barrier(2);
            const string pinSecret = "TestSecretKeyLongEnoughForHmacSha256!";

            var task1 = Task.Run(async () =>
            {
                await using var db = CreateDbContext(connectionString);
                var repo = new CheckInRepository(db);
                barrier.SignalAndWait();
                try
                {
                    var result = await repo.CompleteCheckInWithLockAsync(
                        resId, allocId, inspId, true, "Notes 1", staffUserId, pinSecret);
                    return (Success: true, IsReplay: result.IsAlreadyCompleted, Error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (Success: false, IsReplay: false, Error: ex.Message);
                }
            });

            var task2 = Task.Run(async () =>
            {
                await using var db = CreateDbContext(connectionString);
                var repo = new CheckInRepository(db);
                barrier.SignalAndWait();
                try
                {
                    var result = await repo.CompleteCheckInWithLockAsync(
                        resId, allocId, inspId, true, "Notes 2", staffUserId, pinSecret);
                    return (Success: true, IsReplay: result.IsAlreadyCompleted, Error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (Success: false, IsReplay: false, Error: ex.Message);
                }
            });

            var results = await Task.WhenAll(task1, task2);

            // One should be initial completion (IsReplay: false) and the other should either be idempotent replay (IsReplay: true) or fail cleanly.
            var completedCount = results.Count(r => r.Success && !r.IsReplay);
            Assert.Equal(1, completedCount);

            // Exactly ONE handover record in database
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var handovers = await verifyDb.handover_records
                    .Where(h => h.agreement.reservation_id == resId)
                    .ToListAsync();
                Assert.Single(handovers);

                var credentials = await verifyDb.access_credentials
                    .Where(c => c.agreement.reservation_id == resId && c.status == "active")
                    .ToListAsync();
                Assert.Single(credentials);

                var notifications = await verifyDb.notifications
                    .Where(n => n.user_id == customerUserId && n.template_code == "HANDOVER_CHECK_IN_COMPLETED")
                    .ToListAsync();
                Assert.Single(notifications);

                var suAfter = await verifyDb.storage_units.FindAsync(unitId);
                Assert.NotNull(suAfter);
                Assert.Equal("occupied", suAfter.physical_status);

                var resAfter = await verifyDb.reservations.FindAsync(resId);
                Assert.NotNull(resAfter);
                Assert.Equal("checked_in", resAfter.status);

                var agrAfter = await verifyDb.rental_agreements.FirstOrDefaultAsync(a => a.reservation_id == resId);
                Assert.NotNull(agrAfter);
                Assert.Equal("active", agrAfter.status);
                Assert.NotNull(agrAfter.checked_in_at);
                Assert.NotNull(agrAfter.signed_at);
            }
        }
        finally
        {
            await using var cleanDb = CreateDbContext(connectionString);
            if (cleanUpReservationIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.access_credentials WHERE agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.handover_records WHERE agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.inspection_items WHERE inspection_id IN (SELECT id FROM core.inspections WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.inspections WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_allocations WHERE storage_unit_id IN ({string.Join(",", cleanUpUnitIds)}) OR agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.invoices WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.identity_verifications WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.reservations WHERE id IN ({string.Join(",", cleanUpReservationIds)})");
            }
            if (cleanUpUnitIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_status_history WHERE storage_unit_id IN ({string.Join(",", cleanUpUnitIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.storage_units WHERE id IN ({string.Join(",", cleanUpUnitIds)})");
            }
            if (cleanUpFacilityRateIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.facility_rates WHERE id IN ({string.Join(",", cleanUpFacilityRateIds)})");
            }
            if (cleanUpPriceRangeIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.price_ranges WHERE id IN ({string.Join(",", cleanUpPriceRangeIds)})");
            }
            if (cleanUpUnitTypeIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_types WHERE id IN ({string.Join(",", cleanUpUnitTypeIds)})");
            }
            if (cleanUpFacilityIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.staff_facility_assignments WHERE facility_id IN ({string.Join(",", cleanUpFacilityIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.facilities WHERE id IN ({string.Join(",", cleanUpFacilityIds)})");
            }
            if (cleanUpUserIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.notifications WHERE user_id IN ({string.Join(",", cleanUpUserIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.employee_profiles WHERE user_id IN ({string.Join(",", cleanUpUserIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.customer_profiles WHERE user_id IN ({string.Join(",", cleanUpUserIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.users WHERE id IN ({string.Join(",", cleanUpUserIds)})");
            }
        }
    }

    [SqlIntegrationFact]
    public async Task CompleteCheckIn_RollbackOnFailure_LeavesNoOrphanRecords()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();
        var cleanUpUnitIds = new List<long>();
        var cleanUpFacilityRateIds = new List<long>();
        var cleanUpPriceRangeIds = new List<long>();

        try
        {
            long facilityId;
            long unitTypeId;
            long unitId;
            long staffUserId;
            long resId;
            long allocId;
            long inspId;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fac = new facility
                {
                    code = $"FC_RB_{testRunId}",
                    name = $"Facility RB {testRunId}",
                    address_line = "123 Rollback St",
                    city = "Ho Chi Minh City",
                    status = "active",
                    timezone = "Asia/Ho_Chi_Minh"
                };
                seedDb.facilities.Add(fac);
                await seedDb.SaveChangesAsync();
                facilityId = fac.id;
                cleanUpFacilityIds.Add(facilityId);

                var ut = new unit_type
                {
                    code = $"UT_RB_{testRunId}",
                    name = $"Type RB {testRunId}",
                    width_m = 2.0m,
                    length_m = 2.0m,
                    height_m = 2.5m,
                    climate_controlled = false,
                    is_active = true
                };
                seedDb.unit_types.Add(ut);
                await seedDb.SaveChangesAsync();
                unitTypeId = ut.id;
                cleanUpUnitTypeIds.Add(unitTypeId);

                var pr = new price_range
                {
                    unit_type_id = unitTypeId,
                    min_monthly_rate = 500_000m,
                    max_monthly_rate = 5_000_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    valid_to = null
                };
                seedDb.price_ranges.Add(pr);
                await seedDb.SaveChangesAsync();
                cleanUpPriceRangeIds.Add(pr.id);

                var rate = new facility_rate
                {
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    monthly_rate = 1_000_000m,
                    deposit_amount = 1_000_000m,
                    booking_fee = 50_000m,
                    valid_from = new DateOnly(2020, 1, 1)
                };
                seedDb.facility_rates.Add(rate);
                await seedDb.SaveChangesAsync();
                cleanUpFacilityRateIds.Add(rate.id);

                var su = new storage_unit
                {
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    unit_code = $"UC_RB_{testRunId}",
                    physical_status = "reserved",
                    is_listed = true,
                    floor_label = "Tang 1"
                };
                seedDb.storage_units.Add(su);
                await seedDb.SaveChangesAsync();
                unitId = su.id;
                cleanUpUnitIds.Add(unitId);

                var staff = new user
                {
                    email = $"staff_rb_{testRunId}@test.com",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                var cust = new user
                {
                    email = $"cust_rb_{testRunId}@test.com",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.users.AddRange(staff, cust);
                await seedDb.SaveChangesAsync();
                staffUserId = staff.id;
                cleanUpUserIds.Add(staff.id);
                cleanUpUserIds.Add(cust.id);

                seedDb.employee_profiles.Add(new employee_profile { user_id = staff.id, employee_code = $"EC_RB_{testRunId}", full_name = "Staff RB", employment_status = "active", hire_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)), created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });
                seedDb.staff_facility_assignments.Add(new staff_facility_assignment { employee_id = staff.id, facility_id = facilityId, assignment_role = RoleConstants.FacilityStaff, starts_at = DateTimeOffset.UtcNow.AddMonths(-1), created_at = DateTimeOffset.UtcNow });
                seedDb.customer_profiles.Add(new customer_profile { user_id = cust.id, full_name = "Cust RB", identity_number = $"079_RB_{testRunId}", created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });

                var pol = await seedDb.policy_versions
                    .FirstOrDefaultAsync(p => p.policy_type == "rental_terms" && p.valid_from <= new DateOnly(2026, 1, 1));
                if (pol == null)
                {
                    pol = new policy_version
                    {
                        policy_type = "rental_terms",
                        version = $"2026.1_rb_{testRunId}",
                        content = "{}",
                        valid_from = new DateOnly(2020, 1, 1),
                        created_at = DateTimeOffset.UtcNow
                    };
                    seedDb.policy_versions.Add(pol);
                    await seedDb.SaveChangesAsync();
                }

                var r = new reservation
                {
                    reservation_code = $"RC_RB_{testRunId}",
                    customer_id = cust.id,
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    facility_rate_id = rate.id,
                    start_date = today,
                    end_date = today.AddMonths(1),
                    status = "confirmed",
                    monthly_rate_snapshot = 1000000m,
                    deposit_snapshot = 1000000m,
                    booking_fee_snapshot = 50000m,
                    discount_snapshot = 0m,
                    quoted_total = 2050000m,
                    hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.reservations.Add(r);
                await seedDb.SaveChangesAsync();
                resId = r.id;
                cleanUpReservationIds.Add(resId);

                var agr = new rental_agreement
                {
                    agreement_no = $"AGR_RB_{testRunId}",
                    reservation_id = resId,
                    customer_id = cust.id,
                    facility_id = facilityId,
                    policy_version_id = pol.id,
                    start_date = today,
                    end_date = today.AddMonths(1),
                    monthly_rate_snapshot = 1000000m,
                    deposit_snapshot = 1000000m,
                    deposit_balance = 1000000m,
                    status = "scheduled",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.rental_agreements.Add(agr);
                await seedDb.SaveChangesAsync();

                var inv = new invoice
                {
                    invoice_no = $"INV_RB_{testRunId}",
                    customer_id = cust.id,
                    reservation_id = resId,
                    agreement_id = agr.id,
                    issue_date = today,
                    due_date = today.AddDays(7),
                    currency = "VND",
                    subtotal_amount = 2000000m,
                    total_amount = 2000000m,
                    paid_amount = 2000000m,
                    status = "paid",
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.invoices.Add(inv);

                seedDb.identity_verifications.Add(new identity_verification
                {
                    reservation_id = resId,
                    verified_by = staffUserId,
                    verification_method = "government_id",
                    result = "verified",
                    verified_at = DateTimeOffset.UtcNow
                });

                var alloc = new unit_allocation
                {
                    storage_unit_id = unitId,
                    agreement_id = agr.id,
                    reservation_id = null,
                    allocation_kind = "rental",
                    allocation_start_date = today,
                    allocation_end_date = today.AddMonths(1),
                    status = "active",
                    assigned_by = staffUserId,
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.unit_allocations.Add(alloc);
                await seedDb.SaveChangesAsync();
                allocId = alloc.id;

                var insp = new inspection
                {
                    facility_id = facilityId,
                    storage_unit_id = unitId,
                    reservation_id = resId,
                    agreement_id = agr.id,
                    inspected_by = staffUserId,
                    inspection_type = "check_in",
                    overall_condition = "good",
                    status = "completed",
                    inspected_at = DateTimeOffset.UtcNow,
                    created_at = DateTimeOffset.UtcNow
                };
                insp.inspection_items.Add(new inspection_item { item_name = "cửa", condition = "good" });
                insp.inspection_items.Add(new inspection_item { item_name = "khóa", condition = "good" });
                insp.inspection_items.Add(new inspection_item { item_name = "đèn", condition = "good" });
                insp.inspection_items.Add(new inspection_item { item_name = "vệ sinh", condition = "good" });
                seedDb.inspections.Add(insp);
                await seedDb.SaveChangesAsync();
                inspId = insp.id;
            }

            // Fault injection: trigger failure during PIN generation with invalid short secret (< 16 bytes)
            await using var db = CreateDbContext(connectionString);
            var repo = new CheckInRepository(db);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repo.CompleteCheckInWithLockAsync(
                    resId, allocId, inspId, true, "Rollback test", staffUserId, "short"));

            // Verify database consistency: transaction was rolled back cleanly!
            await using var verifyDb = CreateDbContext(connectionString);
            var handovers = await verifyDb.handover_records
                .Where(h => h.agreement.reservation_id == resId)
                .ToListAsync();
            Assert.Empty(handovers);

            var credentials = await verifyDb.access_credentials
                .Where(c => c.agreement.reservation_id == resId)
                .ToListAsync();
            Assert.Empty(credentials);

            var notifications = await verifyDb.notifications
                .Where(n => n.template_code == "HANDOVER_CHECK_IN_COMPLETED" && n.payload.Contains($"\"agreementId\":{resId}"))
                .ToListAsync();
            Assert.Empty(notifications);

            var resAfter = await verifyDb.reservations.FindAsync(resId);
            Assert.NotNull(resAfter);
            Assert.Equal("confirmed", resAfter.status);

            var agrAfter = await verifyDb.rental_agreements.FirstOrDefaultAsync(a => a.reservation_id == resId);
            Assert.NotNull(agrAfter);
            Assert.Equal("scheduled", agrAfter.status);

            var suAfter = await verifyDb.storage_units.FindAsync(unitId);
            Assert.NotNull(suAfter);
            Assert.Equal("reserved", suAfter.physical_status);
        }
        finally
        {
            await using var cleanDb = CreateDbContext(connectionString);
            if (cleanUpReservationIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.access_credentials WHERE agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.handover_records WHERE agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.inspection_items WHERE inspection_id IN (SELECT id FROM core.inspections WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.inspections WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_allocations WHERE storage_unit_id IN ({string.Join(",", cleanUpUnitIds)}) OR agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.invoices WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.identity_verifications WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.reservations WHERE id IN ({string.Join(",", cleanUpReservationIds)})");
            }
            if (cleanUpUnitIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_status_history WHERE storage_unit_id IN ({string.Join(",", cleanUpUnitIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.storage_units WHERE id IN ({string.Join(",", cleanUpUnitIds)})");
            }
            if (cleanUpFacilityRateIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.facility_rates WHERE id IN ({string.Join(",", cleanUpFacilityRateIds)})");
            }
            if (cleanUpPriceRangeIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.price_ranges WHERE id IN ({string.Join(",", cleanUpPriceRangeIds)})");
            }
            if (cleanUpUnitTypeIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_types WHERE id IN ({string.Join(",", cleanUpUnitTypeIds)})");
            }
            if (cleanUpFacilityIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.staff_facility_assignments WHERE facility_id IN ({string.Join(",", cleanUpFacilityIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.facilities WHERE id IN ({string.Join(",", cleanUpFacilityIds)})");
            }
            if (cleanUpUserIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.notifications WHERE user_id IN ({string.Join(",", cleanUpUserIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.employee_profiles WHERE user_id IN ({string.Join(",", cleanUpUserIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.customer_profiles WHERE user_id IN ({string.Join(",", cleanUpUserIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.users WHERE id IN ({string.Join(",", cleanUpUserIds)})");
            }
        }
    }

    [SqlIntegrationFact]
    public async Task ReissuePin_PersistsRealCredentialIdInAuditLog_OnSqlServer()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();
        var cleanUpUnitIds = new List<long>();
        var cleanUpFacilityRateIds = new List<long>();
        var cleanUpPriceRangeIds = new List<long>();
        var cleanUpAuditLogEntityIds = new List<string>();

        try
        {
            long facilityId;
            long unitTypeId;
            long unitId;
            long staffUserId;
            long resId;
            long allocId;
            long inspId;
            long initialCredId;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            const string pinSecret = "TestSecretKeyLongEnoughForHmacSha256!";
            var docNumber = $"079_RE_{testRunId}";

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fac = new facility
                {
                    code = $"FC_RE_{testRunId}",
                    name = $"Facility RE {testRunId}",
                    address_line = "123 Reissue St",
                    city = "Ho Chi Minh City",
                    status = "active",
                    timezone = "Asia/Ho_Chi_Minh"
                };
                seedDb.facilities.Add(fac);
                await seedDb.SaveChangesAsync();
                facilityId = fac.id;
                cleanUpFacilityIds.Add(facilityId);

                var ut = new unit_type
                {
                    code = $"UT_RE_{testRunId}",
                    name = $"Type RE {testRunId}",
                    width_m = 2.0m,
                    length_m = 2.0m,
                    height_m = 2.5m,
                    climate_controlled = false,
                    is_active = true
                };
                seedDb.unit_types.Add(ut);
                await seedDb.SaveChangesAsync();
                unitTypeId = ut.id;
                cleanUpUnitTypeIds.Add(unitTypeId);

                var pr = new price_range
                {
                    unit_type_id = unitTypeId,
                    min_monthly_rate = 500_000m,
                    max_monthly_rate = 5_000_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    valid_to = null
                };
                seedDb.price_ranges.Add(pr);
                await seedDb.SaveChangesAsync();
                cleanUpPriceRangeIds.Add(pr.id);

                var rate = new facility_rate
                {
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    monthly_rate = 1_000_000m,
                    deposit_amount = 1_000_000m,
                    booking_fee = 50_000m,
                    valid_from = new DateOnly(2020, 1, 1)
                };
                seedDb.facility_rates.Add(rate);
                await seedDb.SaveChangesAsync();
                cleanUpFacilityRateIds.Add(rate.id);

                var su = new storage_unit
                {
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    unit_code = $"UC_RE_{testRunId}",
                    physical_status = "reserved",
                    is_listed = true,
                    floor_label = "Tang 1"
                };
                seedDb.storage_units.Add(su);
                await seedDb.SaveChangesAsync();
                unitId = su.id;
                cleanUpUnitIds.Add(unitId);

                var staff = new user
                {
                    email = $"staff_re_{testRunId}@test.com",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                var cust = new user
                {
                    email = $"cust_re_{testRunId}@test.com",
                    password_hash = "hash",
                    status = UserStatusConstants.Active,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.users.AddRange(staff, cust);
                await seedDb.SaveChangesAsync();
                staffUserId = staff.id;
                cleanUpUserIds.Add(staff.id);
                cleanUpUserIds.Add(cust.id);

                seedDb.employee_profiles.Add(new employee_profile { user_id = staff.id, employee_code = $"EC_RE_{testRunId}", full_name = "Staff RE", employment_status = "active", hire_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)), created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });
                seedDb.staff_facility_assignments.Add(new staff_facility_assignment { employee_id = staff.id, facility_id = facilityId, assignment_role = RoleConstants.FacilityStaff, starts_at = DateTimeOffset.UtcNow.AddMonths(-1), created_at = DateTimeOffset.UtcNow });
                seedDb.customer_profiles.Add(new customer_profile { user_id = cust.id, full_name = "Cust RE", identity_number = docNumber, created_at = DateTimeOffset.UtcNow, updated_at = DateTimeOffset.UtcNow });

                var pol = await seedDb.policy_versions
                    .FirstOrDefaultAsync(p => p.policy_type == "rental_terms" && p.valid_from <= new DateOnly(2026, 1, 1));
                if (pol == null)
                {
                    pol = new policy_version
                    {
                        policy_type = "rental_terms",
                        version = $"2026.1_re_{testRunId}",
                        content = "{}",
                        valid_from = new DateOnly(2020, 1, 1),
                        created_at = DateTimeOffset.UtcNow
                    };
                    seedDb.policy_versions.Add(pol);
                    await seedDb.SaveChangesAsync();
                }

                var r = new reservation
                {
                    reservation_code = $"RC_RE_{testRunId}",
                    customer_id = cust.id,
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    facility_rate_id = rate.id,
                    start_date = today,
                    end_date = today.AddMonths(1),
                    status = "confirmed",
                    monthly_rate_snapshot = 1000000m,
                    deposit_snapshot = 1000000m,
                    booking_fee_snapshot = 50000m,
                    discount_snapshot = 0m,
                    quoted_total = 2050000m,
                    hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.reservations.Add(r);
                await seedDb.SaveChangesAsync();
                resId = r.id;
                cleanUpReservationIds.Add(resId);

                var agr = new rental_agreement
                {
                    agreement_no = $"AGR_RE_{testRunId}",
                    reservation_id = resId,
                    customer_id = cust.id,
                    facility_id = facilityId,
                    policy_version_id = pol.id,
                    start_date = today,
                    end_date = today.AddMonths(1),
                    monthly_rate_snapshot = 1000000m,
                    deposit_snapshot = 1000000m,
                    deposit_balance = 1000000m,
                    status = "scheduled",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.rental_agreements.Add(agr);
                await seedDb.SaveChangesAsync();

                var inv = new invoice
                {
                    invoice_no = $"INV_RE_{testRunId}",
                    customer_id = cust.id,
                    reservation_id = resId,
                    agreement_id = agr.id,
                    issue_date = today,
                    due_date = today.AddDays(7),
                    currency = "VND",
                    subtotal_amount = 2000000m,
                    total_amount = 2000000m,
                    paid_amount = 2000000m,
                    status = "paid",
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.invoices.Add(inv);

                seedDb.identity_verifications.Add(new identity_verification
                {
                    reservation_id = resId,
                    verified_by = staffUserId,
                    verification_method = "government_id",
                    result = "verified",
                    verified_at = DateTimeOffset.UtcNow
                });

                var alloc = new unit_allocation
                {
                    storage_unit_id = unitId,
                    agreement_id = agr.id,
                    reservation_id = null,
                    allocation_kind = "rental",
                    allocation_start_date = today,
                    allocation_end_date = today.AddMonths(1),
                    status = "active",
                    assigned_by = staffUserId,
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.unit_allocations.Add(alloc);
                await seedDb.SaveChangesAsync();
                allocId = alloc.id;

                var insp = new inspection
                {
                    facility_id = facilityId,
                    storage_unit_id = unitId,
                    reservation_id = resId,
                    agreement_id = agr.id,
                    inspected_by = staffUserId,
                    inspection_type = "check_in",
                    overall_condition = "good",
                    status = "completed",
                    inspected_at = DateTimeOffset.UtcNow,
                    created_at = DateTimeOffset.UtcNow
                };
                insp.inspection_items.Add(new inspection_item { item_name = "cửa", condition = "good" });
                insp.inspection_items.Add(new inspection_item { item_name = "khóa", condition = "good" });
                insp.inspection_items.Add(new inspection_item { item_name = "đèn", condition = "good" });
                insp.inspection_items.Add(new inspection_item { item_name = "vệ sinh", condition = "good" });
                seedDb.inspections.Add(insp);
                await seedDb.SaveChangesAsync();
                inspId = insp.id;
            }

            // Step 1: Complete check-in successfully
            await using (var db = CreateDbContext(connectionString))
            {
                var repo = new CheckInRepository(db);
                var complete = await repo.CompleteCheckInWithLockAsync(
                    resId, allocId, inspId, true, "Initial complete", staffUserId, pinSecret);
                Assert.NotNull(complete.CredentialId);
                initialCredId = complete.CredentialId.Value;
            }

            // Step 2: Reissue PIN on SQL Server
            long newCredId;
            await using (var db = CreateDbContext(connectionString))
            {
                var repo = new CheckInRepository(db);
                var reissue = await repo.ReissuePinWithLockAsync(
                    resId, initialCredId, docNumber, true, "Customer forgot PIN", staffUserId, pinSecret);

                Assert.Equal(initialCredId, reissue.OldCredentialId);
                Assert.True(reissue.NewCredentialId > 0);
                Assert.NotEqual(initialCredId, reissue.NewCredentialId);
                newCredId = reissue.NewCredentialId;
                cleanUpAuditLogEntityIds.Add(newCredId.ToString());
            }

            // Step 3: Verify SQL Server database records
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                // Verify credentials in DB
                var oldCred = await verifyDb.access_credentials.FindAsync(initialCredId);
                Assert.NotNull(oldCred);
                Assert.Equal("revoked", oldCred.status);

                var newCred = await verifyDb.access_credentials.FindAsync(newCredId);
                Assert.NotNull(newCred);
                Assert.Equal("active", newCred.status);

                // Verify audit log has REAL identity ID (not "0")
                var auditLog = await verifyDb.audit_logs
                    .FirstOrDefaultAsync(a => a.action == "REISSUE_PIN" && a.entity_id == newCredId.ToString());

                Assert.NotNull(auditLog);
                Assert.Equal(newCredId.ToString(), auditLog.entity_id);
                Assert.NotEqual("0", auditLog.entity_id);
                Assert.Equal("facility_staff", auditLog.actor_role);
                Assert.Equal("access_credential", auditLog.entity_type);
                Assert.Contains(newCredId.ToString(), auditLog.new_values);
            }
        }
        finally
        {
            await using var cleanDb = CreateDbContext(connectionString);
            if (cleanUpAuditLogEntityIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.audit_logs WHERE action = 'REISSUE_PIN' AND entity_id IN ({string.Join(",", cleanUpAuditLogEntityIds.Select(id => $"'{id}'"))})");
            }
            if (cleanUpReservationIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.access_credentials WHERE agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.handover_records WHERE agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.inspection_items WHERE inspection_id IN (SELECT id FROM core.inspections WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.inspections WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_allocations WHERE storage_unit_id IN ({string.Join(",", cleanUpUnitIds)}) OR agreement_id IN (SELECT id FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)}))");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.invoices WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.rental_agreements WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.identity_verifications WHERE reservation_id IN ({string.Join(",", cleanUpReservationIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.reservations WHERE id IN ({string.Join(",", cleanUpReservationIds)})");
            }
            if (cleanUpUnitIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_status_history WHERE storage_unit_id IN ({string.Join(",", cleanUpUnitIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.storage_units WHERE id IN ({string.Join(",", cleanUpUnitIds)})");
            }
            if (cleanUpFacilityRateIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.facility_rates WHERE id IN ({string.Join(",", cleanUpFacilityRateIds)})");
            }
            if (cleanUpPriceRangeIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.price_ranges WHERE id IN ({string.Join(",", cleanUpPriceRangeIds)})");
            }
            if (cleanUpUnitTypeIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.unit_types WHERE id IN ({string.Join(",", cleanUpUnitTypeIds)})");
            }
            if (cleanUpFacilityIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.staff_facility_assignments WHERE facility_id IN ({string.Join(",", cleanUpFacilityIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.facilities WHERE id IN ({string.Join(",", cleanUpFacilityIds)})");
            }
            if (cleanUpUserIds.Count > 0)
            {
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.notifications WHERE user_id IN ({string.Join(",", cleanUpUserIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.employee_profiles WHERE user_id IN ({string.Join(",", cleanUpUserIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.customer_profiles WHERE user_id IN ({string.Join(",", cleanUpUserIds)})");
                await cleanDb.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM core.users WHERE id IN ({string.Join(",", cleanUpUserIds)})");
            }
        }
    }
}
