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
            // Task B: Late IPN on payment 1 arrives concurrently
            var taskA = Task.Run(async () =>
            {
                await using var dbA = CreateDbContext(connectionString);
                var inv = await dbA.invoices.FindAsync(invoice1Id);
                if (inv != null)
                {
                    inv.status = "voided";
                    inv.updated_at = DateTimeOffset.UtcNow;
                    await dbA.SaveChangesAsync();
                }
            });

            var taskB = Task.Run(async () =>
            {
                await using var dbB = CreateDbContext(connectionString);
                var repoB = new PaymentRepository(dbB);
                return await repoB.ExecuteFinalizePaymentTransactionAsync(new FinalizePaymentParams
                {
                    PaymentId = payment1Id,
                    Provider = "vnpay",
                    ProviderTransactionId = $"TXN_{testRunId}_OLD",
                    Amount = 2_050_000m,
                    ExternalEventId = $"EVT_{testRunId}_OLD",
                    RawPayloadJson = "{}",
                    PaidAt = DateTimeOffset.UtcNow
                });
            });

            await Task.WhenAll(taskA, taskB);
            var finalizeResult = await taskB;

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

    private static async Task CleanUpTestDataAsync(
        string connectionString,
        string testRunId,
        IEnumerable<long> cleanUpReservationIds,
        IEnumerable<long> cleanUpUserIds,
        IEnumerable<long> cleanUpFacilityIds,
        IEnumerable<long> cleanUpUnitTypeIds)
    {
        await using var cleanDb = CreateDbContext(connectionString);
        foreach (var rId in cleanUpReservationIds.Distinct())
        {
            var allocs = await cleanDb.payment_allocations.Where(pa => pa.invoice.reservation_id == rId).ToListAsync();
            cleanDb.payment_allocations.RemoveRange(allocs);

            var refunds = await cleanDb.refunds.Where(rf => rf.payment.target_invoice.reservation_id == rId).ToListAsync();
            var refundIds = refunds.Select(rf => rf.id).ToList();
            var approvals = await cleanDb.refund_approvals.Where(ra => refundIds.Contains(ra.refund_id)).ToListAsync();
            cleanDb.refund_approvals.RemoveRange(approvals);
            cleanDb.refunds.RemoveRange(refunds);

            var payments = await cleanDb.payments.Where(p => p.target_invoice.reservation_id == rId).ToListAsync();
            cleanDb.payments.RemoveRange(payments);

            var invLines = await cleanDb.invoice_lines.Where(il => il.invoice.reservation_id == rId).ToListAsync();
            cleanDb.invoice_lines.RemoveRange(invLines);

            var invs = await cleanDb.invoices.Where(i => i.reservation_id == rId).ToListAsync();
            cleanDb.invoices.RemoveRange(invs);

            var redemptions = await cleanDb.promotion_redemptions.Where(pr => pr.reservation_id == rId).ToListAsync();
            cleanDb.promotion_redemptions.RemoveRange(redemptions);

            var agreements = await cleanDb.rental_agreements.Where(a => a.reservation_id == rId).ToListAsync();
            cleanDb.rental_agreements.RemoveRange(agreements);

            var res = await cleanDb.reservations.FindAsync(rId);
            if (res != null) cleanDb.reservations.Remove(res);
        }

        var events = await cleanDb.integration_events.Where(ie => ie.external_event_id.Contains(testRunId)).ToListAsync();
        cleanDb.integration_events.RemoveRange(events);

        foreach (var uId in cleanUpUserIds.Distinct())
        {
            var prof = await cleanDb.customer_profiles.FindAsync(uId);
            if (prof != null) cleanDb.customer_profiles.Remove(prof);
            var u = await cleanDb.users.FindAsync(uId);
            if (u != null) cleanDb.users.Remove(u);
        }
        foreach (var fId in cleanUpFacilityIds.Distinct())
        {
            var rates = await cleanDb.facility_rates.Where(r => r.facility_id == fId).ToListAsync();
            cleanDb.facility_rates.RemoveRange(rates);
            var fac = await cleanDb.facilities.FindAsync(fId);
            if (fac != null) cleanDb.facilities.Remove(fac);
        }
        foreach (var utId in cleanUpUnitTypeIds.Distinct())
        {
            var ut = await cleanDb.unit_types.FindAsync(utId);
            if (ut != null) cleanDb.unit_types.Remove(ut);
        }
        await cleanDb.SaveChangesAsync();
    }
}
