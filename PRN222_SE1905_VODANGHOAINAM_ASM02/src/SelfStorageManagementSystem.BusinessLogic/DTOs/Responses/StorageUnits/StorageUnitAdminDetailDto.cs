using System;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;

/// <summary>
/// Detailed administrative DTO for a single storage unit including type specs, pricing,
/// reference summaries, and active occupancy metadata.
/// </summary>
public class StorageUnitAdminDetailDto : StorageUnitAdminDto
{
    // Unit Type physical dimensions and capacity
    public decimal UnitTypeWidthMeters { get; set; }
    public decimal UnitTypeLengthMeters { get; set; }
    public decimal UnitTypeHeightMeters { get; set; }
    public decimal? UnitTypeFloorAreaSqm { get; set; }
    public decimal? UnitTypeVolumeCbm { get; set; }

    // Unit Type pricing
    public decimal MonthlyRate { get; set; }

    // Relational reference counts for safety and audit analysis
    public int AllocationsCount { get; set; }
    public int ActiveAllocationsCount { get; set; }
    public int InspectionsCount { get; set; }
    public int MaintenanceOrdersCount { get; set; }
    public int ActiveMaintenanceOrdersCount { get; set; }
    public int SupportTicketsCount { get; set; }
    public int StatusHistoryCount { get; set; }
    public bool HasMapPosition { get; set; }

    // Active occupancy information (if currently allocated)
    public string? CurrentCustomerName { get; set; }
    public string? CurrentAgreementNo { get; set; }
    public DateTimeOffset? CurrentOccupiedSince { get; set; }
}
