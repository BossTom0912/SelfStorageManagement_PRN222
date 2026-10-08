using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Implementations;

public class PaymentRepository : IPaymentRepository
{
    private readonly SelfStorageDbContext _context;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> InMemorySemaphores = new();

    public PaymentRepository(SelfStorageDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<reservation?> GetReservationForQuoteAsync(
        long reservationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.reservations
            .AsNoTracking()
            .Include(r => r.customer)
                .ThenInclude(c => c.user)
            .Include(r => r.facility)
            .Include(r => r.unit_type)
            .Include(r => r.facility_rate)
            .Include(r => r.promotion_redemption)
                .ThenInclude(pr => pr!.promotion)
            .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);
    }

    public async Task<policy_version?> GetActiveRentalTermsPolicyVersionAsync(
        DateOnly forDate,
        CancellationToken cancellationToken = default)
    {
        return await _context.policy_versions
            .AsNoTracking()
            .Where(p => p.policy_type == "rental_terms" &&
                        p.valid_from <= forDate &&
                        (p.valid_to == null || p.valid_to > forDate))
            .OrderByDescending(p => p.valid_from)
            .ThenByDescending(p => p.id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<promotion?> GetPromotionByCodeAsync(
        string code,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var cleanCode = code.Trim().ToUpperInvariant();
        return await _context.promotions
            .Include(p => p.promotion_rules)
            .FirstOrDefaultAsync(p => p.code == cleanCode &&
                                      p.is_active &&
                                      p.valid_from <= nowUtc &&
                                      p.valid_to >= nowUtc,
                                      cancellationToken);
    }

    public async Task<int> GetPromotionActiveRedemptionsCountAsync(
        long promotionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.promotion_redemptions
            .CountAsync(pr => pr.promotion_id == promotionId &&
                              (pr.status == "reserved" || pr.status == "applied"),
                              cancellationToken);
    }

    public async Task<int> GetCustomerActiveRedemptionsCountAsync(
        long promotionId,
        long customerId,
        CancellationToken cancellationToken = default)
    {
        return await _context.promotion_redemptions
            .CountAsync(pr => pr.promotion_id == promotionId &&
                              pr.customer_id == customerId &&
                              (pr.status == "reserved" || pr.status == "applied"),
                              cancellationToken);
    }

    public async Task<bool> IsNewCustomerAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var hasPriorConfirmedReservations = await _context.reservations
            .AnyAsync(r => r.customer_id == customerId &&
                           (r.status == "confirmed" || r.status == "checked_in" || r.status == "converted" || r.status == "completed"),
                           cancellationToken);

        if (hasPriorConfirmedReservations) return false;

        var hasPriorAgreements = await _context.rental_agreements
            .AnyAsync(a => a.customer_id == customerId, cancellationToken);

        return !hasPriorAgreements;
    }

    public async Task<payment?> GetPaymentByIdAsync(
        long paymentId,
        CancellationToken cancellationToken = default)
    {
        return await _context.payments
            .Include(p => p.customer)
                .ThenInclude(c => c.user)
            .Include(p => p.target_invoice)
                .ThenInclude(inv => inv.reservation)
            .Include(p => p.target_invoice)
                .ThenInclude(inv => inv.agreement)
            .Include(p => p.payment_allocations)
            .FirstOrDefaultAsync(p => p.id == paymentId, cancellationToken);
    }

    public async Task<payment?> GetPaymentByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return await _context.payments
            .Include(p => p.target_invoice)
                .ThenInclude(inv => inv.reservation)
            .FirstOrDefaultAsync(p => p.idempotency_key == idempotencyKey, cancellationToken);
    }

    public async Task<payment?> GetActivePendingPaymentByReservationIdAsync(
        long reservationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        return await _context.payments
            .Include(p => p.target_invoice)
            .FirstOrDefaultAsync(p => p.target_invoice.reservation_id == reservationId &&
                                      (p.status == "initiated" || p.status == "pending") &&
                                      p.target_invoice.reservation != null &&
                                      p.target_invoice.reservation.hold_until > nowUtc,
                                      cancellationToken);
    }

    public async Task<invoice?> GetInvoiceByIdAsync(
        long invoiceId,
        CancellationToken cancellationToken = default)
    {
        return await _context.invoices
            .Include(inv => inv.customer)
                .ThenInclude(c => c.user)
            .Include(inv => inv.reservation)
            .Include(inv => inv.agreement)
            .Include(inv => inv.invoice_lines)
            .Include(inv => inv.payment_allocations)
            .FirstOrDefaultAsync(inv => inv.id == invoiceId, cancellationToken);
    }

    public async Task<rental_agreement?> GetAgreementByIdAsync(
        long agreementId,
        CancellationToken cancellationToken = default)
    {
        return await _context.rental_agreements
            .Include(a => a.customer)
                .ThenInclude(c => c.user)
            .Include(a => a.facility)
            .Include(a => a.policy_version)
            .Include(a => a.reservation)
            .FirstOrDefaultAsync(a => a.id == agreementId, cancellationToken);
    }

    public async Task<rental_agreement?> GetAgreementByReservationIdAsync(
        long reservationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.rental_agreements
            .Include(a => a.customer)
                .ThenInclude(c => c.user)
            .Include(a => a.facility)
            .Include(a => a.policy_version)
            .FirstOrDefaultAsync(a => a.reservation_id == reservationId, cancellationToken);
    }

    public async Task<integration_event?> GetIntegrationEventAsync(
        string source,
        string externalEventId,
        CancellationToken cancellationToken = default)
    {
        return await _context.integration_events
            .FirstOrDefaultAsync(e => e.source == source && e.external_event_id == externalEventId, cancellationToken);
    }

