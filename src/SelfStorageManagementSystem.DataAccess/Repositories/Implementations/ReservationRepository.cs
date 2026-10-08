using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Implementations;

public class ReservationRepository : GenericRepository<reservation>, IReservationRepository
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemorySemaphores = new();

    public ReservationRepository(SelfStorageDbContext context) : base(context)
    {
    }

    public async Task<reservation?> GetByIdWithDetailsAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        return await _context.reservations
            .AsNoTracking()
            .Include(r => r.customer)
                .ThenInclude(c => c.user)
            .Include(r => r.facility)
            .Include(r => r.unit_type)
            .Include(r => r.facility_rate)
            .FirstOrDefaultAsync(r => r.id == id, cancellationToken);
    }

    public async Task<(IReadOnlyList<reservation> Items, int TotalCount)> GetPagedByCustomerAsync(
        long customerId,
        string? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.reservations
            .AsNoTracking()
            .Include(r => r.facility)
            .Include(r => r.unit_type)
            .Where(r => r.customer_id == customerId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLower();
            query = query.Where(r => r.status.ToLower() == normalizedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.created_at)
            .ThenByDescending(r => r.id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<facility_rate?> GetActiveFacilityRateAsync(
        long facilityId,
        long unitTypeId,
        DateOnly rentalDate,
        CancellationToken cancellationToken = default)
    {
        return await _context.facility_rates
            .AsNoTracking()
            .Include(fr => fr.unit_type)
            .FirstOrDefaultAsync(fr =>
                fr.facility_id == facilityId &&
                fr.unit_type_id == unitTypeId &&
                fr.valid_from <= rentalDate &&
                (fr.valid_to == null || rentalDate < fr.valid_to),
                cancellationToken);
    }

    public async Task<customer_profile?> GetCustomerProfileAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _context.customer_profiles
            .Include(cp => cp.user)
                .ThenInclude(u => u.user_roleusers)
                    .ThenInclude(ur => ur.role)
            .FirstOrDefaultAsync(cp => cp.user_id == userId, cancellationToken);

        if (profile?.user != null && (!profile.user.user_roleusers.Any() || profile.user.user_roleusers.Any(ur => ur.role == null)))
        {
            var userRoles = await _context.user_roles
                .Include(ur => ur.role)
                .Where(ur => ur.user_id == userId)
                .ToListAsync(cancellationToken);

            profile.user.user_roleusers = userRoles;
        }

        return profile;
    }

    public async Task<facility?> GetFacilityAsync(
        long facilityId,
        CancellationToken cancellationToken = default)
    {
        return await _context.facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.id == facilityId, cancellationToken);
    }

    public async Task<unit_type?> GetUnitTypeAsync(
        long unitTypeId,
        CancellationToken cancellationToken = default)
    {
        return await _context.unit_types
            .AsNoTracking()
            .FirstOrDefaultAsync(ut => ut.id == unitTypeId, cancellationToken);
    }

    public async Task<int> GetAvailableCapacityAsync(
        long facilityId,
        long unitTypeId,
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var candidateUnits = await CountCandidateAvailableUnitsAsync(
            facilityId,
            unitTypeId,
            startDate,
            endDate,
            cancellationToken);

        var activeReservations = await CountUnassignedActiveReservationsAsync(
            facilityId,
            unitTypeId,
            startDate,
            endDate,
            nowUtc,
            cancellationToken);

        return Math.Max(0, candidateUnits - activeReservations);
    }

    public async Task<reservation> CreateReservationHoldWithLockAsync(
        reservation newReservation,
        CancellationToken cancellationToken = default)
    {
        var lockKey = $"SS_Facility_{newReservation.facility_id}_UnitType_{newReservation.unit_type_id}_Capacity";
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
                transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

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

            var nowUtc = DateTimeOffset.UtcNow;

            // Recompute available capacity inside the serialized transaction scope
            var candidateUnits = await _context.storage_units
                .Where(u =>
                    u.facility_id == newReservation.facility_id &&
                    u.unit_type_id == newReservation.unit_type_id &&
                    u.unit_type.is_active &&
                    u.is_listed &&
                    u.physical_status == "available" &&
                    !u.maintenance_work_orders.Any(m =>
                        m.blocks_booking &&
                        m.status != "completed" &&
                        m.status != "cancelled") &&
                    !u.unit_allocations.Any(a =>
                        a.status == "active" &&
                        a.allocation_start_date < newReservation.end_date &&
                        a.allocation_end_date > newReservation.start_date))
                .CountAsync(cancellationToken);

            var activeReservations = await _context.reservations
                .Where(r =>
                    r.facility_id == newReservation.facility_id &&
                    r.unit_type_id == newReservation.unit_type_id &&
                    r.start_date < newReservation.end_date &&
                    r.end_date > newReservation.start_date &&
                    (r.status == "confirmed" ||
                     ((r.status == "pending" || r.status == "awaiting_deposit") && r.hold_until > nowUtc)) &&
                    !_context.unit_allocations.Any(ua => ua.reservation_id == r.id && ua.status == "active"))
                .CountAsync(cancellationToken);

            var availableCapacity = candidateUnits - activeReservations;
            if (availableCapacity <= 0)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                throw new InvalidOperationException("CAPACITY_EXHAUSTED");
            }

            _context.reservations.Add(newReservation);
            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return newReservation;
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
            inMemorySemaphore?.Release();
        }
    }

    public async Task<reservation> CancelReservationAsync(
        long reservationId,
        string? cancellationReason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var cleanReason = string.IsNullOrWhiteSpace(cancellationReason)
            ? "Customer requested cancellation"
            : cancellationReason.Trim();

        var isRelational = _context.Database.IsRelational();
        IDbContextTransaction? transaction = null;
        if (isRelational)
        {
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        }

        try
        {
            var current = await _context.reservations
                .FirstOrDefaultAsync(x => x.id == reservationId, cancellationToken);

            if (current == null)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("RESERVATION_NOT_FOUND");
            }

            if (current.status == "cancelled")
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                _context.Entry(current).State = EntityState.Detached;
                var loadedCancelled = await _context.reservations
                    .AsNoTracking()
                    .Include(r => r.customer)
                        .ThenInclude(c => c.user)
                    .Include(r => r.facility)
                    .Include(r => r.unit_type)
                    .Include(r => r.facility_rate)
                    .FirstOrDefaultAsync(x => x.id == reservationId, cancellationToken);
                return loadedCancelled ?? current; // Idempotent success with complete navigation details
            }

            if (current.status == "confirmed")
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("CANNOT_CANCEL_CONFIRMED");
            }

            if (current.status == "expired" ||
                ((current.status == "pending" || current.status == "awaiting_deposit") && current.hold_until <= nowUtc))
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("CANNOT_CANCEL_EXPIRED");
            }

            if (current.status is not ("pending" or "awaiting_deposit"))
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException($"CANNOT_CANCEL_STATUS_{current.status.ToUpperInvariant()}");
            }

            // Atomically update ONLY if still pending/awaiting_deposit and hold_until > nowUtc
            int affected;
            if (isRelational)
            {
                affected = await _context.reservations
                    .Where(r => r.id == reservationId &&
                                (r.status == "pending" || r.status == "awaiting_deposit") &&
                                r.hold_until > nowUtc)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.status, "cancelled")
                        .SetProperty(b => b.cancelled_at, nowUtc)
                        .SetProperty(b => b.cancellation_reason, cleanReason)
                        .SetProperty(b => b.updated_at, nowUtc),
                        cancellationToken);
            }
            else
            {
                var r = await _context.reservations
                    .FirstOrDefaultAsync(x => x.id == reservationId &&
                                (x.status == "pending" || x.status == "awaiting_deposit") &&
                                x.hold_until > nowUtc, cancellationToken);
                if (r != null)
                {
                    r.status = "cancelled";
                    r.cancelled_at = nowUtc;
                    r.cancellation_reason = cleanReason;
                    r.updated_at = nowUtc;
                    await _context.SaveChangesAsync(cancellationToken);
                    affected = 1;
                }
                else
                {
                    affected = 0;
                }
            }

            if (affected == 0)
            {
                // Re-evaluate state after race condition
                _context.Entry(current).State = EntityState.Detached;
                var refreshed = await _context.reservations
                    .AsNoTracking()
                    .Include(r => r.customer)
                        .ThenInclude(c => c.user)
                    .Include(r => r.facility)
                    .Include(r => r.unit_type)
                    .Include(r => r.facility_rate)
                    .FirstOrDefaultAsync(x => x.id == reservationId, cancellationToken);

                if (transaction != null) await transaction.RollbackAsync(cancellationToken);

                if (refreshed?.status == "cancelled") return refreshed;
                if (refreshed?.status == "confirmed") throw new InvalidOperationException("CANNOT_CANCEL_CONFIRMED");
                throw new InvalidOperationException("CANNOT_CANCEL_EXPIRED");
            }

            // Void unpaid draft/open invoices and release reserved vouchers for this cancelled reservation
            if (isRelational)
            {
                await _context.invoices
                    .Where(inv => inv.reservation_id == reservationId &&
                                  (inv.status == "draft" || inv.status == "open") &&
                                  inv.paid_amount == 0)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.status, "voided")
                        .SetProperty(b => b.voided_at, nowUtc)
                        .SetProperty(b => b.updated_at, nowUtc),
                        cancellationToken);

                await _context.promotion_redemptions
                    .Where(pr => pr.reservation_id == reservationId && pr.status == "reserved")
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.status, "released"),
                        cancellationToken);
            }
            else
            {
                var invs = await _context.invoices
                    .Where(inv => inv.reservation_id == reservationId &&
                                  (inv.status == "draft" || inv.status == "open") &&
                                  inv.paid_amount == 0)
                    .ToListAsync(cancellationToken);

                foreach (var inv in invs)
                {
                    inv.status = "voided";
                    inv.voided_at = nowUtc;
                    inv.updated_at = nowUtc;
                }

                var redemptions = await _context.promotion_redemptions
                    .Where(pr => pr.reservation_id == reservationId && pr.status == "reserved")
                    .ToListAsync(cancellationToken);

                foreach (var pr in redemptions)
                {
                    pr.status = "released";
                }

                if (invs.Count > 0 || redemptions.Count > 0)
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }

            if (transaction != null) await transaction.CommitAsync(cancellationToken);

            // Detach tracked entity to ensure ChangeTracker does not retain stale state
            _context.Entry(current).State = EntityState.Detached;

            var updated = await _context.reservations
                .AsNoTracking()
                .Include(r => r.customer)
                    .ThenInclude(c => c.user)
                .Include(r => r.facility)
                .Include(r => r.unit_type)
                .Include(r => r.facility_rate)
                .FirstOrDefaultAsync(x => x.id == reservationId, cancellationToken);

            return updated ?? current;
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
    }

    public async Task<int> ExpireOverdueReservationHoldsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var isRelational = _context.Database.IsRelational();
        IDbContextTransaction? transaction = null;
        if (isRelational)
        {
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        }

        try
        {
            var candidateIds = await _context.reservations
                .Where(r => (r.status == "pending" || r.status == "awaiting_deposit") && r.hold_until <= nowUtc)
                .Select(r => r.id)
                .ToListAsync(cancellationToken);

            if (candidateIds.Count == 0)
            {
                if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                return 0;
            }

            var actuallyExpiredIds = new List<long>();
            if (isRelational)
            {
                foreach (var id in candidateIds)
                {
                    var lockKey = $"SS_Reservation_{id}";
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
                        continue;
                    }

                    var affected = await _context.reservations
                        .Where(r => r.id == id &&
                                    (r.status == "pending" || r.status == "awaiting_deposit") &&
                                    r.hold_until <= nowUtc)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.status, "expired")
                            .SetProperty(b => b.updated_at, nowUtc),
                            cancellationToken);

                    if (affected > 0)
                    {
                        actuallyExpiredIds.Add(id);
                    }
                }

                if (actuallyExpiredIds.Count > 0)
                {
                    await _context.invoices
                        .Where(inv => inv.reservation_id.HasValue &&
                                      actuallyExpiredIds.Contains(inv.reservation_id.Value) &&
                                      (inv.status == "draft" || inv.status == "open") &&
                                      inv.paid_amount == 0)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.status, "voided")
                            .SetProperty(b => b.voided_at, nowUtc)
                            .SetProperty(b => b.updated_at, nowUtc),
                            cancellationToken);

                    await _context.promotion_redemptions
                        .Where(pr => pr.reservation_id.HasValue &&
                                      actuallyExpiredIds.Contains(pr.reservation_id.Value) &&
                                      pr.status == "reserved")
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.status, "released"),
                            cancellationToken);
                }
            }
            else
            {
                var candidateEntities = await _context.reservations
                    .Where(r => candidateIds.Contains(r.id) &&
                                (r.status == "pending" || r.status == "awaiting_deposit") &&
                                r.hold_until <= nowUtc)
                    .ToListAsync(cancellationToken);

                foreach (var r in candidateEntities)
                {
                    r.status = "expired";
                    r.updated_at = nowUtc;
                    actuallyExpiredIds.Add(r.id);
                }

                if (actuallyExpiredIds.Count > 0)
                {
                    var invs = await _context.invoices
                        .Where(inv => inv.reservation_id.HasValue &&
                                      actuallyExpiredIds.Contains(inv.reservation_id.Value) &&
                                      (inv.status == "draft" || inv.status == "open") &&
                                      inv.paid_amount == 0)
                        .ToListAsync(cancellationToken);

                    foreach (var inv in invs)
                    {
                        inv.status = "voided";
                        inv.voided_at = nowUtc;
                        inv.updated_at = nowUtc;
                    }

                    var redemptions = await _context.promotion_redemptions
                        .Where(pr => pr.reservation_id.HasValue &&
                                      actuallyExpiredIds.Contains(pr.reservation_id.Value) &&
                                      pr.status == "reserved")
                        .ToListAsync(cancellationToken);

                    foreach (var pr in redemptions)
                    {
                        pr.status = "released";
                    }

                    await _context.SaveChangesAsync(cancellationToken);
                }
            }

            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return actuallyExpiredIds.Count;
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
    }

    public async Task<bool> TryExpireSingleReservationIfOverdueAsync(
        long reservationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var isRelational = _context.Database.IsRelational();
        IDbContextTransaction? transaction = null;
        if (isRelational)
        {
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        }

        try
        {
            int affected;
            if (isRelational)
            {
                affected = await _context.reservations
                    .Where(r => r.id == reservationId &&
                                (r.status == "pending" || r.status == "awaiting_deposit") &&
                                r.hold_until <= nowUtc)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.status, "expired")
                        .SetProperty(b => b.updated_at, nowUtc),
                        cancellationToken);

                if (affected == 0)
                {
                    if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                await _context.invoices
                    .Where(inv => inv.reservation_id == reservationId &&
                                  (inv.status == "draft" || inv.status == "open") &&
                                  inv.paid_amount == 0)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.status, "voided")
                        .SetProperty(b => b.voided_at, nowUtc)
                        .SetProperty(b => b.updated_at, nowUtc),
                        cancellationToken);

                await _context.promotion_redemptions
                    .Where(pr => pr.reservation_id == reservationId && pr.status == "reserved")
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.status, "released"),
                        cancellationToken);
            }
            else
            {
                var target = await _context.reservations
                    .FirstOrDefaultAsync(r => r.id == reservationId &&
                                (r.status == "pending" || r.status == "awaiting_deposit") &&
                                r.hold_until <= nowUtc, cancellationToken);

                if (target == null)
                {
                    return false;
                }

                target.status = "expired";
                target.updated_at = nowUtc;

                var invs = await _context.invoices
                    .Where(inv => inv.reservation_id == reservationId &&
                                  (inv.status == "draft" || inv.status == "open") &&
                                  inv.paid_amount == 0)
                    .ToListAsync(cancellationToken);

                foreach (var inv in invs)
                {
                    inv.status = "voided";
                    inv.voided_at = nowUtc;
                    inv.updated_at = nowUtc;
                }

                var redemptions = await _context.promotion_redemptions
                    .Where(pr => pr.reservation_id == reservationId && pr.status == "reserved")
                    .ToListAsync(cancellationToken);

                foreach (var pr in redemptions)
                {
                    pr.status = "released";
                }

                if (invs.Count > 0 || redemptions.Count > 0)
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                affected = 1;
            }

            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
    }

    private async Task<int> CountCandidateAvailableUnitsAsync(
        long facilityId,
        long unitTypeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        return await _context.storage_units
            .AsNoTracking()
            .Where(u =>
                u.facility_id == facilityId &&
                u.unit_type_id == unitTypeId &&
                u.unit_type.is_active &&
                u.is_listed &&
                u.physical_status == "available" &&
                !u.maintenance_work_orders.Any(m =>
                    m.blocks_booking &&
                    m.status != "completed" &&
                    m.status != "cancelled") &&
                !u.unit_allocations.Any(a =>
                    a.status == "active" &&
                    a.allocation_start_date < endDate &&
                    a.allocation_end_date > startDate))
            .CountAsync(cancellationToken);
    }

    private async Task<int> CountUnassignedActiveReservationsAsync(
        long facilityId,
        long unitTypeId,
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        return await _context.reservations
            .AsNoTracking()
            .Where(r =>
                r.facility_id == facilityId &&
                r.unit_type_id == unitTypeId &&
                r.start_date < endDate &&
                r.end_date > startDate &&
                (r.status == "confirmed" ||
                 ((r.status == "pending" || r.status == "awaiting_deposit") && r.hold_until > nowUtc)) &&
                !_context.unit_allocations.Any(ua => ua.reservation_id == r.id && ua.status == "active"))
            .CountAsync(cancellationToken);
    }
}
