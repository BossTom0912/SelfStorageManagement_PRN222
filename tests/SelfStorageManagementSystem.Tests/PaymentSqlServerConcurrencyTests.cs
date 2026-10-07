using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class PaymentSqlServerConcurrencyTests
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
    public async Task ConcurrentCheckout_SameReservation_DifferentIdempotencyKeys_ExactlyOneSucceeds()
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

        try
        {
            long customerUserId = 0;
            long facilityId = 0;
            long unitTypeId = 0;
            long facilityRateId = 0;
            long policyVersionId = 0;
            long reservationId = 0;

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fac = new facility
                {
                    code = $"FC_{testRunId}",
                    name = $"Facility {testRunId}",
                    address_line = "123 Concurrency Ave",
                    city = "Hanoi",
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
                    area_m2 = 5.0m,
                    climate_controlled = false,
                    is_active = true
                };
                seedDb.unit_types.Add(ut);
                await seedDb.SaveChangesAsync();
                unitTypeId = ut.id;
                cleanUpUnitTypeIds.Add(unitTypeId);

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
                facilityRateId = rate.id;

                var user = new user
                {
                    email = $"user_{testRunId}@test.local",
                    password_hash = "hash",
                    status = "active",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.users.Add(user);
                await seedDb.SaveChangesAsync();
                customerUserId = user.id;
                cleanUpUserIds.Add(customerUserId);

                seedDb.customer_profiles.Add(new customer_profile
                {
                    user_id = customerUserId,
                    full_name = $"Customer {testRunId}",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                });

                var policy = await seedDb.policy_versions
                    .FirstOrDefaultAsync(p => p.policy_type == "rental_terms" && p.valid_from <= new DateOnly(2026, 1, 1));
                if (policy == null)
                {
                    policy = new policy_version
                    {
                        policy_type = "rental_terms",
                        version = $"2026.1_{testRunId}",
                        content = "{}",
                        valid_from = new DateOnly(2020, 1, 1),
                        created_at = DateTimeOffset.UtcNow
                    };
                    seedDb.policy_versions.Add(policy);
                    await seedDb.SaveChangesAsync();
                }
                policyVersionId = policy.id;

                var res = new reservation
                {
                    reservation_code = $"RES_{testRunId}",
                    customer_id = customerUserId,
                    facility_id = facilityId,
                    unit_type_id = unitTypeId,
                    facility_rate_id = facilityRateId,
                    start_date = new DateOnly(2026, 11, 1),
                    end_date = new DateOnly(2027, 2, 1),
                    monthly_rate_snapshot = 1_000_000m,
                    deposit_snapshot = 1_000_000m,
                    booking_fee_snapshot = 50_000m,
                    discount_snapshot = 0m,
                    quoted_total = 2_050_000m,
                    status = "pending",
                    hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.reservations.Add(res);
                await seedDb.SaveChangesAsync();
                reservationId = res.id;
                cleanUpReservationIds.Add(reservationId);
            }

            // Race 2 checkout transactions with different idempotency keys simultaneously
            var task1 = Task.Run(async () =>
            {
                await using var db = CreateDbContext(connectionString);
                var repo = new PaymentRepository(db);
                try
                {
                    var result = await repo.ExecuteCheckoutTransactionAsync(new CheckoutTransactionParams
                    {
                        ReservationId = reservationId,
                        CustomerId = customerUserId,
                        PolicyVersionId = policyVersionId,
                        PolicyVersionNumber = "2026.1",
                        IdempotencyKey = $"IDEM_1_{testRunId}",
                        PaymentMethod = "demo",
                        PaymentProvider = "demo",
                        ComputedDeposit = 1_000_000m,
                        ComputedRent = 1_000_000m,
                        ComputedBookingFee = 50_000m,
                        ComputedDiscount = 0m,
                        ComputedTotal = 2_050_000m,
                        NowUtc = DateTimeOffset.UtcNow,
                        ClientIp = "127.0.0.1",
                        EvidenceMetadataJson = "{}"
                    });
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
                var repo = new PaymentRepository(db);
                try
                {
                    var result = await repo.ExecuteCheckoutTransactionAsync(new CheckoutTransactionParams
                    {
                        ReservationId = reservationId,
                        CustomerId = customerUserId,
                        PolicyVersionId = policyVersionId,
                        PolicyVersionNumber = "2026.1",
                        IdempotencyKey = $"IDEM_2_{testRunId}",
                        PaymentMethod = "demo",
                        PaymentProvider = "demo",
                        ComputedDeposit = 1_000_000m,
                        ComputedRent = 1_000_000m,
                        ComputedBookingFee = 50_000m,
                        ComputedDiscount = 0m,
                        ComputedTotal = 2_050_000m,
                        NowUtc = DateTimeOffset.UtcNow,
                        ClientIp = "127.0.0.1",
                        EvidenceMetadataJson = "{}"
                    });
                    return (Success: true, Error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (Success: false, Error: ex.Message);
                }
            });

            var results = await Task.WhenAll(task1, task2);
            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count(r => !r.Success);

            // Exactly 1 checkout attempt must succeed, and 1 must fail due to lock or already pending attempt
            Assert.Equal(1, successCount);
            Assert.Equal(1, failureCount);
        }
        finally
        {
            await using var cleanDb = CreateDbContext(connectionString);
            foreach (var rId in cleanUpReservationIds)
            {
                var payments = await cleanDb.payments.Where(p => p.target_invoice.reservation_id == rId).ToListAsync();
                cleanDb.payments.RemoveRange(payments);
                var invs = await cleanDb.invoices.Where(i => i.reservation_id == rId).ToListAsync();
                cleanDb.invoices.RemoveRange(invs);
                var res = await cleanDb.reservations.FindAsync(rId);
                if (res != null) cleanDb.reservations.Remove(res);
            }
            foreach (var uId in cleanUpUserIds)
            {
                var prof = await cleanDb.customer_profiles.FindAsync(uId);
                if (prof != null) cleanDb.customer_profiles.Remove(prof);
                var u = await cleanDb.users.FindAsync(uId);
                if (u != null) cleanDb.users.Remove(u);
            }
            foreach (var fId in cleanUpFacilityIds)
            {
                var rates = await cleanDb.facility_rates.Where(r => r.facility_id == fId).ToListAsync();
                cleanDb.facility_rates.RemoveRange(rates);
                var fac = await cleanDb.facilities.FindAsync(fId);
                if (fac != null) cleanDb.facilities.Remove(fac);
            }
            foreach (var utId in cleanUpUnitTypeIds)
            {
                var ut = await cleanDb.unit_types.FindAsync(utId);
                if (ut != null) cleanDb.unit_types.Remove(ut);
            }
            await cleanDb.SaveChangesAsync();
        }
    }
}