    public async Task<List<payment>> GetPaymentsRequiringReconciliationAsync(
        List<long>? accessibleFacilityIds,
        CancellationToken cancellationToken = default)
    {
        var query = _context.payments
            .Include(p => p.customer)
                .ThenInclude(c => c.user)
            .Include(p => p.target_invoice)
                .ThenInclude(inv => inv.reservation)
                    .ThenInclude(r => r!.facility)
            .Where(p => p.failure_reason == "LATE_PAYMENT_HOLD_EXPIRED" ||
                        p.failure_reason == "PARTIAL_PAYMENT_UNDERPAID" ||
                        p.metadata.Contains("\"reconciliation_required\":true") ||
                        p.metadata.Contains("\"reconciliation_required\": true"));

        if (accessibleFacilityIds != null)
        {
            query = query.Where(p => p.target_invoice.reservation != null &&
                                     accessibleFacilityIds.Contains(p.target_invoice.reservation.facility_id));
        }

        return await query.OrderByDescending(p => p.created_at).ToListAsync(cancellationToken);
    }

    public async Task<CheckoutTransactionResult> ExecuteCheckoutTransactionAsync(
        CheckoutTransactionParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var reservationLockKey = $"SS_Reservation_{parameters.ReservationId}";
        var promotionLockKey = parameters.AppliedPromotion != null ? $"SS_Promotion_{parameters.AppliedPromotion.id}" : null;
        var isRelational = _context.Database.IsRelational();

        SemaphoreSlim? inMemoryResSemaphore = null;
        SemaphoreSlim? inMemoryPromoSemaphore = null;
        if (!isRelational)
        {
            inMemoryResSemaphore = InMemorySemaphores.GetOrAdd(reservationLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredRes = await inMemoryResSemaphore.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredRes)
            {
                throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
            }

            if (promotionLockKey != null)
            {
                inMemoryPromoSemaphore = InMemorySemaphores.GetOrAdd(promotionLockKey, _ => new SemaphoreSlim(1, 1));
                var enteredPromo = await inMemoryPromoSemaphore.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                if (!enteredPromo)
                {
                    inMemoryResSemaphore.Release();
                    throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
                }
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

                // 1. Lock Reservation
                var resResultParam = new SqlParameter
                {
                    ParameterName = "@ResResult",
                    SqlDbType = SqlDbType.Int,
                    Direction = ParameterDirection.Output
                };
                var resResourceParam = new SqlParameter("@ResResource", reservationLockKey);

                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC @ResResult = sp_getapplock @Resource = @ResResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000",
                    new object[] { resResultParam, resResourceParam },
                    cancellationToken);

                var resLockResult = (int)(resResultParam.Value ?? -999);
                if (resLockResult < 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
                }

                // 2. Lock Promotion (Consistent lock order: Reservation -> Promotion)
                if (promotionLockKey != null)
                {
                    var promoResultParam = new SqlParameter
                    {
                        ParameterName = "@PromoResult",
                        SqlDbType = SqlDbType.Int,
                        Direction = ParameterDirection.Output
                    };
                    var promoResourceParam = new SqlParameter("@PromoResource", promotionLockKey);

                    await _context.Database.ExecuteSqlRawAsync(
                        "EXEC @PromoResult = sp_getapplock @Resource = @PromoResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000",
                        new object[] { promoResultParam, promoResourceParam },
                        cancellationToken);

                    var promoLockResult = (int)(promoResultParam.Value ?? -999);
                    if (promoLockResult < 0)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
                    }
                }
            }

            // 1. Idempotency check: check if payment with this key already exists
            var existingPaymentWithKey = await _context.payments
                .Include(p => p.target_invoice)
                .FirstOrDefaultAsync(p => p.idempotency_key == parameters.IdempotencyKey, cancellationToken);

            if (existingPaymentWithKey != null)
            {
                // Verify full payload matches under lock (Item 1)
                var meta = SafeParseJson(existingPaymentWithKey.metadata);
                meta.TryGetValue("accepted_policy_version_id", out var pvObj);
                meta.TryGetValue("promotion_code", out var promoObj);

                long? recordedPolicyVersionId = null;
                if (pvObj != null && long.TryParse(pvObj.ToString(), out var parsedPvId))
                {
                    recordedPolicyVersionId = parsedPvId;
                }

                var recordedPromoCode = promoObj?.ToString()?.Trim() ?? string.Empty;
                var requestedPromoCode = parameters.AppliedPromotion?.code?.Trim() ?? string.Empty;

                var matchesPayload = existingPaymentWithKey.customer_id == parameters.CustomerId &&
                                     existingPaymentWithKey.target_invoice?.reservation_id == parameters.ReservationId &&
                                     string.Equals(existingPaymentWithKey.provider, parameters.PaymentProvider, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(existingPaymentWithKey.method, parameters.PaymentMethod, StringComparison.OrdinalIgnoreCase) &&
                                     existingPaymentWithKey.amount == parameters.ComputedTotal &&
                                     recordedPolicyVersionId == parameters.PolicyVersionId &&
                                     string.Equals(recordedPromoCode, requestedPromoCode, StringComparison.OrdinalIgnoreCase);

                if (!matchesPayload)
                {
                    if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("IDEMPOTENCY_PAYLOAD_MISMATCH");
                }

                // Check if payment or invoice is in terminal / invalid state (Item 5)
                if (existingPaymentWithKey.status == "failed" || existingPaymentWithKey.status == "cancelled")
                {
                    if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException($"PAYMENT_ALREADY_TERMINATED:{existingPaymentWithKey.status}");
                }

                if (existingPaymentWithKey.status == "succeeded")
                {
                    if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("PAYMENT_ALREADY_SUCCEEDED");
                }

                if (existingPaymentWithKey.target_invoice?.status == "voided")
                {
                    if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("INVOICE_VOIDED");
                }

                var existingReservation = await _context.reservations
                    .FirstOrDefaultAsync(r => r.id == parameters.ReservationId, cancellationToken);

                if (existingReservation == null)
                {
                    if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("RESERVATION_NOT_FOUND");
                }

                if (transaction != null) await transaction.CommitAsync(cancellationToken);

                return new CheckoutTransactionResult
                {
                    IsIdempotentReplay = true,
                    Payment = existingPaymentWithKey,
                    Invoice = existingPaymentWithKey.target_invoice!,
                    Reservation = existingReservation
                };
            }

            // 2. Check if another active pending payment attempt exists with a DIFFERENT key
            var otherActiveAttempt = await _context.payments
                .Include(p => p.target_invoice)
                .FirstOrDefaultAsync(p => p.target_invoice.reservation_id == parameters.ReservationId &&
                                          (p.status == "initiated" || p.status == "pending"),
                                          cancellationToken);

            var reservation = await _context.reservations
                .Include(r => r.facility)
                .FirstOrDefaultAsync(r => r.id == parameters.ReservationId, cancellationToken);

            if (reservation == null)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("RESERVATION_NOT_FOUND");
            }

