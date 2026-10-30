using System;
using System.Collections.Generic;
using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.DataAccess.Models;

/// <summary>
/// Query criteria for searching and paginating storage units in admin repository.
/// Translates directly to SQL OFFSET/FETCH pagination without in-memory dataset buffering.
/// </summary>
public class StorageUnitAdminQueryParameters
{
    public long? FacilityId { get; set; }
    public IReadOnlyList<long>? FacilityIds { get; set; }
    public long? UnitTypeId { get; set; }
    public long? AreaId { get; set; }
    public string? PhysicalStatus { get; set; }
    public bool? IsListed { get; set; }
    public string? SearchTerm { get; set; }
    public string? SortBy { get; set; } = "unit_code";
    public bool SortDescending { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>
/// Summary of all inbound foreign key references to a storage_unit.
/// Used to verify referential integrity prior to delete operations.
/// </summary>
public class StorageUnitReferenceSummary
{
    public long StorageUnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public long FacilityId { get; set; }
    public long UnitTypeId { get; set; }

    /// <summary>
    /// Foreign key: core.unit_allocations.storage_unit_id
    /// </summary>
    public int AllocationsCount { get; set; }

    /// <summary>
    /// Active allocations: core.unit_allocations where status = 'active'
    /// </summary>
    public int ActiveAllocationsCount { get; set; }

    /// <summary>
    /// Foreign key: core.inspections.storage_unit_id
    /// </summary>
    public int InspectionsCount { get; set; }

    /// <summary>
    /// Foreign key: core.maintenance_work_orders.storage_unit_id
    /// </summary>
    public int MaintenanceWorkOrdersCount { get; set; }

    /// <summary>
    /// Active blocking maintenance orders
    /// </summary>
    public int ActiveMaintenanceWorkOrdersCount { get; set; }

    /// <summary>
    /// Foreign key: core.support_tickets.storage_unit_id
    /// </summary>
    public int SupportTicketsCount { get; set; }

    /// <summary>
    /// Foreign key: core.unit_transfer_requests.from_unit_id
    /// </summary>
    public int TransferRequestsFromCount { get; set; }

    /// <summary>
    /// Foreign key: core.unit_transfer_requests.to_unit_id
    /// </summary>
    public int TransferRequestsToCount { get; set; }

    /// <summary>
    /// Foreign key: core.unit_status_history.storage_unit_id
    /// </summary>
    public int StatusHistoryCount { get; set; }

    /// <summary>
    /// Foreign key: core.unit_map_positions.unit_id (1-to-1)
    /// </summary>
    public bool HasMapPosition { get; set; }

    /// <summary>
    /// Total count of all historical records linking to this unit.
    /// </summary>
    public int TotalHistoricalReferencesCount =>
        AllocationsCount +
        InspectionsCount +
        MaintenanceWorkOrdersCount +
        SupportTicketsCount +
        TransferRequestsFromCount +
        TransferRequestsToCount +
        StatusHistoryCount;

    /// <summary>
    /// Indicates whether any historical or active relational dependency exists.
    /// If true, hard deletion in SQL will either be blocked by FK constraints or destroy audit history.
    /// </summary>
    public bool HasAnyReferences => TotalHistoricalReferencesCount > 0;

    /// <summary>
    /// Indicates whether the unit can safely be hard deleted without FK constraint violations.
    /// </summary>
    public bool CanHardDelete => !HasAnyReferences;
}

/// <summary>
/// Capacity insights evaluated atomically under the facility+unit_type capacity lock.
/// Tracks candidate available units against unassigned holds and confirmed reservations.
/// </summary>
public class StorageUnitCapacityImpact
{
    public long FacilityId { get; set; }
    public long UnitTypeId { get; set; }
    public long? TargetUnitId { get; set; }

    /// <summary>
    /// True if the targeted unit currently qualifies as a candidate available unit.
    /// (is_listed = 1, physical_status = 'available', unit_type.is_active = 1, no blocking maintenance, no active allocation).
    /// </summary>
    public bool IsTargetUnitCurrentlyCandidate { get; set; }

    /// <summary>
    /// Total count of storage units belonging to this facility and unit type.
    /// </summary>
    public int TotalUnitsCount { get; set; }

    /// <summary>
    /// Total candidate available units currently able to fulfill unassigned reservations.
    /// </summary>
    public int CandidateAvailableUnitsCount { get; set; }

    /// <summary>
    /// Count of active unassigned reservation holds (status 'pending' or 'awaiting_deposit' with hold_until > now).
    /// </summary>
    public int UnassignedHoldsCount { get; set; }

    /// <summary>
    /// Count of active unassigned confirmed reservations (deposit paid, awaiting check-in assignment).
    /// </summary>
    public int UnassignedConfirmedCount { get; set; }

    /// <summary>
    /// Total unassigned active reservations (holds + confirmed).
    /// </summary>
    public int TotalUnassignedActiveReservationsCount => UnassignedHoldsCount + UnassignedConfirmedCount;

    /// <summary>
    /// Current net available capacity before deletion: CandidateAvailableUnitsCount - TotalUnassignedActiveReservationsCount.
    /// </summary>
    public int NetAvailableCapacity => CandidateAvailableUnitsCount - TotalUnassignedActiveReservationsCount;

    /// <summary>
    /// Candidate available units remaining if the target unit is deleted.
    /// </summary>
    public int CandidateUnitsAfterDelete =>
        IsTargetUnitCurrentlyCandidate ? Math.Max(0, CandidateAvailableUnitsCount - 1) : CandidateAvailableUnitsCount;

    /// <summary>
    /// Net capacity remaining after target unit deletion.
    /// </summary>
    public int NetCapacityAfterDelete => CandidateUnitsAfterDelete - TotalUnassignedActiveReservationsCount;

    /// <summary>
    /// True if deleting this unit would result in negative net capacity (overbooking / unfulfilled reservations).
    /// </summary>
    public bool WouldBreachCapacityIfDeleted => NetCapacityAfterDelete < 0;

    public DateTimeOffset EvaluatedAtUtc { get; set; }
}

/// <summary>
/// Scope validation checking facility, unit_type, and area consistency.
/// Prevents trigger 51020 violations (Unit area must belong to the same facility).
/// </summary>
public class StorageUnitScopeValidationResult
{
    public bool FacilityExists { get; set; }
    public bool FacilityIsActive { get; set; }
    public bool UnitTypeExists { get; set; }
    public bool UnitTypeIsActive { get; set; }
    public long? AreaId { get; set; }
    public bool AreaExists { get; set; }
    public bool AreaIsActive { get; set; }
    public bool AreaBelongsToFacility { get; set; }

    public bool IsValid =>
        FacilityExists &&
        FacilityIsActive &&
        UnitTypeExists &&
        UnitTypeIsActive &&
        (!AreaId.HasValue || (AreaExists && AreaIsActive && AreaBelongsToFacility));

    public string? FailureReason { get; set; }
}

/// <summary>
/// Evaluation context passed to BLL policy delegate during atomic deletion under capacity lock.
/// </summary>
public class StorageUnitDeleteEvaluationContext
{
    public storage_unit Unit { get; set; } = null!;
    public StorageUnitReferenceSummary ReferenceSummary { get; set; } = null!;
    public StorageUnitCapacityImpact CapacityImpact { get; set; } = null!;
}

/// <summary>
/// Result of an atomic storage unit deletion operation.
/// </summary>
public class StorageUnitDeleteResult
{
    public long UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool IsUnlistedOnly { get; set; }
    public string Message { get; set; } = string.Empty;
    public StorageUnitReferenceSummary? ReferenceSummary { get; set; }
    public StorageUnitCapacityImpact? CapacityImpact { get; set; }
}
