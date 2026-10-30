using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Models;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

/// <summary>
/// Repository interface for administrative storage unit queries and atomic mutations under capacity locks.
/// Operates strictly within DataAccess layer; does not evaluate HTTP permissions or UI concerns.
/// </summary>
public interface IStorageUnitAdminRepository : IRepository<storage_unit>
{
    /// <summary>
    /// Queries storage units matching filter criteria using database-level SQL pagination (OFFSET/FETCH).
    /// Does not buffer the entire dataset in memory.
    /// </summary>
    Task<(IReadOnlyList<storage_unit> Items, int TotalCount)> GetPagedStorageUnitsAsync(
        StorageUnitAdminQueryParameters queryParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single storage unit by identifier, optionally eager loading related navigation collections.
    /// </summary>
    Task<storage_unit?> GetStorageUnitByIdAsync(
        long unitId,
        bool includeDetails = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a given unit code already exists within the target facility.
    /// Supports excluding a specific unit ID for update scenarios.
    /// </summary>
    Task<bool> UnitCodeExistsInFacilityAsync(
        long facilityId,
        string unitCode,
        long? excludeUnitId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves active facilities for lookup and selection.
    /// </summary>
    Task<IReadOnlyList<facility>> GetLookupFacilitiesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves active unit types for lookup, optionally scoped to unit types configured for a facility.
    /// </summary>
    Task<IReadOnlyList<unit_type>> GetLookupUnitTypesAsync(
        long? facilityId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves active facility areas belonging to a specific facility.
    /// </summary>
    Task<IReadOnlyList<facility_area>> GetLookupAreasAsync(
        long facilityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates referential integrity and area facility assignment before mutation.
    /// Guarantees area belongs to the same facility (trigger 51020 requirement).
    /// </summary>
    Task<StorageUnitScopeValidationResult> ValidateScopeReferencesAsync(
        long facilityId,
        long unitTypeId,
        long? areaId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inspects and counts all inbound foreign key references to a storage unit,
    /// including allocations, inspections, maintenance orders, support tickets, transfers,
    /// unit map position, and unit status history.
    /// </summary>
    Task<StorageUnitReferenceSummary> GetStorageUnitReferenceSummaryAsync(
        long unitId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes candidate available units, unassigned holds, and unassigned confirmed reservations
    /// for a facility and unit type. Provides data needed for BLL delete capacity policy.
    /// </summary>
    Task<StorageUnitCapacityImpact> GetCapacityImpactAsync(
        long facilityId,
        long unitTypeId,
        long? targetUnitId = null,
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically creates a new storage unit under the facility+unit_type capacity lock.
    /// Validates code uniqueness, scope integrity, and sets initial timestamps.
    /// </summary>
    Task<storage_unit> CreateStorageUnitWithLockAsync(
        storage_unit newUnit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically updates a storage unit under capacity lock.
    /// Loads the tracked entity fresh, validates expectedUpdatedAt (optimistic concurrency),
    /// applies modifications to the loaded entity (no detached full-graph overwrite),
    /// updates updated_at, and commits changes.
    /// </summary>
    Task<storage_unit> UpdateStorageUnitWithLockAsync(
        long unitId,
        DateTimeOffset expectedUpdatedAt,
        Action<storage_unit> applyUpdates,
        long? targetFacilityId = null,
        long? targetUnitTypeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically executes a deletion under the capacity lock.
    /// Re-reads state and updated_at fresh after locking, verifies concurrency,
    /// computes reference summary and unassigned holds/confirmed capacity impact,
    /// invokes the BLL policy delegate within the transaction, and safely deletes or rolls back.
    /// </summary>
    Task<StorageUnitDeleteResult> DeleteStorageUnitWithLockAsync(
        long unitId,
        DateTimeOffset expectedUpdatedAt,
        Func<StorageUnitDeleteEvaluationContext, Task<bool>>? evaluatePolicy = null,
        CancellationToken cancellationToken = default);
}