            if (otherActiveAttempt != null && reservation.hold_until > parameters.NowUtc)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException($"ACTIVE_ATTEMPT_EXISTS:{otherActiveAttempt.id}");
            }

            // 3. Validate reservation hold state
            if (reservation.status != "pending" && reservation.status != "awaiting_deposit")
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException($"INVALID_RESERVATION_STATUS:{reservation.status}");
            }

            if (reservation.hold_until <= parameters.NowUtc)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("HOLD_EXPIRED");
            }

            if (reservation.deposit_snapshot != reservation.monthly_rate_snapshot)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("DEPOSIT_RATE_MISMATCH");
            }

            // 4. Handle promotion voucher inside locked transaction (Issue 4)
            if (parameters.AppliedPromotion == null)
            {
                // Customer retrying without voucher. Release any previously reserved redemptions!
                var activeRedemptions = await _context.promotion_redemptions
                    .Where(pr => pr.reservation_id == parameters.ReservationId && pr.status == "reserved")
                    .ToListAsync(cancellationToken);

                foreach (var red in activeRedemptions)
                {
                    red.status = "released";
                }
            }
            else
            {
                // Release any reserved redemptions for a different promotion on this reservation
                var otherRedemptions = await _context.promotion_redemptions
                    .Where(pr => pr.reservation_id == parameters.ReservationId &&
                                 pr.status == "reserved" &&
                                 pr.promotion_id != parameters.AppliedPromotion.id)
                    .ToListAsync(cancellationToken);

                foreach (var other in otherRedemptions)
                {
                    other.status = "released";
                }

                // Revalidate usage limit inside lock
                if (parameters.AppliedPromotion.usage_limit.HasValue)
                {
                    var currentUsage = await _context.promotion_redemptions
                        .CountAsync(pr => pr.promotion_id == parameters.AppliedPromotion.id &&
                                          pr.reservation_id != parameters.ReservationId &&
                                          (pr.status == "reserved" || pr.status == "applied"),
                                          cancellationToken);

                    if (currentUsage >= parameters.AppliedPromotion.usage_limit.Value)
                    {
                        if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                        throw new InvalidOperationException("PROMOTION_USAGE_LIMIT_EXCEEDED");
                    }
                }

                // Revalidate customer limit inside lock
                if (parameters.AppliedPromotion.per_customer_limit.HasValue)
                {
                    var currentCustomerUsage = await _context.promotion_redemptions
                        .CountAsync(pr => pr.promotion_id == parameters.AppliedPromotion.id &&
                                          pr.customer_id == parameters.CustomerId &&
                                          pr.reservation_id != parameters.ReservationId &&
                                          (pr.status == "reserved" || pr.status == "applied"),
                                          cancellationToken);

                    if (currentCustomerUsage >= parameters.AppliedPromotion.per_customer_limit.Value)
                    {
                        if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                        throw new InvalidOperationException("PROMOTION_CUSTOMER_LIMIT_EXCEEDED");
                    }
                }

                var existingRedemption = await _context.promotion_redemptions
                    .FirstOrDefaultAsync(pr => pr.reservation_id == parameters.ReservationId &&
                                              pr.promotion_id == parameters.AppliedPromotion.id &&
                                              pr.status == "reserved", cancellationToken);

                if (existingRedemption != null)
                {
                    existingRedemption.discount_amount = parameters.ComputedDiscount;
                    existingRedemption.status = "reserved";
                    existingRedemption.redeemed_at = parameters.NowUtc;
                }
                else
                {
                    var newRedemption = new promotion_redemption
                    {
                        promotion_id = parameters.AppliedPromotion.id,
                        customer_id = parameters.CustomerId,
                        reservation_id = parameters.ReservationId,
                        discount_amount = parameters.ComputedDiscount,
                        status = "reserved",
                        redeemed_at = parameters.NowUtc
                    };
                    _context.promotion_redemptions.Add(newRedemption);
                }
            }

            // Update reservation snapshots
            reservation.discount_snapshot = parameters.ComputedDiscount;
            reservation.quoted_total = parameters.ComputedTotal;
            reservation.status = "awaiting_deposit";
            reservation.updated_at = parameters.NowUtc;

            // 5. Create or update initial invoice (Finding 5, Issue 2)
            var existingInvoice = await _context.invoices
                .Include(inv => inv.invoice_lines)
                .FirstOrDefaultAsync(inv => inv.reservation_id == parameters.ReservationId &&
                                            (inv.status == "draft" || inv.status == "open") &&
                                            inv.paid_amount == 0,
                                            cancellationToken);

            if (existingInvoice != null)
            {
                var hasExistingPayments = await _context.payments
                    .AnyAsync(p => p.target_invoice_id == existingInvoice.id, cancellationToken);

                if (hasExistingPayments || existingInvoice.total_amount != parameters.ComputedTotal)
                {
                    existingInvoice.status = "voided";
                    existingInvoice.voided_at = parameters.NowUtc;
                    existingInvoice.updated_at = parameters.NowUtc;
                    existingInvoice = null; // Void previous invoice and generate new immutable invoice!
                }
            }

            var lines = new List<invoice_line>
            {
                new invoice_line
                {
                    line_type = "deposit",
                    description = "Tiền đặt cọc giữ kho (1 tháng thuê)",
                    quantity = 1,
                    unit_price = parameters.ComputedDeposit,
                    metadata = "{}"
                },
                new invoice_line
                {
                    line_type = "rent",
                    description = "Tiền thuê kho kỳ đầu (tháng đầu tiên)",
                    quantity = 1,
                    unit_price = parameters.ComputedRent,
                    metadata = "{}"
                }
            };

            if (parameters.ComputedBookingFee > 0)
            {
                lines.Add(new invoice_line
                {
                    line_type = "booking_fee",
                    description = "Phí đặt chỗ ban đầu",
                    quantity = 1,
                    unit_price = parameters.ComputedBookingFee,
                    metadata = "{}"
                });
            }

            if (parameters.ComputedDiscount > 0)
            {
                lines.Add(new invoice_line
                {
                    line_type = "discount",
                    description = $"Ưu đãi giảm giá ({parameters.AppliedPromotion?.code})",
                    quantity = 1,
                    unit_price = -parameters.ComputedDiscount,
                    metadata = "{}"
                });
            }

            invoice targetInvoice;
            if (existingInvoice == null)
            {
                var today = DateOnly.FromDateTime(parameters.NowUtc.DateTime);
                var invoiceNo = GenerateInvoiceNo(reservation.facility?.code, parameters.NowUtc);

                targetInvoice = new invoice
                {
                    invoice_no = invoiceNo,
                    customer_id = parameters.CustomerId,
                    reservation_id = parameters.ReservationId,
                    issue_date = today,
                    due_date = reservation.start_date >= today ? reservation.start_date : today,
                    currency = "VND",
                    subtotal_amount = 0m,
                    discount_amount = 0m,
                    tax_amount = 0m,
                    total_amount = 0m,
                    paid_amount = 0m,
                    status = "draft",
                    created_at = parameters.NowUtc,
                    updated_at = parameters.NowUtc
                };

                _context.invoices.Add(targetInvoice);
                await _context.SaveChangesAsync(cancellationToken);

                foreach (var line in lines)
                {
                    line.invoice_id = targetInvoice.id;
                }
                _context.invoice_lines.AddRange(lines);
                await _context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                targetInvoice = existingInvoice;

                // Sync existing invoice lines with modified quote / voucher
                _context.invoice_lines.RemoveRange(targetInvoice.invoice_lines);
                await _context.SaveChangesAsync(cancellationToken);

                foreach (var line in lines)
                {
                    line.invoice_id = targetInvoice.id;
                }
                _context.invoice_lines.AddRange(lines);
                await _context.SaveChangesAsync(cancellationToken);
            }

            // For in-memory database where SQL triggers do not fire, manually compute totals
            if (!isRelational)
            {
                var subtotal = parameters.ComputedDeposit + parameters.ComputedRent + parameters.ComputedBookingFee;
                var discount = parameters.ComputedDiscount;
                targetInvoice.subtotal_amount = subtotal;
                targetInvoice.discount_amount = discount;
                targetInvoice.total_amount = subtotal - discount;
            }
            else
            {
                // Reload invoice updated by trg_invoice_lines_refresh_totals
                _context.Entry(targetInvoice).State = EntityState.Detached;
                targetInvoice = await _context.invoices
                    .FirstAsync(i => i.id == targetInvoice.id, cancellationToken);
            }

            if (targetInvoice.total_amount != parameters.ComputedTotal)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException($"INVOICE_TOTAL_MISMATCH: Computed {parameters.ComputedTotal} vs DB {targetInvoice.total_amount}");
            }

            targetInvoice.status = "open";
            targetInvoice.opened_at = parameters.NowUtc;
            targetInvoice.updated_at = parameters.NowUtc;
            await _context.SaveChangesAsync(cancellationToken);

            // 6. Create payment attempt record with status 'initiated'
            var payment = new payment
            {
                customer_id = parameters.CustomerId,
                target_invoice_id = targetInvoice.id,
                amount = targetInvoice.total_amount,
                currency = "VND",
                method = parameters.PaymentMethod,
                provider = parameters.PaymentProvider,
                idempotency_key = parameters.IdempotencyKey,
                status = "initiated",
                metadata = parameters.EvidenceMetadataJson,
                created_at = parameters.NowUtc,
                updated_at = parameters.NowUtc
            };

            _context.payments.Add(payment);
            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new CheckoutTransactionResult
            {
                IsIdempotentReplay = false,
                Payment = payment,
                Invoice = targetInvoice,
                Reservation = reservation
            };
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
            inMemoryPromoSemaphore?.Release();
            inMemoryResSemaphore?.Release();
        }
    }

    public async Task UpdatePaymentStatusToPendingAsync(
        long paymentId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var payment = await _context.payments.FirstOrDefaultAsync(p => p.id == paymentId, cancellationToken);
        if (payment != null && payment.status == "initiated")
        {
            payment.status = "pending";
            payment.updated_at = nowUtc;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<FinalizePaymentResult> ExecuteFinalizePaymentTransactionAsync(
        FinalizePaymentParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var payment = await _context.payments
            .Include(p => p.target_invoice)
                .ThenInclude(inv => inv.reservation)
                    .ThenInclude(r => r!.facility)
            .FirstOrDefaultAsync(p => p.id == parameters.PaymentId, cancellationToken);

        if (payment == null)
        {
            throw new InvalidOperationException("PAYMENT_NOT_FOUND");
        }

        var reservationId = payment.target_invoice.reservation_id ?? 0;
        var lockKey = $"SS_Reservation_{reservationId}";
        var isRelational = _context.Database.IsRelational();

        SemaphoreSlim? inMemorySemaphore = null;
        if (!isRelational)
        {
            inMemorySemaphore = InMemorySemaphores.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            var entered = await inMemorySemaphore.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!entered)
            {
                throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

                var resultParam = new SqlParameter
                {
                    ParameterName = "@Result",
                    SqlDbType = SqlDbType.Int,
                    Direction = ParameterDirection.Output
                };
                var resourceParam = new SqlParameter("@Resource", lockKey);

                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000",
                    new object[] { resultParam, resourceParam },
                    cancellationToken);

                var lockResult = (int)(resultParam.Value ?? -999);
                if (lockResult < 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
                }
            }

            // Check if integration event has already been processed (idempotency)
            var existingEvent = await _context.integration_events
                .FirstOrDefaultAsync(e => e.source == parameters.Provider &&
                                          e.external_event_id == parameters.ExternalEventId,
                                          cancellationToken);

            if (existingEvent != null && existingEvent.status == "processed")
            {
                var existingAgreement = await _context.rental_agreements
                    .FirstOrDefaultAsync(a => a.reservation_id == reservationId, cancellationToken);

                if (transaction != null) await transaction.CommitAsync(cancellationToken);

                return new FinalizePaymentResult
                {
                    IsAlreadyProcessed = true,
                    Payment = payment,
                    Invoice = payment.target_invoice,
                    Reservation = payment.target_invoice.reservation!,
                    Agreement = existingAgreement
                };
            }

            // Reload fresh state from DB under the exclusive lock (Item 2)
            _context.Entry(payment).State = EntityState.Detached;
            if (payment.target_invoice != null)
            {
                _context.Entry(payment.target_invoice).State = EntityState.Detached;
                if (payment.target_invoice.reservation != null)
                {
                    _context.Entry(payment.target_invoice.reservation).State = EntityState.Detached;
                }
            }

            var freshPayment = await _context.payments
                .Include(p => p.target_invoice)
                    .ThenInclude(inv => inv.reservation)
                        .ThenInclude(r => r!.facility)
                .FirstOrDefaultAsync(p => p.id == parameters.PaymentId, cancellationToken);

            if (freshPayment == null)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("PAYMENT_NOT_FOUND");
            }

            payment = freshPayment;
            var invoice = payment.target_invoice;
            var reservation = invoice?.reservation;

            if (invoice == null || reservation == null)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("RESERVATION_NOT_FOUND");
            }

            // Idempotent check on payment status
            if (payment.status == "succeeded")
            {
                var existingAgreement = await _context.rental_agreements
                    .FirstOrDefaultAsync(a => a.reservation_id == reservationId, cancellationToken);

                if (transaction != null) await transaction.CommitAsync(cancellationToken);

                return new FinalizePaymentResult
                {
                    IsAlreadyProcessed = true,
                    Payment = payment,
                    Invoice = invoice,
                    Reservation = reservation,
                    Agreement = existingAgreement
                };
            }

            // Check if reservation hold is expired, cancelled, or invoice voided/amount mismatch (Issue 2)
            var nowUtc = DateTimeOffset.UtcNow;
            var isHoldExpiredOrCancelled = reservation.hold_until <= nowUtc ||
                                           reservation.status == "cancelled" ||
                                           reservation.status == "expired";
            var isInvoiceVoidedOrMismatch = invoice.status == "voided" || invoice.total_amount != payment.amount;

            if (isHoldExpiredOrCancelled || isInvoiceVoidedOrMismatch)
            {
                var failureReason = isHoldExpiredOrCancelled
                    ? "LATE_PAYMENT_HOLD_EXPIRED"
                    : (invoice.status == "voided" ? "INVOICE_VOIDED" : "INVOICE_AMOUNT_MISMATCH");

                var reasonText = isHoldExpiredOrCancelled
                    ? "Khoản thanh toán đến sau khi hold giữ chỗ đã hết hạn/hủy (Cần đối soát & hoàn tiền)"
                    : $"Khoản thanh toán không khớp hóa đơn hiệu lực ({failureReason}) (Cần đối soát & hoàn tiền)";

                var reconReason = isHoldExpiredOrCancelled
                    ? "Hold đã hết hạn trước khi thanh toán hoàn tất. Hệ thống đã ghi nhận khoản tiền và tạo yêu cầu hoàn tiền để nhân sự đối soát."
                    : $"Hóa đơn đã bị hủy hoặc số tiền thanh toán không khớp với hóa đơn hiện hành ({failureReason}). Hệ thống đã ghi nhận khoản tiền và tạo yêu cầu hoàn tiền để nhân sự đối soát.";

                // Provider confirmed money, but hold expired/cancelled or invoice voided/mismatched!
                // DO NOT restore reservation!
                // DO NOT create agreement!
                // DO NOT allocate payment to invoice!
                payment.status = "succeeded";
                payment.paid_at = parameters.PaidAt;
                payment.provider_transaction_id = parameters.ProviderTransactionId;
                payment.failure_reason = failureReason;
                payment.updated_at = nowUtc;

                var metaDict = SafeParseJson(payment.metadata);
                metaDict["reconciliation_required"] = true;
                metaDict["reconciliation_reason"] = reconReason;
                metaDict["provider_transaction_id"] = parameters.ProviderTransactionId ?? string.Empty;
                payment.metadata = JsonSerializer.Serialize(metaDict);

                // Create refund request for staff review/approval
                var refundKey = $"REFUND-{payment.id}-{Guid.NewGuid():N}".Substring(0, 32);
                var refundRequest = new refund
                {
                    payment_id = payment.id,
                    agreement_id = null,
                    amount = payment.amount,
                    currency = "VND",
                    reason = reasonText,
                    provider = parameters.Provider,
                    idempotency_key = refundKey,
                    status = "requested",
                    created_at = nowUtc,
                    updated_at = nowUtc
                };
                _context.refunds.Add(refundRequest);

                // Record integration event
                if (existingEvent == null)
                {
                    var lateEvent = new integration_event
                    {
                        source = parameters.Provider,
                        external_event_id = parameters.ExternalEventId,
                        event_type = "payment_succeeded_reconciliation",
                        payload = parameters.RawPayloadJson,
                        status = "processed",
                        received_at = nowUtc,
                        processed_at = nowUtc
                    };
                    _context.integration_events.Add(lateEvent);
                }
                else
                {
                    existingEvent.status = "processed";
                    existingEvent.processed_at = nowUtc;
                }

                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null) await transaction.CommitAsync(cancellationToken);

                return new FinalizePaymentResult
                {
                    IsAlreadyProcessed = false,
                    ReconciliationRequired = true,
                    ReconciliationReason = reconReason,
                    Payment = payment,
                    Invoice = invoice,
                    Reservation = reservation,
                    Agreement = null
                };
            }

            // Check if invoice already paid or reservation confirmed by another attempt -> SECOND / DUPLICATE PAYMENT CASE (Finding 3)
            var isInvoiceAlreadyPaid = invoice.status == "paid" || invoice.paid_amount >= invoice.total_amount;
            var isReservationAlreadyConfirmed = reservation.status == "confirmed";

            if (isInvoiceAlreadyPaid || isReservationAlreadyConfirmed)
            {
                payment.status = "succeeded";
                payment.paid_at = parameters.PaidAt;
                payment.provider_transaction_id = parameters.ProviderTransactionId;
                payment.failure_reason = "DUPLICATE_PAYMENT_ALREADY_PAID";
                payment.updated_at = nowUtc;

                var metaDict = SafeParseJson(payment.metadata);
                metaDict["reconciliation_required"] = true;
                metaDict["reconciliation_reason"] = "Hóa đơn hoặc đơn đặt chỗ đã được thanh toán bởi một giao dịch khác. Tiền trùng cần hoàn lại.";
                metaDict["provider_transaction_id"] = parameters.ProviderTransactionId ?? string.Empty;
                payment.metadata = JsonSerializer.Serialize(metaDict);

                var refundKey = $"REFUND-{payment.id}-{Guid.NewGuid():N}".Substring(0, 32);
                var refundRequest = new refund
                {
                    payment_id = payment.id,
                    agreement_id = null,
                    amount = payment.amount,
                    currency = "VND",
                    reason = "Thanh toán trùng/thừa cho đơn đặt chỗ đã hoàn tất (Cần đối soát & hoàn tiền)",
                    provider = parameters.Provider,
                    idempotency_key = refundKey,
                    status = "requested",
                    created_at = nowUtc,
                    updated_at = nowUtc
                };
                _context.refunds.Add(refundRequest);

                if (existingEvent == null)
                {
                    var dupEvent = new integration_event
                    {
                        source = parameters.Provider,
                        external_event_id = parameters.ExternalEventId,
                        event_type = "payment_succeeded_duplicate_reconciliation",
                        payload = parameters.RawPayloadJson,
                        status = "processed",
                        received_at = nowUtc,
                        processed_at = nowUtc
                    };
                    _context.integration_events.Add(dupEvent);
                }
                else
                {
                    existingEvent.status = "processed";
                    existingEvent.processed_at = nowUtc;
                }

                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null) await transaction.CommitAsync(cancellationToken);

                return new FinalizePaymentResult
                {
                    IsAlreadyProcessed = false,
                    ReconciliationRequired = true,
                    ReconciliationReason = "Đơn đặt chỗ hoặc hóa đơn đã được thanh toán bởi một giao dịch khác. Giao dịch này đã được ghi nhận và tạo yêu cầu hoàn tiền đối soát.",
                    Payment = payment,
                    Invoice = invoice,
                    Reservation = reservation,
                    Agreement = null
                };
            }

            // NORMAL SUCCESSFUL PAYMENT CASE
            payment.status = "succeeded";
            payment.paid_at = parameters.PaidAt;
            payment.provider_transaction_id = parameters.ProviderTransactionId;
            payment.updated_at = parameters.PaidAt;

            var effectiveAmount = parameters.Amount > 0 ? parameters.Amount : payment.amount;

            // Add payment allocation
            var allocation = new payment_allocation
            {
                payment_id = payment.id,
                invoice_id = invoice.id,
                allocated_amount = effectiveAmount,
                allocated_at = parameters.PaidAt
            };
            _context.payment_allocations.Add(allocation);
            await _context.SaveChangesAsync(cancellationToken);

            // In SQL Server, trg_payment_allocation_guard automatically updates invoice paid_amount and status to 'paid'.
            // For in-memory test database:
            if (!isRelational)
            {
                invoice.paid_amount += effectiveAmount;
                invoice.status = invoice.paid_amount >= invoice.total_amount ? "paid" : "partially_paid";
                invoice.updated_at = parameters.PaidAt;
            }
            else
            {
                _context.Entry(invoice).State = EntityState.Detached;
                invoice = await _context.invoices.FirstAsync(i => i.id == invoice.id, cancellationToken);
            }

            // Strictly verify that invoice is paid and paid_amount equals total_amount before confirming reservation or creating agreement (Issue 2 & Item 3)
            if (invoice.status != "paid" || invoice.paid_amount != invoice.total_amount)
            {
                var reconReason = $"Hóa đơn chỉ mới được thanh toán một phần ({invoice.paid_amount}/{invoice.total_amount}). Chưa đủ điều kiện kích hoạt hợp đồng.";
                payment.failure_reason = "PARTIAL_PAYMENT_UNDERPAID";
                payment.updated_at = parameters.PaidAt;

                var metaDict = SafeParseJson(payment.metadata);
                metaDict["reconciliation_required"] = true;
                metaDict["reconciliation_reason"] = reconReason;
                metaDict["provider_transaction_id"] = parameters.ProviderTransactionId ?? string.Empty;
                payment.metadata = JsonSerializer.Serialize(metaDict);

                var refundKey = $"REFUND-{payment.id}-{Guid.NewGuid():N}".Substring(0, 32);
                var refundRequest = new refund
                {
                    payment_id = payment.id,
                    agreement_id = null,
                    amount = effectiveAmount,
                    currency = "VND",
                    reason = $"Thanh toán thiếu tiền ({invoice.paid_amount}/{invoice.total_amount}) cho đơn đặt chỗ (Cần đối soát & hoàn tiền)",
                    provider = parameters.Provider,
                    idempotency_key = refundKey,
                    status = "requested",
                    created_at = parameters.PaidAt,
                    updated_at = parameters.PaidAt
                };
                _context.refunds.Add(refundRequest);

                if (existingEvent == null)
                {
                    var partialEvent = new integration_event
                    {
                        source = parameters.Provider,
                        external_event_id = parameters.ExternalEventId,
                        event_type = "payment_succeeded_partial_reconciliation",
                        payload = parameters.RawPayloadJson,
                        status = "processed",
                        received_at = parameters.PaidAt,
                        processed_at = parameters.PaidAt
                    };
                    _context.integration_events.Add(partialEvent);
                }
                else
                {
                    existingEvent.status = "processed";
                    existingEvent.processed_at = parameters.PaidAt;
                }

                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null) await transaction.CommitAsync(cancellationToken);

                return new FinalizePaymentResult
                {
                    IsAlreadyProcessed = false,
                    ReconciliationRequired = true,
                    ReconciliationReason = reconReason,
                    Payment = payment,
                    Invoice = invoice,
                    Reservation = reservation,
                    Agreement = null
                };
            }

            // Query accepted policy version from payment metadata (Finding 9)
            var paymentMeta = SafeParseJson(payment.metadata);
            long policyVersionId;
            if (paymentMeta.TryGetValue("accepted_policy_version_id", out var pvObj) &&
                long.TryParse(pvObj?.ToString(), out var parsedPvId) && parsedPvId > 0)
            {
                policyVersionId = parsedPvId;
            }
            else
            {
                var policyDate = reservation.start_date;
                var policyVersion = await _context.policy_versions
                    .Where(p => p.policy_type == "rental_terms" &&
                                p.valid_from <= policyDate &&
                                (p.valid_to == null || p.valid_to > policyDate))
                    .OrderByDescending(p => p.valid_from)
                    .ThenByDescending(p => p.id)
                    .FirstOrDefaultAsync(cancellationToken);

                policyVersionId = policyVersion?.id ?? 1;
            }

            // Generate agreement_no
            var agreementNo = GenerateAgreementNo(reservation.facility?.code, parameters.PaidAt);

            var agreement = new rental_agreement
            {
                agreement_no = agreementNo,
                reservation_id = reservation.id,
                customer_id = reservation.customer_id,
                facility_id = reservation.facility_id,
                policy_version_id = policyVersionId,
                start_date = reservation.start_date,
                end_date = reservation.end_date,
                monthly_rate_snapshot = reservation.monthly_rate_snapshot,
                deposit_snapshot = reservation.deposit_snapshot,
                deposit_balance = reservation.deposit_snapshot,
                status = "scheduled", // Scheduled awaiting digital handover in Function 5
                signed_at = null,     // Signed at is null in Function 4
                checked_in_at = null,
                checked_out_at = null,
                actual_end_date = null,
                created_at = parameters.PaidAt,
                updated_at = parameters.PaidAt
            };

            _context.rental_agreements.Add(agreement);
            await _context.SaveChangesAsync(cancellationToken);

            // Link invoice with agreement
            invoice.agreement_id = agreement.id;
            invoice.updated_at = parameters.PaidAt;

            // Confirm reservation
            reservation.status = "confirmed";
            reservation.confirmed_at = parameters.PaidAt;
            reservation.updated_at = parameters.PaidAt;

            // Apply promotion redemption ONLY if this payment had a promotion applied (Issue 4)
            if (paymentMeta.TryGetValue("promotion_id", out var promoIdObj) &&
                long.TryParse(promoIdObj?.ToString(), out var promoId) && promoId > 0)
            {
                var redemption = await _context.promotion_redemptions
                    .FirstOrDefaultAsync(pr => pr.reservation_id == reservation.id &&
                                               pr.promotion_id == promoId &&
                                               pr.status == "reserved", cancellationToken);

                if (redemption != null)
                {
                    redemption.status = "applied";
                    redemption.invoice_id = invoice.id;
                }
            }

            // Save integration event
            if (existingEvent == null)
            {
                var successEvent = new integration_event
                {
                    source = parameters.Provider,
                    external_event_id = parameters.ExternalEventId,
                    event_type = "payment_succeeded",
                    payload = parameters.RawPayloadJson,
                    status = "processed",
                    received_at = parameters.PaidAt,
                    processed_at = parameters.PaidAt
                };
                _context.integration_events.Add(successEvent);
            }
            else
            {
                existingEvent.status = "processed";
                existingEvent.processed_at = parameters.PaidAt;
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new FinalizePaymentResult
            {
                IsAlreadyProcessed = false,
                ReconciliationRequired = false,
                Payment = payment,
                Invoice = invoice,
                Reservation = reservation,
                Agreement = agreement
            };
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
            inMemorySemaphore?.Release();
        }
    }

    public async Task<bool> ExecuteFinalizeFailedPaymentTransactionAsync(
        FinalizeFailedPaymentParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var payment = await _context.payments
            .Include(p => p.target_invoice)
            .FirstOrDefaultAsync(p => p.id == parameters.PaymentId, cancellationToken);

        if (payment == null) return false;

        // If payment is already terminal, no update needed
        if (payment.status == "succeeded" || payment.status == "cancelled")
        {
            return false;
        }

        payment.status = "failed";
        payment.failure_reason = parameters.FailureReason;
        payment.updated_at = parameters.FailedAt;

        var existingEvent = await _context.integration_events
            .FirstOrDefaultAsync(e => e.source == parameters.Provider &&
                                      e.external_event_id == parameters.ExternalEventId,
                                      cancellationToken);

        if (existingEvent == null)
        {
            var failedEvent = new integration_event
            {
                source = parameters.Provider,
                external_event_id = parameters.ExternalEventId,
                event_type = "payment_failed",
                payload = parameters.RawPayloadJson,
                status = "processed",
                received_at = parameters.FailedAt,
                processed_at = parameters.FailedAt
            };
            _context.integration_events.Add(failedEvent);
        }
        else
        {
            existingEvent.status = "processed";
            existingEvent.processed_at = parameters.FailedAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<refund>> GetRefundsAsync(
        List<long>? accessibleFacilityIds,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var query = _context.refunds
            .Include(r => r.refund_approval)
                .ThenInclude(ra => ra!.decided_byNavigation)
            .Include(r => r.payment)
                .ThenInclude(p => p.target_invoice)
                    .ThenInclude(inv => inv.reservation)
            .Include(r => r.agreement)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var cleanStatus = status.Trim().ToLowerInvariant();
            query = query.Where(r => r.status == cleanStatus);
        }

        if (accessibleFacilityIds != null)
        {
            query = query.Where(r =>
                (r.payment.target_invoice != null &&
                 r.payment.target_invoice.reservation != null &&
                 accessibleFacilityIds.Contains(r.payment.target_invoice.reservation.facility_id))
                ||
                (r.agreement != null &&
                 accessibleFacilityIds.Contains(r.agreement.facility_id)));
        }

        return await query.OrderByDescending(r => r.created_at).ToListAsync(cancellationToken);
    }

    public async Task<refund?> GetRefundByIdAsync(
        long refundId,
        CancellationToken cancellationToken = default)
    {
        var refRecord = await _context.refunds
            .Include(r => r.refund_approval)
                .ThenInclude(ra => ra!.decided_byNavigation)
            .Include(r => r.payment)
            .Include(r => r.agreement)
                .ThenInclude(a => a!.facility)
            .FirstOrDefaultAsync(r => r.id == refundId, cancellationToken);

        if (refRecord?.payment != null && refRecord.payment.target_invoice_id > 0)
        {
            await _context.Entry(refRecord.payment)
                .Reference(p => p.target_invoice)
                .Query()
                .Include(i => i.reservation)
                    .ThenInclude(res => res!.facility)
                .LoadAsync(cancellationToken);
        }

        return refRecord;
    }

    public async Task<refund> ReviewRefundAsync(
        long refundId,
        long employeeUserId,
        string decision,
        string? reason,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default)
    {
        var cleanDecision = decision.Trim().ToLowerInvariant();
        if (cleanDecision != "approved" && cleanDecision != "rejected")
        {
            throw new ArgumentException("Quyết định chỉ có thể là 'approved' hoặc 'rejected'.", nameof(decision));
        }

        var isRelational = _context.Database.IsRelational();
        var lockKey = $"SS_Refund_{refundId}";

        SemaphoreSlim? inMemorySemaphore = null;
        if (!isRelational)
        {
            inMemorySemaphore = InMemorySemaphores.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            var entered = await inMemorySemaphore.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!entered) throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                var resultParam = new SqlParameter
                {
                    ParameterName = "@Result",
                    SqlDbType = SqlDbType.Int,
                    Direction = ParameterDirection.Output
                };
                var resourceParam = new SqlParameter("@Resource", lockKey);

                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000",
                    new object[] { resultParam, resourceParam },
                    cancellationToken);

                var lockResult = (int)(resultParam.Value ?? -999);
                if (lockResult < 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
                }
            }

            var refRecord = await _context.refunds
                .Include(r => r.refund_approval)
                .Include(r => r.payment)
                    .ThenInclude(p => p.target_invoice)
                        .ThenInclude(inv => inv.reservation)
                .Include(r => r.agreement)
                .FirstOrDefaultAsync(r => r.id == refundId, cancellationToken);

            if (refRecord == null)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("REFUND_NOT_FOUND");
            }

            if (refRecord.status != "requested" || refRecord.refund_approval != null)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException($"REFUND_ALREADY_DECIDED:{refRecord.status}");
            }

            refRecord.status = cleanDecision;
            refRecord.updated_at = decidedAt;

            var approval = new refund_approval
            {
                refund_id = refundId,
                decision = cleanDecision,
                decided_by = employeeUserId,
                reason = reason,
                decided_at = decidedAt
            };

            _context.refund_approvals.Add(approval);
            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null) await transaction.CommitAsync(cancellationToken);

            return refRecord;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
            inMemorySemaphore?.Release();
        }
    }

    private static string GenerateInvoiceNo(string? facilityCode, DateTimeOffset nowUtc)
    {
        var cleanCode = string.IsNullOrWhiteSpace(facilityCode) ? "FAC" : facilityCode.Trim().ToUpperInvariant();
        var datePart = nowUtc.ToString("yyyyMMdd");
        var randomHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToUpperInvariant();
        return $"INV-{cleanCode}-{datePart}-{randomHex}";
    }

    private static string GenerateAgreementNo(string? facilityCode, DateTimeOffset nowUtc)
    {
        var cleanCode = string.IsNullOrWhiteSpace(facilityCode) ? "FAC" : facilityCode.Trim().ToUpperInvariant();
        var datePart = nowUtc.ToString("yyyyMMdd");
        var randomHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToUpperInvariant();
        return $"AGR-{cleanCode}-{datePart}-{randomHex}";
    }

    private static Dictionary<string, object> SafeParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }
}
