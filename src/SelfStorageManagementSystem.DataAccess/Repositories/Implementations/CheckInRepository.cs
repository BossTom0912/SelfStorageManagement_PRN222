using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SelfStorageManagementSystem.DataAccess.Common;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Implementations;

public class CheckInRepository : GenericRepository<reservation>, ICheckInRepository
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemorySemaphores = new();

    public CheckInRepository(SelfStorageDbContext context) : base(context)
    {
    }

    public async Task<reservation?> GetReservationForCheckInAsync(
        string codeOrId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codeOrId))
        {
            return null;
        }

        var normalized = codeOrId.Trim();
        var isNumeric = long.TryParse(normalized, out var numericId);

        return await _context.reservations
            .AsNoTracking()
            .Include(r => r.customer)
                .ThenInclude(c => c.user)
            .Include(r => r.facility)
            .Include(r => r.unit_type)
            .Include(r => r.rental_agreement)
                .ThenInclude(a => a!.policy_version)
            .Include(r => r.rental_agreement)
                .ThenInclude(a => a!.unit_allocations)
                    .ThenInclude(ua => ua.storage_unit)
            .Include(r => r.rental_agreement)
                .ThenInclude(a => a!.invoices)
                    .ThenInclude(inv => inv.payment_allocations)
                        .ThenInclude(pa => pa.payment)
            .Include(r => r.rental_agreement)
                .ThenInclude(a => a!.handover_records)
            .Include(r => r.rental_agreement)
                .ThenInclude(a => a!.access_credentials)
            .Include(r => r.identity_verifications)
                .ThenInclude(iv => iv.verified_byNavigation)
                    .ThenInclude(ep => ep.user)
            .Include(r => r.inspections)
                .ThenInclude(ins => ins.inspection_items)
            .FirstOrDefaultAsync(r =>
                r.reservation_code == normalized ||
                (isNumeric && r.id == numericId) ||
                (r.rental_agreement != null && r.rental_agreement.agreement_no == normalized),
                cancellationToken);
    }

    public async Task<List<storage_unit>> GetEligibleUnitsAsync(
        long facilityId,
        long unitTypeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        return await _context.storage_units
            .AsNoTracking()
            .Include(u => u.unit_type)
            .Include(u => u.area)
            .Include(u => u.unit_map_position)
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
            .OrderBy(u => u.unit_code)
            .ThenBy(u => u.id)
            .ToListAsync(cancellationToken);
    }

    public async Task<VerifyIdentityOutcome> VerifyIdentityWithLockAsync(
        long reservationId,
        long staffUserId,
        string method,
        string? documentNumber,
        bool isConfirmedMatch,
        string? notes,
        Action<StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default)
    {
        var preCheck = await _context.reservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

        if (preCheck == null)
        {
            throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
        }

        var resLockKey = $"SS_Reservation_{reservationId}";
        var custLockKey = $"SS_Customer_{preCheck.customer_id}_Identity";

        var isRelational = _context.Database.IsRelational();
        SemaphoreSlim? semRes = null;
        SemaphoreSlim? semCust = null;

        if (!isRelational)
        {
            semRes = InMemorySemaphores.GetOrAdd(resLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredRes = await semRes.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredRes) throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");

            semCust = InMemorySemaphores.GetOrAdd(custLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredCust = await semCust.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredCust)
            {
                semRes.Release();
                throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await AcquireAppLockAsync(resLockKey, cancellationToken);
                await AcquireAppLockAsync(custLockKey, cancellationToken);
            }

            var res = await _context.reservations
                .Include(r => r.customer)
                .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

            if (res == null)
            {
                throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
            }

            if (validatePermission != null)
            {
                var permSnapshot = await GetFreshStaffCounterPermissionSnapshotAsync(staffUserId, cancellationToken);
                validatePermission(permSnapshot, res.facility_id);
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var cleanDoc = documentNumber?.Trim();
            string? fingerprint = null;
            if (!string.IsNullOrWhiteSpace(cleanDoc))
            {
                var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(cleanDoc));
                fingerprint = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            // Ensure notes never leak raw document number
            var cleanNotes = notes?.Trim();
            if (!string.IsNullOrWhiteSpace(cleanDoc) && !string.IsNullOrWhiteSpace(cleanNotes) && cleanNotes.Contains(cleanDoc))
            {
                cleanNotes = cleanNotes.Replace(cleanDoc, "[REDACTED]");
            }

            var existingDoc = res.customer.identity_number?.Trim();
            var isMismatch = false;

            if (!string.IsNullOrWhiteSpace(existingDoc))
            {
                if (!string.Equals(existingDoc, cleanDoc, StringComparison.OrdinalIgnoreCase))
                {
                    isMismatch = true;
                }
            }
            else
            {
                // Profile has no identity_number yet
                if (isConfirmedMatch && !string.IsNullOrWhiteSpace(cleanDoc))
                {
                    res.customer.identity_number = cleanDoc;
                }
            }

            if (isMismatch)
            {
                var rejectedVerification = new identity_verification
                {
                    reservation_id = reservationId,
                    verified_by = staffUserId,
                    verification_method = method,
                    document_fingerprint = fingerprint,
                    result = "rejected",
                    notes = string.IsNullOrWhiteSpace(cleanNotes) ? "Mismatched document number" : cleanNotes,
                    verified_at = nowUtc
                };

                _context.identity_verifications.Add(rejectedVerification);
                await _context.SaveChangesAsync(cancellationToken);

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return new VerifyIdentityOutcome
                {
                    Verification = rejectedVerification,
                    IsMismatch = true
                };
            }

            var verification = new identity_verification
            {
                reservation_id = reservationId,
                verified_by = staffUserId,
                verification_method = method,
                document_fingerprint = fingerprint,
                result = isConfirmedMatch ? "verified" : "rejected",
                notes = cleanNotes,
                verified_at = nowUtc
            };

            _context.identity_verifications.Add(verification);
            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new VerifyIdentityOutcome
            {
                Verification = verification,
                IsMismatch = false
            };
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw MapSqlOrDbUpdateException(ex);
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
            semCust?.Release();
            semRes?.Release();
        }
    }

    public async Task<unit_allocation> AssignUnitWithLockAsync(
        long reservationId,
        long storageUnitId,
        long staffUserId,
        Action<StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default)
    {
        var preCheck = await _context.reservations
            .AsNoTracking()
            .Include(r => r.rental_agreement)
            .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

        if (preCheck == null)
        {
            throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
        }

        var resLockKey = $"SS_Reservation_{reservationId}";
        var capacityLockKey = $"SS_Facility_{preCheck.facility_id}_UnitType_{preCheck.unit_type_id}_Capacity";

        var isRelational = _context.Database.IsRelational();
        SemaphoreSlim? semRes = null;
        SemaphoreSlim? semCap = null;

        if (!isRelational)
        {
            semRes = InMemorySemaphores.GetOrAdd(resLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredRes = await semRes.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredRes) throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");

            semCap = InMemorySemaphores.GetOrAdd(capacityLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredCap = await semCap.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredCap)
            {
                semRes.Release();
                throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await AcquireAppLockAsync(resLockKey, cancellationToken);
                await AcquireAppLockAsync(capacityLockKey, cancellationToken);
            }

            // Re-read under locks
            var res = await _context.reservations
                .Include(r => r.facility)
                .Include(r => r.rental_agreement)
                    .ThenInclude(a => a!.unit_allocations)
                        .ThenInclude(ua => ua.storage_unit)
                .Include(r => r.rental_agreement)
                    .ThenInclude(a => a!.invoices)
                .Include(r => r.identity_verifications)
                .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

            if (res == null)
            {
                throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
            }

            if (validatePermission != null)
            {
                var permSnapshot = await GetFreshStaffCounterPermissionSnapshotAsync(staffUserId, cancellationToken);
                validatePermission(permSnapshot, res.facility_id);
            }

            if (res.status != "confirmed")
            {
                throw new InvalidOperationException("RESERVATION_NOT_CONFIRMED");
            }

            if (res.rental_agreement == null || res.rental_agreement.status != "scheduled")
            {
                throw new InvalidOperationException("AGREEMENT_NOT_SCHEDULED");
            }

            // Rental date check at facility timezone
            var nowUtc = DateTimeOffset.UtcNow;
            var facilityToday = FacilityTimeHelper.GetFacilityToday(res.facility.timezone, nowUtc);
            if (facilityToday < res.start_date)
            {
                throw new InvalidOperationException("RENTAL_PERIOD_NOT_STARTED");
            }
            if (facilityToday >= res.end_date)
            {
                throw new InvalidOperationException("RENTAL_PERIOD_EXPIRED");
            }

            // Check first period invoice paid
            var invoice = res.rental_agreement.invoices
                .FirstOrDefault(inv => inv.status == "paid" && inv.paid_amount == inv.total_amount && inv.total_amount > 0);
            if (invoice == null)
            {
                throw new InvalidOperationException("INVOICE_NOT_PAID");
            }

            // Check latest verification
            var latestVerification = res.identity_verifications
                .OrderByDescending(iv => iv.verified_at)
                .ThenByDescending(iv => iv.id)
                .FirstOrDefault();

            if (latestVerification == null || latestVerification.result != "verified")
            {
                throw new InvalidOperationException("IDENTITY_NOT_VERIFIED");
            }

            // Check existing active allocation for this agreement
            var activeAllocation = res.rental_agreement.unit_allocations
                .FirstOrDefault(ua => ua.status == "active");

            if (activeAllocation != null)
            {
                if (activeAllocation.storage_unit_id == storageUnitId)
                {
                    // Idempotent assign replay
                    if (transaction != null) await transaction.CommitAsync(cancellationToken);
                    return activeAllocation;
                }
                else
                {
                    throw new InvalidOperationException("AGREEMENT_ALREADY_HAS_DIFFERENT_ALLOCATION");
                }
            }

            // Verify candidate unit
            var unit = await _context.storage_units
                .FirstOrDefaultAsync(u => u.id == storageUnitId, cancellationToken);

            if (unit == null)
            {
                throw new KeyNotFoundException("STORAGE_UNIT_NOT_FOUND");
            }

            if (unit.facility_id != res.facility_id || unit.unit_type_id != res.unit_type_id)
            {
                throw new InvalidOperationException("STORAGE_UNIT_MISMATCH");
            }

            if (!unit.is_listed)
            {
                throw new InvalidOperationException("STORAGE_UNIT_NOT_LISTED");
            }

            if (unit.physical_status != "available")
            {
                throw new InvalidOperationException("STORAGE_UNIT_NOT_AVAILABLE");
            }

            // Check maintenance work orders
            var hasBlockingMaintenance = await _context.maintenance_work_orders
                .AnyAsync(m => m.storage_unit_id == storageUnitId &&
                               m.blocks_booking &&
                               m.status != "completed" &&
                               m.status != "cancelled", cancellationToken);
            if (hasBlockingMaintenance)
            {
                throw new InvalidOperationException("STORAGE_UNIT_MAINTENANCE_BLOCKED");
            }

            // Check overlapping active allocation
            var hasOverlap = await _context.unit_allocations
                .AnyAsync(ua => ua.storage_unit_id == storageUnitId &&
                                ua.status == "active" &&
                                ua.allocation_start_date < res.end_date &&
                                ua.allocation_end_date > res.start_date, cancellationToken);
            if (hasOverlap)
            {
                throw new InvalidOperationException("STORAGE_UNIT_OVERLAPPING_ALLOCATION");
            }

            // Create allocation
            var newAllocation = new unit_allocation
            {
                storage_unit_id = storageUnitId,
                agreement_id = res.rental_agreement.id,
                reservation_id = null,
                allocation_kind = "rental",
                allocation_start_date = res.rental_agreement.start_date,
                allocation_end_date = res.rental_agreement.end_date,
                status = "active",
                assigned_by = staffUserId,
                created_at = nowUtc,
                ended_at = null
            };

            _context.unit_allocations.Add(newAllocation);

            // Update unit physical_status: available -> reserved
            unit.physical_status = "reserved";
            unit.updated_at = nowUtc;

            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            newAllocation.storage_unit = unit;
            return newAllocation;
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw MapSqlOrDbUpdateException(ex);
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
            semCap?.Release();
            semRes?.Release();
        }
    }

    public async Task<unit_allocation> ReleaseUnitWithLockAsync(
        long reservationId,
        long currentAllocationId,
        string reason,
        long staffUserId,
        Action<StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default)
    {
        var preCheck = await _context.reservations
            .AsNoTracking()
            .Include(r => r.rental_agreement)
            .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

        if (preCheck == null)
        {
            throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
        }

        var resLockKey = $"SS_Reservation_{reservationId}";
        var capacityLockKey = $"SS_Facility_{preCheck.facility_id}_UnitType_{preCheck.unit_type_id}_Capacity";

        var isRelational = _context.Database.IsRelational();
        SemaphoreSlim? semRes = null;
        SemaphoreSlim? semCap = null;

        if (!isRelational)
        {
            semRes = InMemorySemaphores.GetOrAdd(resLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredRes = await semRes.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredRes) throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");

            semCap = InMemorySemaphores.GetOrAdd(capacityLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredCap = await semCap.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredCap)
            {
                semRes.Release();
                throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await AcquireAppLockAsync(resLockKey, cancellationToken);
                await AcquireAppLockAsync(capacityLockKey, cancellationToken);
            }

            var res = await _context.reservations
                .Include(r => r.rental_agreement)
                    .ThenInclude(a => a!.unit_allocations)
                        .ThenInclude(ua => ua.storage_unit)
                .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

            if (res == null)
            {
                throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
            }

            if (validatePermission != null)
            {
                var permSnapshot = await GetFreshStaffCounterPermissionSnapshotAsync(staffUserId, cancellationToken);
                validatePermission(permSnapshot, res.facility_id);
            }

            if (res.rental_agreement == null || res.rental_agreement.status != "scheduled")
            {
                throw new InvalidOperationException("AGREEMENT_NOT_SCHEDULED");
            }

            var allocation = res.rental_agreement.unit_allocations
                .FirstOrDefault(ua => ua.status == "active");

            if (allocation == null || allocation.id != currentAllocationId)
            {
                throw new InvalidOperationException("ALLOCATION_NOT_FOUND_OR_STALE");
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var cleanReason = string.IsNullOrWhiteSpace(reason) ? "Staff released unit before handover" : reason.Trim();

            allocation.status = "released";
            allocation.ended_at = nowUtc;
            allocation.reason = cleanReason;

            var unit = await _context.storage_units
                .FirstOrDefaultAsync(u => u.id == allocation.storage_unit_id, cancellationToken);

            if (unit != null)
            {
                var otherActive = await _context.unit_allocations
                    .AnyAsync(ua => ua.id != allocation.id &&
                                    ua.storage_unit_id == unit.id &&
                                    ua.status == "active", cancellationToken);

                if (!otherActive && unit.physical_status == "reserved")
                {
                    unit.physical_status = "available";
                    unit.updated_at = nowUtc;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return allocation;
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw MapSqlOrDbUpdateException(ex);
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
            semCap?.Release();
            semRes?.Release();
        }
    }

    public async Task<inspection> PerformInspectionWithLockAsync(
        long reservationId,
        long currentAllocationId,
        long staffUserId,
        IReadOnlyList<(string ItemName, string Condition, string? Notes)> items,
        string? overallSummary,
        bool isUnsafe = false,
        Action<StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default)
    {
        var preCheck = await _context.reservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

        if (preCheck == null)
        {
            throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
        }

        var resLockKey = $"SS_Reservation_{reservationId}";
        var capacityLockKey = $"SS_Facility_{preCheck.facility_id}_UnitType_{preCheck.unit_type_id}_Capacity";

        var isRelational = _context.Database.IsRelational();
        SemaphoreSlim? semRes = null;
        SemaphoreSlim? semCap = null;

        if (!isRelational)
        {
            semRes = InMemorySemaphores.GetOrAdd(resLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredRes = await semRes.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredRes) throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");

            semCap = InMemorySemaphores.GetOrAdd(capacityLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredCap = await semCap.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredCap)
            {
                semRes.Release();
                throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await AcquireAppLockAsync(resLockKey, cancellationToken);
                await AcquireAppLockAsync(capacityLockKey, cancellationToken);
            }

            var res = await _context.reservations
                .Include(r => r.rental_agreement)
                    .ThenInclude(a => a!.unit_allocations)
                        .ThenInclude(ua => ua.storage_unit)
                .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

            if (res == null)
            {
                throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
            }

            if (validatePermission != null)
            {
                var permSnapshot = await GetFreshStaffCounterPermissionSnapshotAsync(staffUserId, cancellationToken);
                validatePermission(permSnapshot, res.facility_id);
            }

            if (res.rental_agreement == null || res.rental_agreement.status != "scheduled")
            {
                throw new InvalidOperationException("AGREEMENT_NOT_SCHEDULED");
            }

            var activeAllocation = res.rental_agreement.unit_allocations
                .FirstOrDefault(ua => ua.status == "active");

            if (activeAllocation == null || activeAllocation.id != currentAllocationId)
            {
                throw new InvalidOperationException("ALLOCATION_NOT_FOUND_OR_STALE");
            }

            // Checklist verification & normalization
            var normalizedItems = ValidateAndNormalizeInspectionItems(items);

            var nowUtc = DateTimeOffset.UtcNow;
            var hasFailure = isUnsafe || normalizedItems.Any(it => it.Condition is "damaged" or "missing");
            var anyAcceptable = !hasFailure && normalizedItems.Any(it => it.Condition == "acceptable");

            var status = hasFailure ? "failed" : "completed";
            var overallCondition = isUnsafe
                ? "unsafe"
                : (hasFailure
                    ? "damaged"
                    : (anyAcceptable ? "acceptable" : "good"));

            var inspection = new inspection
            {
                facility_id = res.facility_id,
                storage_unit_id = activeAllocation.storage_unit_id,
                reservation_id = res.id,
                agreement_id = res.rental_agreement.id,
                inspected_by = staffUserId,
                inspection_type = "check_in",
                status = status,
                overall_condition = overallCondition,
                summary = overallSummary?.Trim(),
                inspected_at = nowUtc,
                created_at = nowUtc
            };

            foreach (var item in normalizedItems)
            {
                inspection.inspection_items.Add(new inspection_item
                {
                    item_name = item.ItemName,
                    condition = item.Condition,
                    notes = item.Notes,
                    charge_amount = 0
                });
            }

            _context.inspections.Add(inspection);

            if (hasFailure)
            {
                activeAllocation.status = "released";
                activeAllocation.ended_at = nowUtc;
                activeAllocation.reason = $"Inspection failed: {(isUnsafe ? "unsafe unit" : overallSummary ?? "damaged/missing items")}";

                var unit = await _context.storage_units
                    .FirstOrDefaultAsync(u => u.id == activeAllocation.storage_unit_id, cancellationToken);

                if (unit != null)
                {
                    unit.physical_status = "maintenance";
                    unit.updated_at = nowUtc;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            inspection.storage_unit = activeAllocation.storage_unit;
            return inspection;
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw MapSqlOrDbUpdateException(ex);
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
            semCap?.Release();
            semRes?.Release();
        }
    }

    public async Task<(handover_record Handover, string? Pin, long? CredentialId, DateTimeOffset? CredentialExpiresAt, bool IsAlreadyCompleted)> CompleteCheckInWithLockAsync(
        long reservationId,
        long currentAllocationId,
        long currentInspectionId,
        bool customerAcknowledged,
        string? notes,
        long staffUserId,
        string? pinHmacSecret,
        Action<StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default)
    {
        if (!customerAcknowledged)
        {
            throw new InvalidOperationException("CUSTOMER_NOT_ACKNOWLEDGED");
        }

        var preCheck = await _context.reservations
            .AsNoTracking()
            .Include(r => r.rental_agreement)
            .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

        if (preCheck == null)
        {
            throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
        }

        var resLockKey = $"SS_Reservation_{reservationId}";
        var capacityLockKey = $"SS_Facility_{preCheck.facility_id}_UnitType_{preCheck.unit_type_id}_Capacity";
        var pinLockKey = $"SS_Facility_{preCheck.facility_id}_PinIssuance";

        var isRelational = _context.Database.IsRelational();
        SemaphoreSlim? semRes = null;
        SemaphoreSlim? semCap = null;
        SemaphoreSlim? semPin = null;

        if (!isRelational)
        {
            semRes = InMemorySemaphores.GetOrAdd(resLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredRes = await semRes.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredRes) throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");

            semCap = InMemorySemaphores.GetOrAdd(capacityLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredCap = await semCap.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredCap)
            {
                semRes.Release();
                throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
            }

            semPin = InMemorySemaphores.GetOrAdd(pinLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredPin = await semPin.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredPin)
            {
                semCap.Release();
                semRes.Release();
                throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await AcquireAppLockAsync(resLockKey, cancellationToken);
                await AcquireAppLockAsync(capacityLockKey, cancellationToken);
                await AcquireAppLockAsync(pinLockKey, cancellationToken);
            }

            var res = await _context.reservations
                .Include(r => r.facility)
                .Include(r => r.rental_agreement)
                    .ThenInclude(a => a!.unit_allocations)
                        .ThenInclude(ua => ua.storage_unit)
                .Include(r => r.rental_agreement)
                    .ThenInclude(a => a!.invoices)
                .Include(r => r.rental_agreement)
                    .ThenInclude(a => a!.handover_records)
                .Include(r => r.rental_agreement)
                    .ThenInclude(a => a!.access_credentials)
                .Include(r => r.identity_verifications)
                .Include(r => r.inspections)
                .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

            if (res == null)
            {
                throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
            }

            var agreement = res.rental_agreement;
            if (agreement == null)
            {
                throw new KeyNotFoundException("AGREEMENT_NOT_FOUND");
            }

            if (validatePermission != null)
            {
                var permSnapshot = await GetFreshStaffCounterPermissionSnapshotAsync(staffUserId, cancellationToken);
                validatePermission(permSnapshot, res.facility_id);
            }

            // Check idempotent complete replay
            var existingHandover = agreement.handover_records
                .OrderByDescending(h => h.created_at)
                .FirstOrDefault();

            if (existingHandover != null || agreement.status == "active")
            {
                if (existingHandover != null &&
                    existingHandover.unit_allocation_id == currentAllocationId &&
                    existingHandover.inspection_id == currentInspectionId)
                {
                    var activeCred = agreement.access_credentials.FirstOrDefault(c => c.status == "active");
                    if (transaction != null) await transaction.CommitAsync(cancellationToken);
                    return (existingHandover, null, activeCred?.id, activeCred?.expires_at, true);
                }

                throw new InvalidOperationException("HANDOVER_REPLAY_CONFLICT");
            }

            if (res.status != "confirmed" || agreement.status != "scheduled")
            {
                throw new InvalidOperationException("INVALID_STATUS_FOR_COMPLETE");
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var facilityToday = FacilityTimeHelper.GetFacilityToday(res.facility.timezone, nowUtc);
            if (facilityToday < agreement.start_date || facilityToday >= agreement.end_date)
            {
                throw new InvalidOperationException("RENTAL_PERIOD_INVALID");
            }

            // Invoice check
            var invoice = agreement.invoices
                .FirstOrDefault(inv => inv.status == "paid" && inv.paid_amount == inv.total_amount && inv.total_amount > 0);
            if (invoice == null)
            {
                throw new InvalidOperationException("INVOICE_NOT_PAID");
            }

            // Latest verification
            var latestVerification = res.identity_verifications
                .OrderByDescending(iv => iv.verified_at)
                .ThenByDescending(iv => iv.id)
                .FirstOrDefault();
            if (latestVerification == null || latestVerification.result != "verified")
            {
                throw new InvalidOperationException("IDENTITY_NOT_VERIFIED");
            }

            // Active allocation
            var allocation = agreement.unit_allocations
                .FirstOrDefault(ua => ua.status == "active");
            if (allocation == null || allocation.id != currentAllocationId)
            {
                throw new InvalidOperationException("ALLOCATION_NOT_FOUND_OR_STALE");
            }

            // Unit status
            var unit = await _context.storage_units
                .FirstOrDefaultAsync(u => u.id == allocation.storage_unit_id, cancellationToken);
            if (unit == null || unit.physical_status != "reserved")
            {
                throw new InvalidOperationException("STORAGE_UNIT_NOT_RESERVED");
            }

            // Inspection check
            var inspection = res.inspections
                .FirstOrDefault(i => i.id == currentInspectionId);
            if (inspection == null ||
                inspection.agreement_id != agreement.id ||
                inspection.storage_unit_id != allocation.storage_unit_id ||
                inspection.status != "completed" ||
                inspection.overall_condition is not ("good" or "acceptable") ||
                (inspection.inspected_at.HasValue && inspection.inspected_at.Value < allocation.created_at))
            {
                throw new InvalidOperationException("INSPECTION_INVALID_OR_STALE");
            }

            // Generate unique PIN for facility
            string pin = string.Empty;
            string digest = string.Empty;
            var isPinUnique = false;

            for (var attempt = 0; attempt < 20; attempt++)
            {
                var candidatePin = PinCryptoHelper.GeneratePin();
                var candidateDigest = PinCryptoHelper.ComputeDigest(candidatePin, pinHmacSecret);

                var collides = await _context.access_credentials
                    .AnyAsync(ac => ac.agreement.facility_id == res.facility_id &&
                                    ac.credential_type == "pin" &&
                                    ac.status == "active" &&
                                    ac.secret_digest == candidateDigest &&
                                    (ac.expires_at == null || ac.expires_at > nowUtc),
                              cancellationToken);

                if (!collides)
                {
                    pin = candidatePin;
                    digest = candidateDigest;
                    isPinUnique = true;
                    break;
                }
            }

            if (!isPinUnique)
            {
                throw new InvalidOperationException("PIN_COLLISION_EXHAUSTED");
            }

            var facilityExpiresAt = FacilityTimeHelper.GetFacilityStartOfDay(agreement.end_date, res.facility.timezone);

            // 1. Handover record
            var handover = new handover_record
            {
                agreement_id = agreement.id,
                unit_allocation_id = allocation.id,
                inspection_id = inspection.id,
                handled_by = staffUserId,
                handover_type = "check_in",
                customer_signed_at = nowUtc,
                staff_signed_at = nowUtc,
                notes = notes?.Trim(),
                created_at = nowUtc
            };
            _context.handover_records.Add(handover);

            // 2. Agreement active
            agreement.status = "active";
            agreement.checked_in_at = nowUtc;
            agreement.signed_at ??= nowUtc;
            agreement.updated_at = nowUtc;

            // 3. Reservation checked_in
            res.status = "checked_in";
            res.updated_at = nowUtc;

            // 4. Unit occupied
            unit.physical_status = "occupied";
            unit.updated_at = nowUtc;

            // 5. Access credential
            var credential = new access_credential
            {
                agreement_id = agreement.id,
                credential_type = "pin",
                secret_digest = digest,
                display_hint = "PIN (6 digits)",
                issued_at = nowUtc,
                expires_at = facilityExpiresAt,
                status = "active",
                issued_by = staffUserId,
                created_at = nowUtc
            };
            _context.access_credentials.Add(credential);

            // 6. In-app notification
            var notificationPayload = JsonSerializer.Serialize(new
            {
                agreementId = agreement.id,
                agreementNo = agreement.agreement_no,
                storageUnitId = unit.id,
                unitCode = unit.unit_code,
                handoverType = "check_in",
                checkedInAt = nowUtc
            });

            var notification = new notification
            {
                user_id = agreement.customer_id,
                channel = "in_app",
                template_code = "HANDOVER_CHECK_IN_COMPLETED",
                subject = "Bàn giao ô kho thành công",
                payload = notificationPayload,
                status = "sent",
                scheduled_at = nowUtc,
                sent_at = nowUtc,
                deduplication_key = $"handover_checkin_agreement_{agreement.id}",
                created_at = nowUtc
            };
            _context.notifications.Add(notification);

            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            handover.agreement = agreement;
            handover.unit_allocation = allocation;
            return (handover, pin, credential.id, credential.expires_at, false);
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw MapSqlOrDbUpdateException(ex);
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
            semPin?.Release();
            semCap?.Release();
            semRes?.Release();
        }
    }

    public async Task<(long AgreementId, long OldCredentialId, long NewCredentialId, string Pin, DateTimeOffset IssuedAt, DateTimeOffset? ExpiresAt)> ReissuePinWithLockAsync(
        long reservationId,
        long currentCredentialId,
        string documentNumber,
        bool isConfirmedMatch,
        string? reason,
        long staffUserId,
        string? pinHmacSecret,
        Action<StaffCounterPermissionSnapshot, long>? validatePermission = null,
        CancellationToken cancellationToken = default)
    {
        if (!isConfirmedMatch)
        {
            throw new InvalidOperationException("STAFF_CONFIRMATION_REQUIRED");
        }

        var preCheck = await _context.reservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

        if (preCheck == null)
        {
            throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
        }

        var resLockKey = $"SS_Reservation_{reservationId}";
        var pinLockKey = $"SS_Facility_{preCheck.facility_id}_PinIssuance";

        var isRelational = _context.Database.IsRelational();
        SemaphoreSlim? semRes = null;
        SemaphoreSlim? semPin = null;

        if (!isRelational)
        {
            semRes = InMemorySemaphores.GetOrAdd(resLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredRes = await semRes.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredRes) throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");

            semPin = InMemorySemaphores.GetOrAdd(pinLockKey, _ => new SemaphoreSlim(1, 1));
            var enteredPin = await semPin.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (!enteredPin)
            {
                semRes.Release();
                throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await AcquireAppLockAsync(resLockKey, cancellationToken);
                await AcquireAppLockAsync(pinLockKey, cancellationToken);
            }

            var res = await _context.reservations
                .Include(r => r.customer)
                .Include(r => r.facility)
                .Include(r => r.rental_agreement)
                    .ThenInclude(a => a!.access_credentials)
                .FirstOrDefaultAsync(r => r.id == reservationId, cancellationToken);

            if (res == null)
            {
                throw new KeyNotFoundException("RESERVATION_NOT_FOUND");
            }

            StaffCounterPermissionSnapshot? permSnapshot = null;
            if (validatePermission != null)
            {
                permSnapshot = await GetFreshStaffCounterPermissionSnapshotAsync(staffUserId, cancellationToken);
                validatePermission(permSnapshot, res.facility_id);
            }

            var agreement = res.rental_agreement;
            if (agreement == null || agreement.status != "active")
            {
                throw new InvalidOperationException("AGREEMENT_NOT_ACTIVE");
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var facilityToday = FacilityTimeHelper.GetFacilityToday(res.facility.timezone, nowUtc);
            if (facilityToday < agreement.start_date || facilityToday >= agreement.end_date)
            {
                throw new InvalidOperationException("RENTAL_PERIOD_EXPIRED");
            }

            var cleanDoc = documentNumber?.Trim();
            if (string.IsNullOrWhiteSpace(cleanDoc) ||
                !string.Equals(res.customer.identity_number?.Trim(), cleanDoc, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("DOCUMENT_NUMBER_MISMATCH");
            }

            var oldCred = agreement.access_credentials
                .FirstOrDefault(c => c.id == currentCredentialId);

            if (oldCred == null || oldCred.status != "active")
            {
                throw new InvalidOperationException("CREDENTIAL_NOT_ACTIVE");
            }

            // Generate new PIN
            string pin = string.Empty;
            string digest = string.Empty;
            var isPinUnique = false;

            for (var attempt = 0; attempt < 20; attempt++)
            {
                var candidatePin = PinCryptoHelper.GeneratePin();
                var candidateDigest = PinCryptoHelper.ComputeDigest(candidatePin, pinHmacSecret);

                var collides = await _context.access_credentials
                    .AnyAsync(ac => ac.agreement.facility_id == res.facility_id &&
                                    ac.credential_type == "pin" &&
                                    ac.status == "active" &&
                                    ac.secret_digest == candidateDigest &&
                                    (ac.expires_at == null || ac.expires_at > nowUtc),
                              cancellationToken);

                if (!collides)
                {
                    pin = candidatePin;
                    digest = candidateDigest;
                    isPinUnique = true;
                    break;
                }
            }

            if (!isPinUnique)
            {
                throw new InvalidOperationException("PIN_COLLISION_EXHAUSTED");
            }

            var facilityExpiresAt = FacilityTimeHelper.GetFacilityStartOfDay(agreement.end_date, res.facility.timezone);

            oldCred.status = "revoked";
            oldCred.revoked_at = nowUtc;

            var newCred = new access_credential
            {
                agreement_id = agreement.id,
                credential_type = "pin",
                secret_digest = digest,
                display_hint = "PIN (6 digits)",
                issued_at = nowUtc,
                expires_at = facilityExpiresAt,
                status = "active",
                issued_by = staffUserId,
                created_at = nowUtc
            };
            _context.access_credentials.Add(newCred);

            // Save new credential first so EF Core populates newCred.id from database IDENTITY
            await _context.SaveChangesAsync(cancellationToken);

            var staffRole = (permSnapshot != null && permSnapshot.Roles.Contains("facility_manager"))
                ? "facility_manager"
                : "facility_staff";

            var auditLog = new audit_log
            {
                actor_user_id = staffUserId,
                actor_role = staffRole,
                entity_schema = "core",
                entity_type = "access_credential",
                entity_id = newCred.id.ToString(),
                action = "REISSUE_PIN",
                old_values = JsonSerializer.Serialize(new { credential_id = oldCred.id, status = "revoked" }),
                new_values = JsonSerializer.Serialize(new { credential_id = newCred.id, status = "active", reason = reason?.Trim() }),
                occurred_at = nowUtc
            };
            _context.audit_logs.Add(auditLog);

            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return (agreement.id, oldCred.id, newCred.id, pin, newCred.issued_at, newCred.expires_at);
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw MapSqlOrDbUpdateException(ex);
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
            semPin?.Release();
            semRes?.Release();
        }
    }

    public async Task<(IReadOnlyList<rental_agreement> Items, int TotalCount)> GetCustomerAgreementsAsync(
        long customerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.rental_agreements
            .AsNoTracking()
            .Include(a => a.facility)
            .Include(a => a.reservation)
                .ThenInclude(r => r.unit_type)
            .Include(a => a.unit_allocations)
                .ThenInclude(ua => ua.storage_unit)
            .Where(a => a.customer_id == customerId)
            .OrderByDescending(a => a.created_at)
            .ThenByDescending(a => a.id);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<rental_agreement?> GetCustomerHandoverAgreementAsync(
        long customerId,
        long agreementId,
        CancellationToken cancellationToken = default)
    {
        return await _context.rental_agreements
            .AsNoTracking()
            .Include(a => a.facility)
            .Include(a => a.unit_allocations)
                .ThenInclude(ua => ua.storage_unit)
            .Include(a => a.handover_records)
            .Include(a => a.access_credentials)
            .Include(a => a.inspections)
            .FirstOrDefaultAsync(a => a.id == agreementId && a.customer_id == customerId, cancellationToken);
    }

    public async Task<(IReadOnlyList<notification> Items, int TotalCount)> GetCustomerNotificationsAsync(
        long customerId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.notifications
            .AsNoTracking()
            .Where(n => n.user_id == customerId && n.channel == "in_app")
            .OrderByDescending(n => n.created_at)
            .ThenByDescending(n => n.id);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    private async Task AcquireAppLockAsync(string lockKey, CancellationToken cancellationToken)
    {
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
            throw new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT");
        }
    }

    private async Task<StaffCounterPermissionSnapshot> GetFreshStaffCounterPermissionSnapshotAsync(
        long staffUserId,
        CancellationToken cancellationToken)
    {
        var user = await _context.users
            .AsNoTracking()
            .Include(u => u.user_roleusers)
                .ThenInclude(ur => ur.role)
            .Include(u => u.employee_profile)
            .FirstOrDefaultAsync(u => u.id == staffUserId, cancellationToken);

        if (user == null)
        {
            return new StaffCounterPermissionSnapshot
            {
                UserId = staffUserId,
                IsUserActive = false,
                IsEmployeeActive = false,
                Roles = new List<string>(),
                Assignments = new List<(long FacilityId, string AssignmentRole, DateTimeOffset StartsAt, DateTimeOffset? EndsAt)>()
            };
        }

        var assignments = await _context.staff_facility_assignments
            .AsNoTracking()
            .Where(a => a.employee_id == staffUserId)
            .Select(a => new { a.facility_id, a.assignment_role, a.starts_at, a.ends_at })
            .ToListAsync(cancellationToken);

        return new StaffCounterPermissionSnapshot
        {
            UserId = staffUserId,
            IsUserActive = string.Equals(user.status, "active", StringComparison.OrdinalIgnoreCase),
            Roles = user.user_roleusers.Select(ur => ur.role.code).ToList(),
            IsEmployeeActive = user.employee_profile != null && string.Equals(user.employee_profile.employment_status, "active", StringComparison.OrdinalIgnoreCase),
            Assignments = assignments.Select(a => (a.facility_id, a.assignment_role, a.starts_at, a.ends_at)).ToList()
        };
    }

    public record NormalizedInspectionItem(string ItemName, string Category, string Condition, string? Notes);

    private static List<NormalizedInspectionItem> ValidateAndNormalizeInspectionItems(
        IReadOnlyList<(string ItemName, string Condition, string? Notes)> items)
    {
        if (items == null || items.Count < 4)
        {
            throw new InvalidOperationException("CHECKLIST_INCOMPLETE");
        }

        var normalizedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<NormalizedInspectionItem>(items.Count);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ItemName))
            {
                throw new InvalidOperationException("CHECKLIST_ITEM_REQUIRED");
            }

            var rawName = item.ItemName.Trim();
            var category = NormalizeCategory(rawName.ToLowerInvariant());

            if (category == null)
            {
                throw new InvalidOperationException("CHECKLIST_INCOMPLETE");
            }

            if (!normalizedCategories.Add(category))
            {
                throw new InvalidOperationException("CHECKLIST_DUPLICATE_ITEM");
            }

            if (string.IsNullOrWhiteSpace(item.Condition))
            {
                throw new InvalidOperationException("CHECKLIST_INVALID_CONDITION");
            }

            var cond = item.Condition.Trim().ToLowerInvariant();
            if (cond == "unsafe")
            {
                throw new InvalidOperationException("CHECKLIST_UNSAFE_CONDITION_FORBIDDEN");
            }

            if (cond == "not_applicable")
            {
                throw new InvalidOperationException("CHECKLIST_NOT_APPLICABLE_FORBIDDEN");
            }

            if (cond is not ("good" or "acceptable" or "damaged" or "missing"))
            {
                throw new InvalidOperationException("CHECKLIST_INVALID_CONDITION");
            }

            result.Add(new NormalizedInspectionItem(
                rawName,
                category,
                cond,
                item.Notes?.Trim()
            ));
        }

        var required = new[] { "door", "lock", "lighting", "cleanliness" };
        foreach (var req in required)
        {
            if (!normalizedCategories.Contains(req))
            {
                throw new InvalidOperationException("CHECKLIST_INCOMPLETE");
            }
        }

        return result;
    }

    private static string? NormalizeCategory(string name)
    {
        if (name.Contains("door") || name.Contains("cửa") || name.Contains("cua")) return "door";
        if (name.Contains("lock") || name.Contains("khóa") || name.Contains("khoa")) return "lock";
        if (name.Contains("light") || name.Contains("đèn") || name.Contains("den")) return "lighting";
        if (name.Contains("clean") || name.Contains("vệ sinh") || name.Contains("ve sinh") || name.Contains("hygiene")) return "cleanliness";
        return null;
    }

    private static Exception MapSqlOrDbUpdateException(Exception ex)
    {
        if (ex is SqlException sqlEx)
        {
            return MapSqlException(sqlEx);
        }
        if (ex is DbUpdateException dbUpdateEx && dbUpdateEx.InnerException is SqlException innerSqlEx)
        {
            return MapSqlException(innerSqlEx);
        }
        return ex;
    }

    private static Exception MapSqlException(SqlException sqlEx)
    {
        return sqlEx.Number switch
        {
            51006 => new InvalidOperationException("STORAGE_UNIT_OVERLAPPING_ALLOCATION"),
            51007 => new InvalidOperationException("AGREEMENT_OVERLAPPING_ALLOCATION"),
            51025 => new InvalidOperationException("STORAGE_UNIT_NOT_BOOKABLE"),
            51026 => new InvalidOperationException("RESERVATION_ALLOCATION_MISMATCH"),
            51027 => new InvalidOperationException("AGREEMENT_ALLOCATION_MISMATCH"),
            51030 => new InvalidOperationException("INSPECTION_FACILITY_MISMATCH"),
            51031 => new InvalidOperationException("INSPECTOR_NOT_ASSIGNED"),
            51032 => new InvalidOperationException("HANDOVER_SCOPE_MISMATCH"),
            51050 => new InvalidOperationException("INVALID_UNIT_STATUS_TRANSITION"),
            51051 => new InvalidOperationException("INVALID_RESERVATION_STATUS_TRANSITION"),
            51052 => new InvalidOperationException("INVALID_AGREEMENT_STATUS_TRANSITION"),
            2601 or 2627 => new InvalidOperationException("UNIQUE_CONSTRAINT_VIOLATION"),
            1205 or 1222 => new InvalidOperationException("CONCURRENCY_LOCK_TIMEOUT"),
            _ => sqlEx
        };
    }
}
