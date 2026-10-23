using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

[Collection("SqlServerConcurrency")]
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
                    width_m = 2.0m,
                    length_m = 2.5m,
                    height_m = 2.5m,
                    area_m2 = 5.0m,
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
                    max_monthly_rate = 2_000_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.price_ranges.Add(pr);
                await seedDb.SaveChangesAsync();

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
                        PaymentMethod = "other",
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
                        PaymentMethod = "other",
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
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
        }
    }

    [SqlIntegrationFact]
    public async Task ConcurrentCheckoutRetry_AndLateIpnOnOldAttempt_RoutesOldPaymentToReconciliation()
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
            long payment1Id = 0;
            long invoice1Id = 0;

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fac = new facility
                {
                    code = $"FC_{testRunId}",
                    name = $"Facility {testRunId}",
                    address_line = "123 Retry Ave",
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
                    width_m = 2.0m,
                    length_m = 2.5m,
                    height_m = 2.5m,
                    area_m2 = 5.0m,
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
                    max_monthly_rate = 2_000_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.price_ranges.Add(pr);
                await seedDb.SaveChangesAsync();

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

                var repo = new PaymentRepository(seedDb);
                var checkout1 = await repo.ExecuteCheckoutTransactionAsync(new CheckoutTransactionParams
                {
                    ReservationId = reservationId,
                    CustomerId = customerUserId,
                    PolicyVersionId = policyVersionId,
                    PolicyVersionNumber = "2026.1",
                    IdempotencyKey = $"IDEM_1_{testRunId}",
                    PaymentMethod = "vnpay",
                    PaymentProvider = "vnpay",
                    ComputedDeposit = 1_000_000m,
                    ComputedRent = 1_000_000m,
                    ComputedBookingFee = 50_000m,
                    ComputedDiscount = 0m,
                    ComputedTotal = 2_050_000m,
                    NowUtc = DateTimeOffset.UtcNow,
                    ClientIp = "127.0.0.1",
                    EvidenceMetadataJson = "{}"
                });
                payment1Id = checkout1.Payment.id;
                invoice1Id = checkout1.Invoice.id;
            }

            // Concurrency:
            // Task A: Checkout retry voids invoice 1
            await using (var dbA = CreateDbContext(connectionString))
            {
                var inv = await dbA.invoices.FindAsync(invoice1Id);
                if (inv != null)
                {
                    inv.status = "voided";
                    inv.updated_at = DateTimeOffset.UtcNow;
                    await dbA.SaveChangesAsync();
                }
            }

            // Task B: Late IPN on payment 1 arrives concurrently/subsequently
            await using var dbB = CreateDbContext(connectionString);
            var repoB = new PaymentRepository(dbB);
            var finalizeResult = await repoB.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
            {
                PaymentId = payment1Id,
                Provider = "vnpay",
                ProviderTransactionId = $"TXN_{testRunId}_OLD",
                Amount = 2_050_000m,
                ExternalEventId = $"EVT_{testRunId}_OLD",
                RawPayloadJson = "{}",
                PaidAt = DateTimeOffset.UtcNow
            });

            Assert.True(finalizeResult.ReconciliationRequired);
            Assert.NotNull(finalizeResult.ReconciliationReason);
            Assert.Null(finalizeResult.Agreement);

            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var p1 = await verifyDb.payments.FindAsync(payment1Id);
                Assert.NotNull(p1);
                Assert.Equal("INVOICE_VOIDED", p1.failure_reason);

                var inv1 = await verifyDb.invoices.FindAsync(invoice1Id);
                Assert.NotNull(inv1);
                Assert.Equal("voided", inv1.status);
                Assert.Equal(0m, inv1.paid_amount);

                var refund = await verifyDb.refunds.FirstOrDefaultAsync(r => r.payment_id == payment1Id);
                Assert.NotNull(refund);
                Assert.Equal("requested", refund.status);
            }
        }
        finally
        {
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
        }
    }

    [SqlIntegrationFact]
    public async Task ConcurrentRefundReview_SameRefund_ExactlyOneSucceedsAndOneFailsWithControlledConflict()
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
            long staffUserId = 0;
            long facilityId = 0;
            long unitTypeId = 0;
            long facilityRateId = 0;
            long policyVersionId = 0;
            long reservationId = 0;
            long paymentId = 0;
            long refundId = 0;

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fac = new facility
                {
                    code = $"FC_{testRunId}",
                    name = $"Facility {testRunId}",
                    address_line = "456 Refund Ave",
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
                    width_m = 2.0m,
                    length_m = 2.5m,
                    height_m = 2.5m,
                    area_m2 = 5.0m,
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
                    max_monthly_rate = 2_000_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.price_ranges.Add(pr);
                await seedDb.SaveChangesAsync();

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

                var custUser = new user
                {
                    email = $"cust_{testRunId}@test.local",
                    password_hash = "hash",
                    status = "active",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.users.Add(custUser);

                var staffUser = new user
                {
                    email = $"staff_{testRunId}@test.local",
                    password_hash = "hash",
                    status = "active",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.users.Add(staffUser);
                await seedDb.SaveChangesAsync();

                customerUserId = custUser.id;
                staffUserId = staffUser.id;
                cleanUpUserIds.Add(customerUserId);
                cleanUpUserIds.Add(staffUserId);

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
                    status = "confirmed",
                    hold_until = DateTimeOffset.UtcNow.AddMinutes(15),
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.reservations.Add(res);
                await seedDb.SaveChangesAsync();
                reservationId = res.id;
                cleanUpReservationIds.Add(reservationId);

                var inv = new invoice
                {
                    reservation_id = reservationId,
                    customer_id = customerUserId,
                    invoice_no = $"INV_{testRunId}",
                    status = "paid",
                    currency = "VND",
                    subtotal_amount = 2_050_000m,
                    discount_amount = 0m,
                    total_amount = 2_050_000m,
                    paid_amount = 2_050_000m,
                    issue_date = new DateOnly(2026, 11, 1),
                    due_date = new DateOnly(2026, 11, 1),
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.invoices.Add(inv);
                await seedDb.SaveChangesAsync();

                var p = new payment
                {
                    target_invoice_id = inv.id,
                    customer_id = customerUserId,
                    amount = 2_050_000m,
                    currency = "VND",
                    method = "vnpay",
                    provider = "vnpay",
                    status = "succeeded",
                    idempotency_key = $"PAY_REF_{testRunId}",
                    paid_at = DateTimeOffset.UtcNow,
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.payments.Add(p);
                await seedDb.SaveChangesAsync();
                paymentId = p.id;

                var rf = new refund
                {
                    payment_id = paymentId,
                    amount = 500_000m,
                    currency = "VND",
                    reason = "Overpayment refund request",
                    provider = "vnpay",
                    idempotency_key = $"REFUND_{testRunId}",
                    status = "requested",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                seedDb.refunds.Add(rf);
                await seedDb.SaveChangesAsync();
                refundId = rf.id;
            }

            // Race 2 employees reviewing the same refund simultaneously
            var task1 = Task.Run(async () =>
            {
                await using var db1 = CreateDbContext(connectionString);
                var repo1 = new PaymentRepository(db1);
                try
                {
                    var result = await repo1.ReviewRefundAsync(
                        refundId,
                        staffUserId,
                        decision: "approved",
                        reason: "Staff 1 approved",
                        decidedAt: DateTimeOffset.UtcNow);
                    return (Success: true, Error: (string?)null);
                }
                catch (Exception ex)
                {
                    return (Success: false, Error: ex.Message);
                }
            });

            var task2 = Task.Run(async () =>
            {
                await using var db2 = CreateDbContext(connectionString);
                var repo2 = new PaymentRepository(db2);
                try
                {
                    var result = await repo2.ReviewRefundAsync(
                        refundId,
                        staffUserId,
                        decision: "rejected",
                        reason: "Staff 2 rejected",
                        decidedAt: DateTimeOffset.UtcNow);
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

            Assert.Equal(1, successCount);
            Assert.Equal(1, failureCount);

            var failure = results.First(r => !r.Success);
            Assert.Contains("REFUND_ALREADY_DECIDED", failure.Error);

            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var approvals = await verifyDb.refund_approvals.Where(ra => ra.refund_id == refundId).ToListAsync();
                Assert.Single(approvals);

                var finalRefund = await verifyDb.refunds.FindAsync(refundId);
                Assert.NotNull(finalRefund);
                Assert.True(finalRefund.status == "approved" || finalRefund.status == "rejected");
            }
        }
        finally
        {
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
        }
    }

    [SqlIntegrationFact]
    public async Task ConcurrentIpn_DeclineThenSuccess_SameTransactionId_ReverseRaceRoutesToReconciliation_AndRetryIsIdempotent_OnSqlServer()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();

        try
        {
            long paymentId;
            long invoiceId;
            long reservationId;

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fixture = await SeedCheckoutFixtureAsync(seedDb, testRunId);
                cleanUpUserIds.Add(fixture.CustomerUserId);
                cleanUpFacilityIds.Add(fixture.FacilityId);
                cleanUpUnitTypeIds.Add(fixture.UnitTypeId);
                cleanUpReservationIds.Add(fixture.ReservationId);
                paymentId = fixture.PaymentId;
                invoiceId = fixture.InvoiceId;
                reservationId = fixture.ReservationId;
            }

            // Step 1: Decline callback arrives first (Gateway declines the payment)
            await using (var declineDb = CreateDbContext(connectionString))
            {
                var declineRepo = new PaymentRepository(declineDb);
                var failedResult = await declineRepo.ExecuteFinalizeFailedPaymentTransactionAsync(new FinalizeFailedPaymentParams
                {
                    PaymentId = paymentId,
                    Provider = "vnpay",
                    ExternalEventId = $"EVT_DECLINE_{testRunId}",
                    FailureReason = "VNPAY payment declined by user or bank.",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"24\"}",
                    FailedAt = DateTimeOffset.UtcNow
                });

                Assert.True(failedResult.IsTransitionCommitted);
            }

            // Verify intermediate durable state after restart (fresh DbContext)
            await using (var verifyDeclineDb = CreateDbContext(connectionString))
            {
                var paymentAfterDecline = await verifyDeclineDb.payments.FindAsync(paymentId);
                Assert.NotNull(paymentAfterDecline);
                Assert.Equal("failed", paymentAfterDecline.status);
                Assert.Equal("VNPAY payment declined by user or bank.", paymentAfterDecline.failure_reason);

                var agreementsCount = await verifyDeclineDb.rental_agreements.CountAsync(a => a.reservation_id == reservationId);
                Assert.Equal(0, agreementsCount);
            }

            // Step 2: Late Success callback arrives with the same payment ID / TxnRef (Reverse Reconciliation T15)
            FinalizePaymentResult lateSuccessResult;
            await using (var successDb = CreateDbContext(connectionString))
            {
                var successRepo = new PaymentRepository(successDb);
                lateSuccessResult = await successRepo.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
                {
                    PaymentId = paymentId,
                    Provider = "vnpay",
                    ProviderTransactionId = $"TXN_VNPAY_{testRunId}",
                    Amount = 2_050_000m,
                    ExternalEventId = $"EVT_SUCCESS_{testRunId}",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"00\"}",
                    PaidAt = DateTimeOffset.UtcNow
                });
            }

            Assert.True(lateSuccessResult.ReconciliationRequired);
            Assert.False(lateSuccessResult.IsAlreadyProcessed);
            Assert.Null(lateSuccessResult.Agreement);

            // Verify durable database state after late success:
            // 1. Payment status is succeeded with failure_reason = PAYMENT_ALREADY_FAILED
            // 2. Reconciliation is required in metadata
            // 3. Exactly 1 refund request is created
            // 4. Zero rental agreements are created
            // 5. Reservation is NOT confirmed
            // 6. Invoice has 0 paid_amount
            await using (var verifyLateDb = CreateDbContext(connectionString))
            {
                var paymentAfterLate = await verifyLateDb.payments.FindAsync(paymentId);
                Assert.NotNull(paymentAfterLate);
                Assert.Equal("succeeded", paymentAfterLate.status);
                Assert.Equal("PAYMENT_ALREADY_FAILED", paymentAfterLate.failure_reason);
                Assert.Contains("\"reconciliation_required\":true", paymentAfterLate.metadata ?? "");

                var refunds = await verifyLateDb.refunds.Where(r => r.payment_id == paymentId).ToListAsync();
                Assert.Single(refunds);
                Assert.Equal("requested", refunds[0].status);
                Assert.Equal(2_050_000m, refunds[0].amount);

                var agreementsCount = await verifyLateDb.rental_agreements.CountAsync(a => a.reservation_id == reservationId);
                Assert.Equal(0, agreementsCount);

                var res = await verifyLateDb.reservations.FindAsync(reservationId);
                Assert.NotNull(res);
                Assert.NotEqual("confirmed", res.status);

                var inv = await verifyLateDb.invoices.FindAsync(invoiceId);
                Assert.NotNull(inv);
                Assert.Equal(0m, inv.paid_amount);
            }

            // Step 3: Success retry callback arrives again (duplicate IPN retry from provider with same transaction ID)
            FinalizePaymentResult retryResult;
            await using (var retryDb = CreateDbContext(connectionString))
            {
                var retryRepo = new PaymentRepository(retryDb);
                retryResult = await retryRepo.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
                {
                    PaymentId = paymentId,
                    Provider = "vnpay",
                    ProviderTransactionId = $"TXN_VNPAY_{testRunId}",
                    Amount = 2_050_000m,
                    ExternalEventId = $"EVT_SUCCESS_{testRunId}",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"00\"}",
                    PaidAt = DateTimeOffset.UtcNow
                });
            }

            Assert.True(retryResult.IsAlreadyProcessed);
            Assert.True(retryResult.ReconciliationRequired);

            // Verify retry didn't produce duplicate records in DB
            await using (var verifyRetryDb = CreateDbContext(connectionString))
            {
                var totalRefunds = await verifyRetryDb.refunds.CountAsync(r => r.payment_id == paymentId);
                Assert.Equal(1, totalRefunds);

                var totalAgreements = await verifyRetryDb.rental_agreements.CountAsync(a => a.reservation_id == reservationId);
                Assert.Equal(0, totalAgreements);

                var totalEvents = await verifyRetryDb.integration_events.CountAsync(e => e.external_event_id == $"EVT_SUCCESS_{testRunId}");
                Assert.Equal(1, totalEvents);
            }
        }
        finally
        {
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
        }
    }

    [SqlIntegrationFact]
    public async Task PaymentFinalize_NormalSuccessFromPendingCheckout_CreatesAgreementAndInvoicePaid_DurableOnSqlServer()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();

        try
        {
            long paymentId;
            long invoiceId;
            long reservationId;

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fixture = await SeedCheckoutFixtureAsync(seedDb, testRunId);
                cleanUpUserIds.Add(fixture.CustomerUserId);
                cleanUpFacilityIds.Add(fixture.FacilityId);
                cleanUpUnitTypeIds.Add(fixture.UnitTypeId);
                cleanUpReservationIds.Add(fixture.ReservationId);
                paymentId = fixture.PaymentId;
                invoiceId = fixture.InvoiceId;
                reservationId = fixture.ReservationId;
            }

            // Normal successful payment finalization via ExecuteFinalizePaymentTransactionAsync
            FinalizePaymentResult result;
            var paidAtUtc = DateTimeOffset.UtcNow;
            await using (var finalizeDb = CreateDbContext(connectionString))
            {
                var repo = new PaymentRepository(finalizeDb);
                result = await repo.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
                {
                    PaymentId = paymentId,
                    Amount = 2_050_000m,
                    PaidAt = paidAtUtc,
                    Provider = "vnpay",
                    ProviderTransactionId = $"TXN_NORM_{testRunId}",
                    ExternalEventId = $"EVT_NORM_{testRunId}",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"00\",\"vnp_TransactionNo\":\"123456\"}"
                });
            }

            Assert.False(result.IsAlreadyProcessed);
            Assert.False(result.ReconciliationRequired);
            Assert.Null(result.ReconciliationReason);
            Assert.NotNull(result.Payment);
            Assert.Equal("succeeded", result.Payment.status);
            Assert.NotNull(result.Invoice);
            Assert.Equal("paid", result.Invoice.status);
            Assert.Equal(2_050_000m, result.Invoice.paid_amount);
            Assert.NotNull(result.Reservation);
            Assert.Equal("confirmed", result.Reservation.status);
            Assert.NotNull(result.Agreement);
            Assert.Equal("scheduled", result.Agreement.status);

            // Fresh DbContext verification: Durable state after commit
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var dbPayment = await verifyDb.payments.FindAsync(paymentId);
                Assert.NotNull(dbPayment);
                Assert.Equal("succeeded", dbPayment.status);
                Assert.Equal($"TXN_NORM_{testRunId}", dbPayment.provider_transaction_id);
                Assert.Null(dbPayment.failure_reason);

                var dbInvoice = await verifyDb.invoices.FindAsync(invoiceId);
                Assert.NotNull(dbInvoice);
                Assert.Equal("paid", dbInvoice.status);
                Assert.Equal(2_050_000m, dbInvoice.paid_amount);
                Assert.NotNull(dbInvoice.agreement_id);

                var dbReservation = await verifyDb.reservations.FindAsync(reservationId);
                Assert.NotNull(dbReservation);
                Assert.Equal("confirmed", dbReservation.status);
                Assert.NotNull(dbReservation.confirmed_at);

                var agreements = await verifyDb.rental_agreements.Where(a => a.reservation_id == reservationId).ToListAsync();
                Assert.Single(agreements);
                Assert.Equal("scheduled", agreements[0].status);
                Assert.Equal(dbInvoice.agreement_id, agreements[0].id);

                var allocations = await verifyDb.payment_allocations.Where(pa => pa.payment_id == paymentId).ToListAsync();
                Assert.Single(allocations);
                Assert.Equal(invoiceId, allocations[0].invoice_id);
                Assert.Equal(2_050_000m, allocations[0].allocated_amount);

                var events = await verifyDb.integration_events.Where(ie => ie.external_event_id == $"EVT_NORM_{testRunId}").ToListAsync();
                Assert.Single(events);
                Assert.Equal("payment_succeeded", events[0].event_type);
                Assert.Equal("processed", events[0].status);

                var refunds = await verifyDb.refunds.Where(r => r.payment_id == paymentId).ToListAsync();
                Assert.Empty(refunds);
            }
        }
        finally
        {
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
        }
    }

    [SqlIntegrationFact]
    public async Task ConcurrentIpn_TwoConcurrentSuccessCallbacks_DifferentDbContexts_BarrierSynchronized_ExactlyOneCreatesAgreement_OnSqlServer()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();

        try
        {
            long paymentId;
            long invoiceId;
            long reservationId;

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fixture = await SeedCheckoutFixtureAsync(seedDb, testRunId);
                cleanUpUserIds.Add(fixture.CustomerUserId);
                cleanUpFacilityIds.Add(fixture.FacilityId);
                cleanUpUnitTypeIds.Add(fixture.UnitTypeId);
                cleanUpReservationIds.Add(fixture.ReservationId);
                paymentId = fixture.PaymentId;
                invoiceId = fixture.InvoiceId;
                reservationId = fixture.ReservationId;
            }

            var barrier = new Barrier(2);

            var task1 = Task.Run(async () =>
            {
                await using var db1 = CreateDbContext(connectionString);
                var repo1 = new PaymentRepository(db1);
                barrier.SignalAndWait();
                return await repo1.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
                {
                    PaymentId = paymentId,
                    Provider = "vnpay",
                    ProviderTransactionId = $"TXN_PAR_SUCC_{testRunId}",
                    Amount = 2_050_000m,
                    ExternalEventId = $"EVT_PAR_SUCC_1_{testRunId}",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"00\"}",
                    PaidAt = DateTimeOffset.UtcNow
                });
            });

            var task2 = Task.Run(async () =>
            {
                await using var db2 = CreateDbContext(connectionString);
                var repo2 = new PaymentRepository(db2);
                barrier.SignalAndWait();
                return await repo2.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
                {
                    PaymentId = paymentId,
                    Provider = "vnpay",
                    ProviderTransactionId = $"TXN_PAR_SUCC_{testRunId}",
                    Amount = 2_050_000m,
                    ExternalEventId = $"EVT_PAR_SUCC_2_{testRunId}",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"00\"}",
                    PaidAt = DateTimeOffset.UtcNow
                });
            });

            var results = await Task.WhenAll(task1, task2);
            var freshWins = results.Count(r => !r.IsAlreadyProcessed && !r.ReconciliationRequired);
            var idempotentOrReconWins = results.Count(r => r.IsAlreadyProcessed || r.ReconciliationRequired);

            Assert.Equal(1, freshWins);
            Assert.Equal(1, idempotentOrReconWins);

            // Fresh DbContext verification: Durable state after commit/restart
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var agreements = await verifyDb.rental_agreements.Where(a => a.reservation_id == reservationId).ToListAsync();
                Assert.Single(agreements);

                var allocations = await verifyDb.payment_allocations.Where(pa => pa.payment_id == paymentId).ToListAsync();
                Assert.Single(allocations);

                var payment = await verifyDb.payments.FindAsync(paymentId);
                Assert.NotNull(payment);
                Assert.Equal("succeeded", payment.status);

                var invoice = await verifyDb.invoices.FindAsync(invoiceId);
                Assert.NotNull(invoice);
                Assert.Equal("paid", invoice.status);
                Assert.Equal(2_050_000m, invoice.paid_amount);

                var reservation = await verifyDb.reservations.FindAsync(reservationId);
                Assert.NotNull(reservation);
                Assert.Equal("confirmed", reservation.status);
            }
        }
        finally
        {
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
        }
    }

    [SqlIntegrationFact]
    public async Task ConcurrentIpn_SuccessThenDecline_SameTransactionId_SuccessWins_DeclineDoesNotOverwrite_OnSqlServer()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();

        try
        {
            long paymentId;
            long invoiceId;
            long reservationId;

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fixture = await SeedCheckoutFixtureAsync(seedDb, testRunId);
                cleanUpUserIds.Add(fixture.CustomerUserId);
                cleanUpFacilityIds.Add(fixture.FacilityId);
                cleanUpUnitTypeIds.Add(fixture.UnitTypeId);
                cleanUpReservationIds.Add(fixture.ReservationId);
                paymentId = fixture.PaymentId;
                invoiceId = fixture.InvoiceId;
                reservationId = fixture.ReservationId;

                // Step 1: Finalize payment successfully via ExecuteFinalizePaymentTransactionAsync
                var seedRepo = new PaymentRepository(seedDb);
                var successResult = await seedRepo.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
                {
                    PaymentId = paymentId,
                    Amount = 2_050_000m,
                    PaidAt = DateTimeOffset.UtcNow,
                    Provider = "vnpay",
                    ProviderTransactionId = $"TXN_PRE_{testRunId}",
                    ExternalEventId = $"EVT_PRE_SUCC_{testRunId}",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"00\"}"
                });
                Assert.False(successResult.ReconciliationRequired);
                Assert.NotNull(successResult.Agreement);
            }

            // Step 2: Late Decline callback arrives with the same payment ID
            FinalizeFailedPaymentResult lateDeclineResult;
            await using (var declineDb = CreateDbContext(connectionString))
            {
                var declineRepo = new PaymentRepository(declineDb);
                lateDeclineResult = await declineRepo.ExecuteFinalizeFailedPaymentTransactionAsync(new FinalizeFailedPaymentParams
                {
                    PaymentId = paymentId,
                    Provider = "vnpay",
                    ExternalEventId = $"EVT_DECLINE_{testRunId}",
                    FailureReason = "Late decline callback after success.",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"24\"}",
                    FailedAt = DateTimeOffset.UtcNow
                });
            }

            // Terminal status check prevents overwriting succeeded status
            Assert.False(lateDeclineResult.IsTransitionCommitted);
            Assert.Equal("succeeded", lateDeclineResult.CurrentStatus);

            // Verify durable state in fresh DbContext
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var finalPayment = await verifyDb.payments.FindAsync(paymentId);
                Assert.NotNull(finalPayment);
                Assert.Equal("succeeded", finalPayment.status);
                Assert.Null(finalPayment.failure_reason);

                var refundCount = await verifyDb.refunds.CountAsync(r => r.payment_id == paymentId);
                Assert.Equal(0, refundCount);
            }
        }
        finally
        {
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
        }
    }

    [SqlIntegrationFact]
    public async Task ConcurrentIpn_TwoConcurrentLateSuccessCallbacks_DifferentDbContexts_BarrierSynchronized_ExactlyOneCreatesRefund_OnSqlServer()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();

        try
        {
            long paymentId;
            long invoiceId;
            long reservationId;

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fixture = await SeedCheckoutFixtureAsync(seedDb, testRunId);
                cleanUpUserIds.Add(fixture.CustomerUserId);
                cleanUpFacilityIds.Add(fixture.FacilityId);
                cleanUpUnitTypeIds.Add(fixture.UnitTypeId);
                cleanUpReservationIds.Add(fixture.ReservationId);
                paymentId = fixture.PaymentId;
                invoiceId = fixture.InvoiceId;
                reservationId = fixture.ReservationId;

                // Step 1: Initial decline marks payment as failed (Reverse race setup)
                var p = await seedDb.payments.FindAsync(paymentId);
                Assert.NotNull(p);
                p.status = "failed";
                p.failure_reason = "VNPAY payment declined";
                await seedDb.SaveChangesAsync();
            }

            var barrier = new Barrier(2);

            var task1 = Task.Run(async () =>
            {
                await using var db1 = CreateDbContext(connectionString);
                var repo1 = new PaymentRepository(db1);
                barrier.SignalAndWait();
                return await repo1.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
                {
                    PaymentId = paymentId,
                    Provider = "vnpay",
                    ProviderTransactionId = $"TXN_PAR_SUCC_{testRunId}",
                    Amount = 2_050_000m,
                    ExternalEventId = $"EVT_PAR_SUCC_{testRunId}",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"00\"}",
                    PaidAt = DateTimeOffset.UtcNow
                });
            });

            var task2 = Task.Run(async () =>
            {
                await using var db2 = CreateDbContext(connectionString);
                var repo2 = new PaymentRepository(db2);
                barrier.SignalAndWait();
                return await repo2.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
                {
                    PaymentId = paymentId,
                    Provider = "vnpay",
                    ProviderTransactionId = $"TXN_PAR_SUCC_{testRunId}",
                    Amount = 2_050_000m,
                    ExternalEventId = $"EVT_PAR_SUCC_{testRunId}",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"00\"}",
                    PaidAt = DateTimeOffset.UtcNow
                });
            });

            var results = await Task.WhenAll(task1, task2);
            var initialWins = results.Count(r => !r.IsAlreadyProcessed);
            var alreadyProcessedWins = results.Count(r => r.IsAlreadyProcessed);

            Assert.Equal(1, initialWins);
            Assert.Equal(1, alreadyProcessedWins);

            // Fresh DbContext verification: Durable state after commit/restart
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var refunds = await verifyDb.refunds.Where(r => r.payment_id == paymentId).ToListAsync();
                Assert.Single(refunds);
                Assert.Equal("requested", refunds[0].status);
                Assert.Equal(2_050_000m, refunds[0].amount);

                var agreements = await verifyDb.rental_agreements.Where(a => a.reservation_id == reservationId).ToListAsync();
                Assert.Empty(agreements);

                var payment = await verifyDb.payments.FindAsync(paymentId);
                Assert.NotNull(payment);
                Assert.Equal("succeeded", payment.status);
                Assert.Equal("PAYMENT_ALREADY_FAILED", payment.failure_reason);

                var reservation = await verifyDb.reservations.FindAsync(reservationId);
                Assert.NotNull(reservation);
                Assert.NotEqual("confirmed", reservation.status);
            }
        }
        finally
        {
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
        }
    }

    [SqlIntegrationFact]
    public async Task ConcurrentIpn_TwoConcurrentDeclineCallbacks_DifferentDbContexts_BarrierSynchronized_OnlyOneCommitsFailure_OnSqlServer()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();

        try
        {
            long paymentId;

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fixture = await SeedCheckoutFixtureAsync(seedDb, testRunId);
                cleanUpUserIds.Add(fixture.CustomerUserId);
                cleanUpFacilityIds.Add(fixture.FacilityId);
                cleanUpUnitTypeIds.Add(fixture.UnitTypeId);
                cleanUpReservationIds.Add(fixture.ReservationId);
                paymentId = fixture.PaymentId;
            }

            var barrier = new Barrier(2);

            var task1 = Task.Run(async () =>
            {
                await using var db1 = CreateDbContext(connectionString);
                var repo1 = new PaymentRepository(db1);
                barrier.SignalAndWait();
                return await repo1.ExecuteFinalizeFailedPaymentTransactionAsync(new FinalizeFailedPaymentParams
                {
                    PaymentId = paymentId,
                    Provider = "vnpay",
                    ExternalEventId = $"EVT_DEC_PAR1_{testRunId}",
                    FailureReason = "Parallel decline 1",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"24\"}",
                    FailedAt = DateTimeOffset.UtcNow
                });
            });

            var task2 = Task.Run(async () =>
            {
                await using var db2 = CreateDbContext(connectionString);
                var repo2 = new PaymentRepository(db2);
                barrier.SignalAndWait();
                return await repo2.ExecuteFinalizeFailedPaymentTransactionAsync(new FinalizeFailedPaymentParams
                {
                    PaymentId = paymentId,
                    Provider = "vnpay",
                    ExternalEventId = $"EVT_DEC_PAR2_{testRunId}",
                    FailureReason = "Parallel decline 2",
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"24\"}",
                    FailedAt = DateTimeOffset.UtcNow
                });
            });

            var results = await Task.WhenAll(task1, task2);
            var committedCount = results.Count(r => r.IsTransitionCommitted);
            var uncommittedCount = results.Count(r => !r.IsTransitionCommitted);

            Assert.Equal(1, committedCount);
            Assert.Equal(1, uncommittedCount);

            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var payment = await verifyDb.payments.FindAsync(paymentId);
                Assert.NotNull(payment);
                Assert.Equal("failed", payment.status);
            }
        }
        finally
        {
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
        }
    }

    [SqlIntegrationFact]
    public async Task PaymentCheckoutTransaction_RollbackOnFailure_LeavesNoOrphanRecords_OnSqlServer()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();

        try
        {
            long customerUserId;
            long facilityId;
            long unitTypeId;
            long facilityRateId;
            long reservationId;

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fac = new facility
                {
                    code = $"FC_RB_{testRunId}",
                    name = $"Facility RB {testRunId}",
                    address_line = "123 Rollback St",
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
                    code = $"UT_RB_{testRunId}",
                    name = $"Type RB {testRunId}",
                    width_m = 2.0m,
                    length_m = 2.5m,
                    height_m = 2.5m,
                    area_m2 = 5.0m,
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
                    max_monthly_rate = 2_000_000m,
                    valid_from = new DateOnly(2020, 1, 1),
                    created_at = DateTimeOffset.UtcNow
                };
                seedDb.price_ranges.Add(pr);
                await seedDb.SaveChangesAsync();

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
                    email = $"user_rb_{testRunId}@test.local",
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
                    full_name = $"Customer RB {testRunId}",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                });

                var res = new reservation
                {
                    reservation_code = $"RES_RB_{testRunId}",
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

            // Simulate explicit SQL transaction rollback upon error during payment checkout creation
            await using (var rollbackDb = CreateDbContext(connectionString))
            {
                await using var tx = await rollbackDb.Database.BeginTransactionAsync();

                var inv = new invoice
                {
                    invoice_no = $"INV_RB_{testRunId}",
                    reservation_id = reservationId,
                    customer_id = customerUserId,
                    issue_date = DateOnly.FromDateTime(DateTime.UtcNow),
                    due_date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                    currency = "VND",
                    subtotal_amount = 2_050_000m,
                    total_amount = 2_050_000m,
                    paid_amount = 0m,
                    status = "draft",
                    created_at = DateTimeOffset.UtcNow
                };
                rollbackDb.invoices.Add(inv);
                await rollbackDb.SaveChangesAsync();

                var p = new payment
                {
                    customer_id = customerUserId,
                    target_invoice_id = inv.id,
                    amount = 2_050_000m,
                    currency = "VND",
                    method = "vnpay",
                    provider = "vnpay",
                    status = "pending",
                    idempotency_key = $"IDEM_RB_{testRunId}",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                };
                rollbackDb.payments.Add(p);
                await rollbackDb.SaveChangesAsync();

                // Explicit rollback simulating unexpected exception / downstream gateway failure
                await tx.RollbackAsync();
            }

            // Fresh DbContext verification: No orphan invoice or payment exists in database
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var orphanInvoices = await verifyDb.invoices.CountAsync(i => i.invoice_no == $"INV_RB_{testRunId}");
                Assert.Equal(0, orphanInvoices);

                var orphanPayments = await verifyDb.payments.CountAsync(p => p.idempotency_key == $"IDEM_RB_{testRunId}");
                Assert.Equal(0, orphanPayments);
            }
        }
        finally
        {
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
        }
    }

    [SqlIntegrationFact]
    public async Task ConcurrentIpn_IntegrationEventAndNotification_ExactlyOnce_OnSqlServer()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();

        try
        {
            long paymentId;
            long reservationId;

            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fixture = await SeedCheckoutFixtureAsync(seedDb, testRunId);
                cleanUpUserIds.Add(fixture.CustomerUserId);
                cleanUpFacilityIds.Add(fixture.FacilityId);
                cleanUpUnitTypeIds.Add(fixture.UnitTypeId);
                cleanUpReservationIds.Add(fixture.ReservationId);
                paymentId = fixture.PaymentId;
                reservationId = fixture.ReservationId;

                // Step 1: Initial decline marks payment as failed (Reverse race setup)
                var p = await seedDb.payments.FindAsync(paymentId);
                Assert.NotNull(p);
                p.status = "failed";
                p.failure_reason = "VNPAY payment declined";
                await seedDb.SaveChangesAsync();
            }

            var externalEventId = $"EVT_EVENT_ONCE_{testRunId}";
            var providerTxnId = $"TXN_EVENT_ONCE_{testRunId}";

            // Send multiple callbacks with the same externalEventId sequentially & concurrently
            for (int i = 0; i < 3; i++)
            {
                await using var db = CreateDbContext(connectionString);
                var repo = new PaymentRepository(db);
                var result = await repo.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
                {
                    PaymentId = paymentId,
                    Provider = "vnpay",
                    ProviderTransactionId = providerTxnId,
                    Amount = 2_050_000m,
                    ExternalEventId = externalEventId,
                    RawPayloadJson = "{\"vnp_ResponseCode\":\"00\"}",
                    PaidAt = DateTimeOffset.UtcNow
                });

                if (i == 0)
                {
                    Assert.False(result.IsAlreadyProcessed);
                    Assert.True(result.ReconciliationRequired);
                }
                else
                {
                    Assert.True(result.IsAlreadyProcessed);
                }
            }

            // Fresh DbContext verification:
            // 1. Integration event count is EXACTLY 1
            // 2. Event status is 'processed'
            // 3. Exactly 1 refund exists
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var events = await verifyDb.integration_events
                    .Where(e => e.external_event_id == externalEventId)
                    .ToListAsync();

                Assert.Single(events);
                Assert.Equal("processed", events[0].status);
                Assert.Equal("payment_succeeded_reconciliation", events[0].event_type);

                var refundCount = await verifyDb.refunds
                    .CountAsync(r => r.payment_id == paymentId);
                Assert.Equal(1, refundCount);
            }
        }
        finally
        {
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
        }
    }

    [SqlIntegrationFact]
    public async Task CleanupInterruption_SimulatedFailureInsideTransaction_RollsBackDisabledTriggers_AndLeavesAllGuardsEnabled_OnSqlServer()
    {
        var connectionString = GetTestConnectionString();
        var testRunId = Guid.NewGuid().ToString("N")[..8];
        var cleanUpReservationIds = new List<long>();
        var cleanUpUserIds = new List<long>();
        var cleanUpFacilityIds = new List<long>();
        var cleanUpUnitTypeIds = new List<long>();

        long paymentId;
        long invoiceId;

        try
        {
            // 1. Initial precondition: All 6 critical business triggers must be enabled
            await AssertAllCriticalTriggersEnabledAsync(connectionString);

            // Seed valid FK fixture (payment in 'pending' status, invoice in 'open' status)
            await using (var seedDb = CreateDbContext(connectionString))
            {
                var fixture = await SeedCheckoutFixtureAsync(seedDb, testRunId);
                cleanUpUserIds.Add(fixture.CustomerUserId);
                cleanUpFacilityIds.Add(fixture.FacilityId);
                cleanUpUnitTypeIds.Add(fixture.UnitTypeId);
                cleanUpReservationIds.Add(fixture.ReservationId);
                paymentId = fixture.PaymentId;
                invoiceId = fixture.InvoiceId;
            }

            // 2. Simulate cleanup interruption:
            // Inside an explicit transaction, triggers are disabled as part of cleanup.
            // While disabled, the allocation guard does NOT block inserting allocation for pending payment.
            // An unexpected exception or cancellation occurs before commit.
            // We assert that the transaction rollback guarantees that all triggers revert to enabled.
            await using (var testDb = CreateDbContext(connectionString))
            {
                var simulatedExceptionThrown = false;
                await using (var tx = await testDb.Database.BeginTransactionAsync())
                {
                    try
                    {
                        await testDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_payment_allocation_guard ON core.payment_allocations;");
                        await testDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_invoice_status_transition ON core.invoices;");
                        await testDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_invoice_lines_guard ON core.invoice_lines;");
                        await testDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_invoice_lines_refresh_totals ON core.invoice_lines;");
                        await testDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_agreement_status_transition ON core.rental_agreements;");
                        await testDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_reservation_status_transition ON core.reservations;");

                        // Verify that inside the uncommitted transaction, triggers are observed as disabled
                        var conn = testDb.Database.GetDbConnection();
                        await using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx.GetDbTransaction();
                        cmd.CommandText = @"
                            SELECT COUNT(*) 
                            FROM sys.triggers 
                            WHERE name IN (
                                'trg_payment_allocation_guard',
                                'trg_invoice_status_transition',
                                'trg_invoice_lines_guard',
                                'trg_invoice_lines_refresh_totals',
                                'trg_agreement_status_transition',
                                'trg_reservation_status_transition'
                            ) AND is_disabled = 1;";
                        var disabledCountInsideTx = (int)(await cmd.ExecuteScalarAsync() ?? 0);
                        Assert.Equal(6, disabledCountInsideTx);

                        // Prove that while disabled, the guard does NOT fire:
                        // With trg_payment_allocation_guard disabled, inserting allocation on pending payment succeeds inside tx!
                        var insertCmd = conn.CreateCommand();
                        insertCmd.Transaction = tx.GetDbTransaction();
                        insertCmd.CommandText = @"
                            INSERT INTO core.payment_allocations (payment_id, invoice_id, allocated_amount, allocated_at)
                            VALUES (@pId, @iId, 100000, SYSUTCDATETIME());";
                        var pParam = insertCmd.CreateParameter();
                        pParam.ParameterName = "@pId";
                        pParam.Value = paymentId;
                        insertCmd.Parameters.Add(pParam);
                        var iParam = insertCmd.CreateParameter();
                        iParam.ParameterName = "@iId";
                        iParam.Value = invoiceId;
                        insertCmd.Parameters.Add(iParam);
                        var rowsInserted = await insertCmd.ExecuteNonQueryAsync();
                        Assert.Equal(1, rowsInserted);

                        // Simulate unexpected process failure / cancellation during cleanup
                        throw new OperationCanceledException($"Simulated process cancellation/interruption during cleanup {testRunId}");
                    }
                    catch (OperationCanceledException)
                    {
                        simulatedExceptionThrown = true;
                        // Transaction rollback on interruption restores disabled triggers and rolls back uncommitted rows
                        await tx.RollbackAsync();
                    }
                }

                Assert.True(simulatedExceptionThrown);
            }

            // 3. Postcondition verification from fresh connection:
            // All 6 triggers must be 100% enabled (is_disabled = 0)
            await AssertAllCriticalTriggersEnabledAsync(connectionString);

            // 4. Verify active enforcement:
            // With real foreign keys (paymentId, invoiceId) present in database,
            // attempting to insert an allocation for a payment whose status is 'pending'
            // passes foreign key constraints but triggers core.trg_payment_allocation_guard.
            // Guard must actively THROW 51055 (strictly asserting error 51055, never foreign key error 547).
            await using (var verifyDb = CreateDbContext(connectionString))
            {
                var sqlEx = await Assert.ThrowsAsync<SqlException>(async () =>
                {
                    await verifyDb.Database.ExecuteSqlRawAsync(@"
                        INSERT INTO core.payment_allocations (payment_id, invoice_id, allocated_amount, allocated_at)
                        VALUES ({0}, {1}, 100000, SYSUTCDATETIME());",
                        paymentId, invoiceId);
                });

                // Strictly assert business trigger error 51055
                Assert.Equal(51055, sqlEx.Number);
                Assert.Contains("Allocation requires a succeeded payment, open invoice and matching customer.", sqlEx.Message);
            }
        }
        finally
        {
            await CleanUpTestDataAsync(connectionString, testRunId, cleanUpReservationIds, cleanUpUserIds, cleanUpFacilityIds, cleanUpUnitTypeIds);
            await AssertAllCriticalTriggersEnabledAsync(connectionString);
        }
    }

    private static async Task<(long CustomerUserId, long FacilityId, long UnitTypeId, long FacilityRateId, long PolicyVersionId, long ReservationId, long PaymentId, long InvoiceId)> SeedCheckoutFixtureAsync(
        SelfStorageDbContext seedDb,
        string testRunId)
    {
        var fac = new facility
        {
            code = $"FC_{testRunId}",
            name = $"Facility {testRunId}",
            address_line = "123 Concurrency St",
            city = "Hanoi",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        };
        seedDb.facilities.Add(fac);
        await seedDb.SaveChangesAsync();

        var ut = new unit_type
        {
            code = $"UT_{testRunId}",
            name = $"Type {testRunId}",
            width_m = 2.0m,
            length_m = 2.5m,
            height_m = 2.5m,
            area_m2 = 5.0m,
            climate_controlled = false,
            is_active = true
        };
        seedDb.unit_types.Add(ut);
        await seedDb.SaveChangesAsync();

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

        var rate = new facility_rate
        {
            facility_id = fac.id,
            unit_type_id = ut.id,
            monthly_rate = 1_000_000m,
            deposit_amount = 1_000_000m,
            booking_fee = 50_000m,
            valid_from = new DateOnly(2020, 1, 1)
        };
        seedDb.facility_rates.Add(rate);
        await seedDb.SaveChangesAsync();

        var su = new storage_unit
        {
            facility_id = fac.id,
            unit_type_id = ut.id,
            unit_code = $"SU_{testRunId}",
            physical_status = "available",
            is_listed = true,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        seedDb.storage_units.Add(su);
        await seedDb.SaveChangesAsync();

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

        seedDb.customer_profiles.Add(new customer_profile
        {
            user_id = user.id,
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

        var res = new reservation
        {
            reservation_code = $"RES_{testRunId}",
            customer_id = user.id,
            facility_id = fac.id,
            unit_type_id = ut.id,
            facility_rate_id = rate.id,
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

        var repo = new PaymentRepository(seedDb);
        var checkout = await repo.ExecuteCheckoutTransactionAsync(new CheckoutTransactionParams
        {
            ReservationId = res.id,
            CustomerId = user.id,
            PolicyVersionId = policy.id,
            PolicyVersionNumber = "2026.1",
            IdempotencyKey = $"IDEM_SEED_{testRunId}",
            PaymentMethod = "vnpay",
            PaymentProvider = "vnpay",
            ComputedDeposit = 1_000_000m,
            ComputedRent = 1_000_000m,
            ComputedBookingFee = 50_000m,
            ComputedDiscount = 0m,
            ComputedTotal = 2_050_000m,
            NowUtc = DateTimeOffset.UtcNow,
            ClientIp = "127.0.0.1",
            EvidenceMetadataJson = "{}"
        });

        return (user.id, fac.id, ut.id, rate.id, policy.id, res.id, checkout.Payment.id, checkout.Invoice.id);
    }

    public static async Task AssertAllCriticalTriggersEnabledAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.name, t.is_disabled
            FROM sys.triggers t
            WHERE t.name IN (
                'trg_payment_allocation_guard',
                'trg_invoice_status_transition',
                'trg_invoice_lines_guard',
                'trg_invoice_lines_refresh_totals',
                'trg_agreement_status_transition',
                'trg_reservation_status_transition'
            );";

        var disabledList = new List<string>();
        var foundCount = 0;
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                foundCount++;
                var name = reader.GetString(0);
                var isDisabled = reader.GetBoolean(1);
                if (isDisabled)
                {
                    disabledList.Add(name);
                }
            }
        }

        if (foundCount < 6)
        {
            throw new InvalidOperationException($"Không tìm thấy đủ 6 trigger nghiệp vụ trọng yếu trong database (tìm thấy {foundCount}/6).");
        }

        if (disabledList.Count > 0)
        {
            throw new InvalidOperationException($"Phát hiện trigger nghiệp vụ bị vô hiệu hóa: {string.Join(", ", disabledList)}. Vi phạm yêu cầu toàn vẹn dữ liệu.");
        }
    }

    private static async Task CleanUpTestDataAsync(
        string connectionString,
        string testRunId,
        IEnumerable<long> cleanUpReservationIds,
        IEnumerable<long> cleanUpUserIds,
        IEnumerable<long> cleanUpFacilityIds,
        IEnumerable<long> cleanUpUnitTypeIds)
    {
        await using var cleanDb = CreateDbContext(connectionString);
        var resIds = cleanUpReservationIds.Distinct().ToList();
        if (resIds.Count > 0)
        {
            var resIdList = string.Join(",", resIds);

            // Execute cleanup inside an explicit transaction.
            // In SQL Server, DDL statements (including DISABLE/ENABLE TRIGGER) are transactional.
            // If the transaction fails, crashes, or is disposed without commit, SQL Server automatically rolls back,
            // restoring all triggers to is_disabled = 0.
            await using (var tx = await cleanDb.Database.BeginTransactionAsync())
            {
                try
                {
                    await cleanDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_payment_allocation_guard ON core.payment_allocations;");
                    await cleanDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_invoice_status_transition ON core.invoices;");
                    await cleanDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_invoice_lines_guard ON core.invoice_lines;");
                    await cleanDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_invoice_lines_refresh_totals ON core.invoice_lines;");
                    await cleanDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_agreement_status_transition ON core.rental_agreements;");
                    await cleanDb.Database.ExecuteSqlRawAsync("DISABLE TRIGGER core.trg_reservation_status_transition ON core.reservations;");

                    await cleanDb.Database.ExecuteSqlRawAsync($"UPDATE core.invoices SET agreement_id = NULL WHERE reservation_id IN ({resIdList})");
                    await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.refund_approvals WHERE refund_id IN (SELECT id FROM core.refunds WHERE payment_id IN (SELECT id FROM core.payments WHERE target_invoice_id IN (SELECT id FROM core.invoices WHERE reservation_id IN ({resIdList}))))");
                    await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.refunds WHERE payment_id IN (SELECT id FROM core.payments WHERE target_invoice_id IN (SELECT id FROM core.invoices WHERE reservation_id IN ({resIdList})))");
                    await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.payment_allocations WHERE invoice_id IN (SELECT id FROM core.invoices WHERE reservation_id IN ({resIdList}))");
                    await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.payments WHERE target_invoice_id IN (SELECT id FROM core.invoices WHERE reservation_id IN ({resIdList}))");
                    await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.invoice_lines WHERE invoice_id IN (SELECT id FROM core.invoices WHERE reservation_id IN ({resIdList}))");
                    await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.invoices WHERE reservation_id IN ({resIdList})");
                    await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.promotion_redemptions WHERE reservation_id IN ({resIdList})");
                    await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.rental_agreements WHERE reservation_id IN ({resIdList})");
                    await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.reservations WHERE id IN ({resIdList})");

                    await cleanDb.Database.ExecuteSqlRawAsync("ENABLE TRIGGER core.trg_payment_allocation_guard ON core.payment_allocations;");
                    await cleanDb.Database.ExecuteSqlRawAsync("ENABLE TRIGGER core.trg_invoice_status_transition ON core.invoices;");
                    await cleanDb.Database.ExecuteSqlRawAsync("ENABLE TRIGGER core.trg_invoice_lines_guard ON core.invoice_lines;");
                    await cleanDb.Database.ExecuteSqlRawAsync("ENABLE TRIGGER core.trg_invoice_lines_refresh_totals ON core.invoice_lines;");
                    await cleanDb.Database.ExecuteSqlRawAsync("ENABLE TRIGGER core.trg_agreement_status_transition ON core.rental_agreements;");
                    await cleanDb.Database.ExecuteSqlRawAsync("ENABLE TRIGGER core.trg_reservation_status_transition ON core.reservations;");

                    await tx.CommitAsync();
                }
                catch
                {
                    try { await tx.RollbackAsync(); } catch { }
                    // Failsafe outside transaction
                    try
                    {
                        await cleanDb.Database.ExecuteSqlRawAsync(@"
                            ENABLE TRIGGER core.trg_payment_allocation_guard ON core.payment_allocations;
                            ENABLE TRIGGER core.trg_invoice_status_transition ON core.invoices;
                            ENABLE TRIGGER core.trg_invoice_lines_guard ON core.invoice_lines;
                            ENABLE TRIGGER core.trg_invoice_lines_refresh_totals ON core.invoice_lines;
                            ENABLE TRIGGER core.trg_agreement_status_transition ON core.rental_agreements;
                            ENABLE TRIGGER core.trg_reservation_status_transition ON core.reservations;");
                    }
                    catch { }
                    throw;
                }
            }
        }

        await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.integration_events WHERE external_event_id LIKE '%{testRunId}%'");

        var userIds = cleanUpUserIds.Distinct().ToList();
        if (userIds.Count > 0)
        {
            var userIdList = string.Join(",", userIds);
            await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.customer_profiles WHERE user_id IN ({userIdList})");
            await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.users WHERE id IN ({userIdList})");
        }

        var facIds = cleanUpFacilityIds.Distinct().ToList();
        if (facIds.Count > 0)
        {
            var facIdList = string.Join(",", facIds);
            await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.storage_units WHERE facility_id IN ({facIdList})");
            await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.facility_rates WHERE facility_id IN ({facIdList})");
            await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.facilities WHERE id IN ({facIdList})");
        }

        var utIds = cleanUpUnitTypeIds.Distinct().ToList();
        if (utIds.Count > 0)
        {
            var utIdList = string.Join(",", utIds);
            await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.price_ranges WHERE unit_type_id IN ({utIdList})");
            await cleanDb.Database.ExecuteSqlRawAsync($"DELETE FROM core.unit_types WHERE id IN ({utIdList})");
        }

        // Postcondition: Ensure all 6 critical business triggers are enabled
        await AssertAllCriticalTriggersEnabledAsync(connectionString);
    }
}
