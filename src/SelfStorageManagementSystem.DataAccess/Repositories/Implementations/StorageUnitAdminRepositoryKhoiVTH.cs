using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Exceptions;
using SelfStorageManagementSystem.DataAccess.Models;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Implementations;

/// <summary>
/// DataAccess implementation of storage unit administration repository.
/// Handles query composition, SQL pagination with deterministic tie-breakers, reference verification,
/// fresh optimistic concurrency checks, and atomic mutations under capacity locks
/// (SS_Facility_{facilityId}_UnitType_{unitTypeId}_Capacity) with strict deadlock-free lock ordering.
/// Does not contain HTTP authorization or presentation logic.
/// </summary>
public class StorageUnitAdminRepositoryKhoiVTH : GenericRepositoryKhoiVTH<storage_unit>, IStorageUnitAdminRepository
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemorySemaphores = new();

    public StorageUnitAdminRepositoryKhoiVTH(SelfStorageDbContext context) : base(context)
    {
    }

    /// <summary>
    /// Generates the shared capacity lock key used across Reservation, Check-In, and Storage Unit CRUD.
    /// </summary>
    public static string GetCapacityLockKey(long facilityId, long unitTypeId) =>
        $"SS_Facility_{facilityId}_UnitType_{unitTypeId}_Capacity";

    /// <summary>
    /// Computes the deterministic, ordered set of capacity lock keys covering source and target
    /// facilities and unit types. Eliminates deadlocks between opposing transfers.
    /// </summary>
    public static IReadOnlyList<string> DetermineCapacityLockKeys(
        long sourceFacilityId,
        long sourceUnitTypeId,
        long targetFacilityId,
        long targetUnitTypeId)
    {
        var sourceKey = GetCapacityLockKey(sourceFacilityId, sourceUnitTypeId);
        var targetKey = GetCapacityLockKey(targetFacilityId, targetUnitTypeId);

        return new[] { sourceKey, targetKey }
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Exposes in-memory semaphores for testing concurrency and lock lifecycle.
    /// </summary>
    public static SemaphoreSlim GetInMemorySemaphore(string lockKey) =>
        InMemorySemaphores.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

    private sealed record StorageUnitMutationPlan(
        long SourceFacilityId,
        long SourceUnitTypeId,
        long TargetFacilityId,
        long TargetUnitTypeId,
        long? TargetAreaId,
        string TargetUnitCode,
        string? TargetFloorLabel,
        string? TargetZoneLabel,
        string TargetPhysicalStatus,
        bool TargetIsListed,
        string? TargetNotes
    );

    // =========================================================================
    // Queries & SQL Pagination
    // =========================================================================

    public async Task<(IReadOnlyList<storage_unit> Items, int TotalCount)> GetPagedStorageUnitsAsync(
        StorageUnitAdminQueryParametersKhoiVTH queryParams,
        CancellationToken cancellationToken = default)
    {
        var query = _context.storage_units
            .AsNoTracking()
            .Include(u => u.facility)
            .Include(u => u.unit_type)
            .Include(u => u.area)
            .AsQueryable();

        // 1. Filtering
        if (queryParams.FacilityId.HasValue)
        {
            query = query.Where(u => u.facility_id == queryParams.FacilityId.Value);
        }
        else if (queryParams.FacilityIds != null && queryParams.FacilityIds.Count > 0)
        {
            query = query.Where(u => queryParams.FacilityIds.Contains(u.facility_id));
        }

        if (queryParams.UnitTypeId.HasValue)
        {
            query = query.Where(u => u.unit_type_id == queryParams.UnitTypeId.Value);
        }

        if (queryParams.AreaId.HasValue)
        {
            query = query.Where(u => u.area_id == queryParams.AreaId.Value);
        }

        if (!string.IsNullOrWhiteSpace(queryParams.PhysicalStatus))
        {
            var status = queryParams.PhysicalStatus.Trim();
            query = query.Where(u => u.physical_status == status);
        }

        if (queryParams.IsListed.HasValue)
        {
            query = query.Where(u => u.is_listed == queryParams.IsListed.Value);
        }

        if (!string.IsNullOrWhiteSpace(queryParams.SearchTerm))
        {
            var term = queryParams.SearchTerm.Trim().ToLower();
            query = query.Where(u =>
                u.unit_code.ToLower().Contains(term) ||
                (u.notes != null && u.notes.ToLower().Contains(term)) ||
                (u.floor_label != null && u.floor_label.ToLower().Contains(term)) ||
                (u.zone_label != null && u.zone_label.ToLower().Contains(term)));
        }

        // 2. Sorting with deterministic tie-breakers (Finding 4: guarantees stable pagination in SQL)
        var sortBy = queryParams.SortBy?.Trim().ToLowerInvariant() ?? "unit_code";
        query = (sortBy, queryParams.SortDescending) switch
        {
            ("created_at", false) => query.OrderBy(u => u.created_at).ThenBy(u => u.facility_id).ThenBy(u => u.id),
            ("created_at", true) => query.OrderByDescending(u => u.created_at).ThenBy(u => u.facility_id).ThenByDescending(u => u.id),
            ("updated_at", false) => query.OrderBy(u => u.updated_at).ThenBy(u => u.facility_id).ThenBy(u => u.id),
            ("updated_at", true) => query.OrderByDescending(u => u.updated_at).ThenBy(u => u.facility_id).ThenByDescending(u => u.id),
            ("physical_status", false) => query.OrderBy(u => u.physical_status).ThenBy(u => u.facility_id).ThenBy(u => u.id),
            ("physical_status", true) => query.OrderByDescending(u => u.physical_status).ThenBy(u => u.facility_id).ThenByDescending(u => u.id),
            ("unit_type", false) => query.OrderBy(u => u.unit_type.name).ThenBy(u => u.facility_id).ThenBy(u => u.id),
            ("unit_type", true) => query.OrderByDescending(u => u.unit_type.name).ThenBy(u => u.facility_id).ThenByDescending(u => u.id),
            ("facility", false) => query.OrderBy(u => u.facility.name).ThenBy(u => u.unit_code).ThenBy(u => u.id),
            ("facility", true) => query.OrderByDescending(u => u.facility.name).ThenByDescending(u => u.unit_code).ThenByDescending(u => u.id),
            ("id", false) => query.OrderBy(u => u.id),
            ("id", true) => query.OrderByDescending(u => u.id),
            (_, true) => query.OrderByDescending(u => u.facility_id).ThenByDescending(u => u.unit_code).ThenByDescending(u => u.id),
            _ => query.OrderBy(u => u.facility_id).ThenBy(u => u.unit_code).ThenBy(u => u.id)
        };

        // 3. SQL Count & Pagination (OFFSET / FETCH NEXT in SQL, no dataset buffering in memory)
        var totalCount = await query.CountAsync(cancellationToken);

        var pageNumber = Math.Max(1, queryParams.PageNumber);
        var pageSize = Math.Clamp(queryParams.PageSize, 1, 100);

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<storage_unit?> GetStorageUnitByIdAsync(
        long unitId,
        bool includeDetails = false,
        CancellationToken cancellationToken = default)
    {
        if (!includeDetails)
        {
            return await _context.storage_units
                .AsNoTracking()
                .Include(u => u.facility)
                .Include(u => u.unit_type)
                .Include(u => u.area)
                .Include(u => u.unit_map_position)
                .FirstOrDefaultAsync(u => u.id == unitId, cancellationToken);
        }

        return await _context.storage_units
            .AsNoTracking()
            .Include(u => u.facility)
                .ThenInclude(f => f.facility_rates)
            .Include(u => u.unit_type)
            .Include(u => u.area)
            .Include(u => u.unit_map_position)
            .Include(u => u.unit_status_histories)
            .Include(u => u.unit_allocations)
                .ThenInclude(a => a.agreement)
                    .ThenInclude(ag => ag.customer)
            .Include(u => u.maintenance_work_orders)
            .Include(u => u.inspections)
            .FirstOrDefaultAsync(u => u.id == unitId, cancellationToken);
    }

    public async Task<bool> UnitCodeExistsInFacilityAsync(
        long facilityId,
        string unitCode,
        long? excludeUnitId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(unitCode)) return false;

        var cleanCode = unitCode.Trim().ToLower();
        var query = _context.storage_units
            .AsNoTracking()
            .Where(u => u.facility_id == facilityId && u.unit_code.ToLower() == cleanCode);

        if (excludeUnitId.HasValue)
        {
            query = query.Where(u => u.id != excludeUnitId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    // =========================================================================
    // Lookups & Scope Validations
    // =========================================================================

    public async Task<IReadOnlyList<facility>> GetLookupFacilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.facilities
            .AsNoTracking()
            .Where(f => f.status == "active")
            .OrderBy(f => f.name)
            .ThenBy(f => f.id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Finding 3 fix: When facilityId is provided, returns ONLY active unit types that have a facility_rate configured
    /// at that specific facility. If no rate exists, returns empty list (no global fallback).
    /// When facilityId is null, returns all active unit types.
    /// </summary>
    public async Task<IReadOnlyList<unit_type>> GetLookupUnitTypesAsync(
        long? facilityId = null,
        CancellationToken cancellationToken = default)
    {
        if (facilityId.HasValue)
        {
            return await _context.unit_types
                .AsNoTracking()
                .Where(ut => ut.is_active &&
                    _context.facility_rates.Any(fr => fr.facility_id == facilityId.Value && fr.unit_type_id == ut.id))
                .Include(ut => ut.facility_rates.Where(fr => fr.facility_id == facilityId.Value))
                .OrderBy(ut => ut.name)
                .ThenBy(ut => ut.id)
                .ToListAsync(cancellationToken);
        }

        return await _context.unit_types
            .AsNoTracking()
            .Where(ut => ut.is_active)
            .Include(ut => ut.facility_rates)
            .OrderBy(ut => ut.name)
            .ThenBy(ut => ut.id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<facility_area>> GetLookupAreasAsync(
        long facilityId,
        CancellationToken cancellationToken = default)
    {
        return await _context.facility_areas
            .AsNoTracking()
            .Where(a => a.facility_id == facilityId && a.is_active)
            .OrderBy(a => a.display_order)
            .ThenBy(a => a.name)
            .ThenBy(a => a.id)
            .ToListAsync(cancellationToken);
    }

    public async Task<StorageUnitScopeValidationResultKhoiVTH> ValidateScopeReferencesAsync(
        long facilityId,
        long unitTypeId,
        long? areaId,
        CancellationToken cancellationToken = default)
    {
        var facility = await _context.facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.id == facilityId, cancellationToken);

        var unitType = await _context.unit_types
            .AsNoTracking()
            .FirstOrDefaultAsync(ut => ut.id == unitTypeId, cancellationToken);

        facility_area? area = null;
        if (areaId.HasValue)
        {
            area = await _context.facility_areas
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.id == areaId.Value, cancellationToken);
        }

        var result = new StorageUnitScopeValidationResultKhoiVTH
        {
            FacilityExists = facility != null,
            FacilityIsActive = facility != null && facility.status == "active",
            UnitTypeExists = unitType != null,
            UnitTypeIsActive = unitType != null && unitType.is_active,
            AreaId = areaId,
            AreaExists = !areaId.HasValue || area != null,
            AreaIsActive = !areaId.HasValue || (area != null && area.is_active),
            AreaBelongsToFacility = !areaId.HasValue || (area != null && area.facility_id == facilityId)
        };

        if (!result.FacilityExists)
        {
            result.FailureReason = "FACILITY_NOT_FOUND";
        }
        else if (!result.FacilityIsActive)
        {
            result.FailureReason = "FACILITY_INACTIVE";
        }
        else if (!result.UnitTypeExists)
        {
            result.FailureReason = "UNIT_TYPE_NOT_FOUND";
        }
        else if (!result.UnitTypeIsActive)
        {
            result.FailureReason = "UNIT_TYPE_INACTIVE";
        }
        else if (areaId.HasValue && !result.AreaExists)
        {
            result.FailureReason = "AREA_NOT_FOUND";
        }
        else if (areaId.HasValue && !result.AreaIsActive)
        {
            result.FailureReason = "AREA_INACTIVE";
        }
        else if (areaId.HasValue && !result.AreaBelongsToFacility)
        {
            result.FailureReason = "AREA_FACILITY_MISMATCH";
        }

        return result;
    }

    // =========================================================================
    // Reference Inspection & Capacity Impact
    // =========================================================================

    public async Task<StorageUnitReferenceSummaryKhoiVTH> GetStorageUnitReferenceSummaryAsync(
        long unitId,
        CancellationToken cancellationToken = default)
    {
        var unitInfo = await _context.storage_units
            .AsNoTracking()
            .Where(u => u.id == unitId)
            .Select(u => new
            {
                u.id,
                u.unit_code,
                u.facility_id,
                u.unit_type_id,
                AllocationsCount = u.unit_allocations.Count(),
                ActiveAllocationsCount = u.unit_allocations.Count(a => a.status == "active"),
                InspectionsCount = u.inspections.Count(),
                MaintenanceOrdersCount = u.maintenance_work_orders.Count(),
                ActiveMaintenanceOrdersCount = u.maintenance_work_orders.Count(m =>
                    m.blocks_booking && m.status != "completed" && m.status != "cancelled"),
                SupportTicketsCount = _context.support_tickets.Count(st => st.storage_unit_id == unitId),
                TransferRequestsFromCount = u.unit_transfer_requestfrom_units.Count(),
                TransferRequestsToCount = u.unit_transfer_requestto_units.Count(),
                StatusHistoryCount = u.unit_status_histories.Count(),
                HasMapPosition = u.unit_map_position != null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (unitInfo == null)
        {
            throw new KeyNotFoundException($"Storage unit with ID {unitId} was not found.");
        }

        return new StorageUnitReferenceSummaryKhoiVTH
        {
            StorageUnitId = unitInfo.id,
            UnitCode = unitInfo.unit_code,
            FacilityId = unitInfo.facility_id,
            UnitTypeId = unitInfo.unit_type_id,
            AllocationsCount = unitInfo.AllocationsCount,
            ActiveAllocationsCount = unitInfo.ActiveAllocationsCount,
            InspectionsCount = unitInfo.InspectionsCount,
            MaintenanceWorkOrdersCount = unitInfo.MaintenanceOrdersCount,
            ActiveMaintenanceWorkOrdersCount = unitInfo.ActiveMaintenanceOrdersCount,
            SupportTicketsCount = unitInfo.SupportTicketsCount,
            TransferRequestsFromCount = unitInfo.TransferRequestsFromCount,
            TransferRequestsToCount = unitInfo.TransferRequestsToCount,
            StatusHistoryCount = unitInfo.StatusHistoryCount,
            HasMapPosition = unitInfo.HasMapPosition
        };
    }

    public async Task<StorageUnitCapacityImpactKhoiVTH> GetCapacityImpactAsync(
        long facilityId,
        long unitTypeId,
        long? targetUnitId = null,
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveNowUtc = nowUtc ?? DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(effectiveNowUtc.UtcDateTime);

        // 1. Candidate available units currently unallocated, available, listed, without blocking maintenance
        var candidateUnits = await _context.storage_units
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
                    a.allocation_end_date > today))
            .CountAsync(cancellationToken);

        var totalUnits = await _context.storage_units
            .AsNoTracking()
            .Where(u => u.facility_id == facilityId && u.unit_type_id == unitTypeId)
            .CountAsync(cancellationToken);

        // 2. Active unassigned reservation holds (status 'pending' or 'awaiting_deposit' with active hold_until)
        var unassignedHolds = await _context.reservations
            .AsNoTracking()
            .Where(r =>
                r.facility_id == facilityId &&
                r.unit_type_id == unitTypeId &&
                r.end_date > today &&
                (r.status == "pending" || r.status == "awaiting_deposit") &&
                r.hold_until > effectiveNowUtc &&
                !_context.unit_allocations.Any(ua => ua.status == "active" &&
                    (ua.reservation_id == r.id || (ua.agreement != null && ua.agreement.reservation_id == r.id))))
            .CountAsync(cancellationToken);

        // 3. Active unassigned confirmed reservations (deposit paid, awaiting check-in assignment)
        var unassignedConfirmed = await _context.reservations
            .AsNoTracking()
            .Where(r =>
                r.facility_id == facilityId &&
                r.unit_type_id == unitTypeId &&
                r.end_date > today &&
                r.status == "confirmed" &&
                !_context.unit_allocations.Any(ua => ua.status == "active" &&
                    (ua.reservation_id == r.id || (ua.agreement != null && ua.agreement.reservation_id == r.id))))
            .CountAsync(cancellationToken);

        // 4. Check if the target unit specifically contributes to the candidate pool
        var isTargetCandidate = false;
        if (targetUnitId.HasValue)
        {
            isTargetCandidate = await _context.storage_units
                .AsNoTracking()
                .Where(u =>
                    u.id == targetUnitId.Value &&
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
                        a.allocation_end_date > today))
                .AnyAsync(cancellationToken);
        }

        return new StorageUnitCapacityImpactKhoiVTH
        {
            FacilityId = facilityId,
            UnitTypeId = unitTypeId,
            TargetUnitId = targetUnitId,
            IsTargetUnitCurrentlyCandidate = isTargetCandidate,
            TotalUnitsCount = totalUnits,
            CandidateAvailableUnitsCount = candidateUnits,
            UnassignedHoldsCount = unassignedHolds,
            UnassignedConfirmedCount = unassignedConfirmed,
            EvaluatedAtUtc = effectiveNowUtc
        };
    }

    // =========================================================================
    // Atomic Mutations Under Capacity AppLock
    // =========================================================================

    public async Task<storage_unit> CreateStorageUnitWithLockAsync(
        storage_unit newUnit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newUnit);

        var lockKey = GetCapacityLockKey(newUnit.facility_id, newUnit.unit_type_id);
        var isRelational = _context.Database.IsRelational();
        var acquiredSemaphores = new List<SemaphoreSlim>();
        IDbContextTransaction? transaction = null;

        try
        {
            if (!isRelational)
            {
                var sem = InMemorySemaphores.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
                var entered = await sem.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                if (!entered)
                {
                    throw new StorageUnitLockTimeoutException("CONCURRENCY_LOCK_TIMEOUT");
                }
                acquiredSemaphores.Add(sem);
            }
            else
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await AcquireAppLockAsync(lockKey, cancellationToken);
            }

            // Scope integrity validation
            var scope = await ValidateScopeReferencesAsync(newUnit.facility_id, newUnit.unit_type_id, newUnit.area_id, cancellationToken);
            if (!scope.IsValid)
            {
                if (scope.FailureReason == "AREA_FACILITY_MISMATCH")
                {
                    throw new StorageUnitAreaScopeMismatchException("Unit area must belong to the same facility.");
                }
                throw new StorageUnitPersistenceException($"Invalid scope reference: {scope.FailureReason}");
            }

            // Unit code uniqueness within facility
            var exists = await UnitCodeExistsInFacilityAsync(newUnit.facility_id, newUnit.unit_code, null, cancellationToken);
            if (exists)
            {
                throw new StorageUnitUniqueConstraintException(
                    $"Storage unit with code '{newUnit.unit_code}' already exists in facility ID {newUnit.facility_id}.");
            }

            var nowUtc = DateTimeOffset.UtcNow;
            newUnit.created_at = nowUtc;
            newUnit.updated_at = nowUtc;
            if (string.IsNullOrWhiteSpace(newUnit.physical_status))
            {
                newUnit.physical_status = "available";
            }

            _context.storage_units.Add(newUnit);
            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return newUnit;
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                    // Ignore rollback errors
                }
            }
            throw MapException(ex);
        }
        finally
        {
            transaction?.Dispose();
            for (int i = acquiredSemaphores.Count - 1; i >= 0; i--)
            {
                acquiredSemaphores[i].Release();
            }
        }
    }

    /// <summary>
    /// Finding 1 & 2 fix (P1 & P2):
    /// - P1: Single execution of applyUpdates via staged mutation plan before lock acquisition.
    ///   Captures immutable mutation plan with scalar fields; never calls applyUpdates twice.
    ///   Strictly verifies caller target IDs against mutation plan; rejects mismatches before lock/save.
    /// - P2: Try/finally protected semaphore acquisition ensures any partially acquired semaphores
    ///   are released in reverse order upon cancellation, timeout, or exception without leaks.
    /// - Concurrency & Reload: Reads fresh record under lock via AsNoTracking, strictly verifies
    ///   expectedUpdatedAt, reloads tracked entity in ChangeTracker, applies mutation plan, and commits.
    /// </summary>
    public async Task<storage_unit> UpdateStorageUnitWithLockAsync(
        long unitId,
        DateTimeOffset expectedUpdatedAt,
        Action<storage_unit> applyUpdates,
        long? targetFacilityId = null,
        long? targetUnitTypeId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applyUpdates);

        // Pre-read unit without tracking to discover initial state
        var preCheck = await _context.storage_units
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.id == unitId, cancellationToken);

        if (preCheck == null)
        {
            throw new KeyNotFoundException($"Storage unit with ID {unitId} was not found.");
        }

        // P1: Create detached staging copy with initial scalar fields (no navigation properties)
        var stagedUnit = new storage_unit
        {
            id = preCheck.id,
            facility_id = preCheck.facility_id,
            unit_type_id = preCheck.unit_type_id,
            area_id = preCheck.area_id,
            unit_code = preCheck.unit_code,
            physical_status = preCheck.physical_status,
            is_listed = preCheck.is_listed,
            floor_label = preCheck.floor_label,
            zone_label = preCheck.zone_label,
            notes = preCheck.notes,
            created_at = preCheck.created_at,
            updated_at = preCheck.updated_at
        };

        // P1: Execute applyUpdates EXACTLY ONCE on the detached staging instance
        applyUpdates(stagedUnit);

        // P1: Capture immutable mutation plan of allowed editable scalar fields
        var mutationPlan = new StorageUnitMutationPlan(
            SourceFacilityId: preCheck.facility_id,
            SourceUnitTypeId: preCheck.unit_type_id,
            TargetFacilityId: stagedUnit.facility_id,
            TargetUnitTypeId: stagedUnit.unit_type_id,
            TargetAreaId: stagedUnit.area_id,
            TargetUnitCode: stagedUnit.unit_code,
            TargetFloorLabel: stagedUnit.floor_label,
            TargetZoneLabel: stagedUnit.zone_label,
            TargetPhysicalStatus: stagedUnit.physical_status,
            TargetIsListed: stagedUnit.is_listed,
            TargetNotes: stagedUnit.notes
        );

        // P1: Strict validation of caller's target IDs against actual mutation plan
        if (targetFacilityId.HasValue && targetFacilityId.Value != mutationPlan.TargetFacilityId)
        {
            throw new ArgumentException(
                $"Specified targetFacilityId ({targetFacilityId.Value}) does not match the actual mutated facility_id ({mutationPlan.TargetFacilityId}).",
                nameof(targetFacilityId));
        }

        if (targetUnitTypeId.HasValue && targetUnitTypeId.Value != mutationPlan.TargetUnitTypeId)
        {
            throw new ArgumentException(
                $"Specified targetUnitTypeId ({targetUnitTypeId.Value}) does not match the actual mutated unit_type_id ({mutationPlan.TargetUnitTypeId}).",
                nameof(targetUnitTypeId));
        }

        // Determine all required capacity lock keys covering source and target in deterministic Ordinal order
        var keysToLock = DetermineCapacityLockKeys(
            mutationPlan.SourceFacilityId,
            mutationPlan.SourceUnitTypeId,
            mutationPlan.TargetFacilityId,
            mutationPlan.TargetUnitTypeId);

        var isRelational = _context.Database.IsRelational();
        var acquiredSemaphores = new List<SemaphoreSlim>();
        IDbContextTransaction? transaction = null;

        // P2: Protect all lock acquisitions inside try/finally to guarantee release on cancellation or exception
        try
        {
            if (!isRelational)
            {
                foreach (var key in keysToLock)
                {
                    var sem = InMemorySemaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
                    var entered = await sem.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                    if (!entered)
                    {
                        throw new StorageUnitLockTimeoutException("CONCURRENCY_LOCK_TIMEOUT");
                    }
                    acquiredSemaphores.Add(sem);
                }
            }
            else
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                foreach (var key in keysToLock)
                {
                    await AcquireAppLockAsync(key, cancellationToken);
                }
            }

            // Fresh read directly from database under lock with AsNoTracking
            var freshDbUnit = await _context.storage_units
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.id == unitId, cancellationToken);

            if (freshDbUnit == null)
            {
                throw new KeyNotFoundException($"Storage unit with ID {unitId} was not found.");
            }

            // Optimistic concurrency verification against actual database timestamp
            if (freshDbUnit.updated_at != expectedUpdatedAt)
            {
                throw new StorageUnitConcurrencyException(
                    $"Concurrency conflict for storage unit ID {unitId}. Expected updated_at: {expectedUpdatedAt:O}, actual in database: {freshDbUnit.updated_at:O}.");
            }

            // Verify that the acquired capacity locks cover the true database source state
            if (freshDbUnit.facility_id != mutationPlan.SourceFacilityId ||
                freshDbUnit.unit_type_id != mutationPlan.SourceUnitTypeId)
            {
                throw new StorageUnitConcurrencyException(
                    $"Storage unit ID {unitId} facility or unit type was modified concurrently in database.");
            }

            // Reload or fetch the tracked entity in ChangeTracker
            var trackedEntry = _context.ChangeTracker.Entries<storage_unit>()
                .FirstOrDefault(e => e.Entity.id == unitId);

            storage_unit unit;
            if (trackedEntry != null)
            {
                await trackedEntry.ReloadAsync(cancellationToken);
                if (trackedEntry.State == EntityState.Detached)
                {
                    throw new KeyNotFoundException($"Storage unit with ID {unitId} was not found.");
                }
                unit = trackedEntry.Entity;
            }
            else
            {
                var fetchedUnit = await _context.storage_units
                    .FirstOrDefaultAsync(u => u.id == unitId, cancellationToken);

                if (fetchedUnit == null)
                {
                    throw new KeyNotFoundException($"Storage unit with ID {unitId} was not found.");
                }
                unit = fetchedUnit;
            }

            // Re-verify tracked entity timestamp after reload
            if (unit.updated_at != expectedUpdatedAt)
            {
                throw new StorageUnitConcurrencyException(
                    $"Concurrency conflict for storage unit ID {unitId}. Expected updated_at: {expectedUpdatedAt:O}, actual: {unit.updated_at:O}.");
            }

            var originalFacilityId = unit.facility_id;
            var originalCode = unit.unit_code;

            // P1: Apply scalar fields strictly from immutable mutation plan (DO NOT call applyUpdates again!)
            unit.facility_id = mutationPlan.TargetFacilityId;
            unit.unit_type_id = mutationPlan.TargetUnitTypeId;
            unit.area_id = mutationPlan.TargetAreaId;
            unit.unit_code = mutationPlan.TargetUnitCode;
            unit.floor_label = mutationPlan.TargetFloorLabel;
            unit.zone_label = mutationPlan.TargetZoneLabel;
            unit.physical_status = mutationPlan.TargetPhysicalStatus;
            unit.is_listed = mutationPlan.TargetIsListed;
            unit.notes = mutationPlan.TargetNotes;

            // Verify code uniqueness if code or facility was modified
            if (unit.unit_code != originalCode || unit.facility_id != originalFacilityId)
            {
                var codeExists = await UnitCodeExistsInFacilityAsync(unit.facility_id, unit.unit_code, unit.id, cancellationToken);
                if (codeExists)
                {
                    throw new StorageUnitUniqueConstraintException(
                        $"Storage unit with code '{unit.unit_code}' already exists in facility ID {unit.facility_id}.");
                }
            }

            // Verify scope references
            var scope = await ValidateScopeReferencesAsync(unit.facility_id, unit.unit_type_id, unit.area_id, cancellationToken);
            if (!scope.IsValid)
            {
                if (scope.FailureReason == "AREA_FACILITY_MISMATCH")
                {
                    throw new StorageUnitAreaScopeMismatchException("Unit area must belong to the same facility.");
                }
                throw new StorageUnitPersistenceException($"Invalid scope reference: {scope.FailureReason}");
            }

            unit.updated_at = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return unit;
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                    // Ignore rollback errors to preserve original exception
                }
            }
            throw MapException(ex);
        }
        finally
        {
            transaction?.Dispose();
            // P2: Release all acquired semaphores in reverse order of acquisition
            for (int i = acquiredSemaphores.Count - 1; i >= 0; i--)
            {
                acquiredSemaphores[i].Release();
            }
        }
    }

    /// <summary>
    /// Finding 1 fix:
    /// - Fresh Optimistic Concurrency: Reads actual DB record via AsNoTracking after acquiring lock,
    ///   reloads any tracked entity in ChangeTracker, and strictly verifies expectedUpdatedAt before deletion.
    /// </summary>
    public async Task<StorageUnitDeleteResultKhoiVTH> DeleteStorageUnitWithLockAsync(
        long unitId,
        DateTimeOffset expectedUpdatedAt,
        Func<StorageUnitDeleteEvaluationContextKhoiVTH, Task<bool>>? evaluatePolicy = null,
        CancellationToken cancellationToken = default)
    {
        var preCheck = await _context.storage_units
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.id == unitId, cancellationToken);

        if (preCheck == null)
        {
            throw new KeyNotFoundException($"Storage unit with ID {unitId} was not found.");
        }

        var lockKey = GetCapacityLockKey(preCheck.facility_id, preCheck.unit_type_id);
        var isRelational = _context.Database.IsRelational();
        var acquiredSemaphores = new List<SemaphoreSlim>();
        IDbContextTransaction? transaction = null;

        try
        {
            if (!isRelational)
            {
                var sem = InMemorySemaphores.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
                var entered = await sem.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                if (!entered)
                {
                    throw new StorageUnitLockTimeoutException("CONCURRENCY_LOCK_TIMEOUT");
                }
                acquiredSemaphores.Add(sem);
            }
            else
            {
                transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await AcquireAppLockAsync(lockKey, cancellationToken);
            }

            // Finding 1 fix: Fresh AsNoTracking check directly from database under active lock
            var freshDbUnit = await _context.storage_units
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.id == unitId, cancellationToken);

            if (freshDbUnit == null)
            {
                throw new KeyNotFoundException($"Storage unit with ID {unitId} was not found.");
            }

            if (freshDbUnit.updated_at != expectedUpdatedAt)
            {
                throw new StorageUnitConcurrencyException(
                    $"Concurrency conflict for storage unit ID {unitId}. Expected updated_at: {expectedUpdatedAt:O}, actual in database: {freshDbUnit.updated_at:O}.");
            }

            // Reload or fetch the tracked entity in ChangeTracker
            var trackedEntry = _context.ChangeTracker.Entries<storage_unit>()
                .FirstOrDefault(e => e.Entity.id == unitId);

            storage_unit unit;
            if (trackedEntry != null)
            {
                await trackedEntry.ReloadAsync(cancellationToken);
                if (trackedEntry.State == EntityState.Detached)
                {
                    throw new KeyNotFoundException($"Storage unit with ID {unitId} was not found.");
                }
                unit = trackedEntry.Entity;
                await _context.Entry(unit).Reference(u => u.unit_map_position).LoadAsync(cancellationToken);
            }
            else
            {
                var fetchedUnit = await _context.storage_units
                    .Include(u => u.unit_map_position)
                    .FirstOrDefaultAsync(u => u.id == unitId, cancellationToken);

                if (fetchedUnit == null)
                {
                    throw new KeyNotFoundException($"Storage unit with ID {unitId} was not found.");
                }
                unit = fetchedUnit;
            }

            // Double check tracked entity timestamp
            if (unit.updated_at != expectedUpdatedAt)
            {
                throw new StorageUnitConcurrencyException(
                    $"Concurrency conflict for storage unit ID {unitId}. Expected updated_at: {expectedUpdatedAt:O}, actual: {unit.updated_at:O}.");
            }

            // Gather reference summary and unassigned holds/confirmed capacity impact under the active lock
            var refSummary = await GetStorageUnitReferenceSummaryAsync(unitId, cancellationToken);
            var capacityImpact = await GetCapacityImpactAsync(unit.facility_id, unit.unit_type_id, unit.id, DateTimeOffset.UtcNow, cancellationToken);

            var evalContext = new StorageUnitDeleteEvaluationContextKhoiVTH
            {
                Unit = unit,
                ReferenceSummary = refSummary,
                CapacityImpact = capacityImpact
            };

            // Evaluate BLL delete policy atomically
            if (evaluatePolicy != null)
            {
                var policyApproved = await evaluatePolicy(evalContext);
                if (!policyApproved)
                {
                    if (transaction != null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }

                    return new StorageUnitDeleteResultKhoiVTH
                    {
                        UnitId = unitId,
                        UnitCode = unit.unit_code,
                        Success = false,
                        Message = "DELETE_POLICY_REJECTED",
                        ReferenceSummary = refSummary,
                        CapacityImpact = capacityImpact
                    };
                }
            }

            // Enforce referential integrity: reject deletion if inbound FK references exist
            if (refSummary.HasAnyReferences)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return new StorageUnitDeleteResultKhoiVTH
                {
                    UnitId = unitId,
                    UnitCode = unit.unit_code,
                    Success = false,
                    Message = "STORAGE_UNIT_HAS_EXISTING_REFERENCES",
                    ReferenceSummary = refSummary,
                    CapacityImpact = capacityImpact
                };
            }

            // Remove associated unit map position if mapped
            if (unit.unit_map_position != null)
            {
                _context.unit_map_positions.Remove(unit.unit_map_position);
            }

            _context.storage_units.Remove(unit);
            await _context.SaveChangesAsync(cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new StorageUnitDeleteResultKhoiVTH
            {
                UnitId = unitId,
                UnitCode = unit.unit_code,
                Success = true,
                Message = "STORAGE_UNIT_DELETED",
                ReferenceSummary = refSummary,
                CapacityImpact = capacityImpact
            };
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                    // Ignore rollback errors
                }
            }
            throw MapException(ex);
        }
        finally
        {
            transaction?.Dispose();
            for (int i = acquiredSemaphores.Count - 1; i >= 0; i--)
            {
                acquiredSemaphores[i].Release();
            }
        }
    }

    // =========================================================================
    // Helpers & Exception Mapping
    // =========================================================================

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
            throw new StorageUnitLockTimeoutException("CONCURRENCY_LOCK_TIMEOUT");
        }
    }

    private static Exception MapException(Exception ex)
    {
        if (ex is StorageUnitPersistenceException)
        {
            return ex;
        }

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
            2601 or 2627 => new StorageUnitUniqueConstraintException("STORAGE_UNIT_CODE_ALREADY_EXISTS", sqlEx),
            547 => new StorageUnitForeignKeyException("STORAGE_UNIT_FOREIGN_KEY_VIOLATION", sqlEx),
            51020 => new StorageUnitAreaScopeMismatchException("Unit area must belong to the same facility.", sqlEx),
            51050 => new StorageUnitInvalidStatusTransitionException("Invalid storage-unit status transition.", sqlEx),
            1205 or 1222 => new StorageUnitLockTimeoutException("CONCURRENCY_LOCK_TIMEOUT", sqlEx),
            _ => new StorageUnitPersistenceException("STORAGE_UNIT_PERSISTENCE_ERROR", sqlEx)
        };
    }
}
