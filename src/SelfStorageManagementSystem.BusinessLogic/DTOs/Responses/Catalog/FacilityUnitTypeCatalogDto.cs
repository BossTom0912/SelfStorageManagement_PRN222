namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Catalog;

public class FacilityUnitTypeCatalogDto
{
    public long UnitTypeId { get; set; }
    public long FacilityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal WidthM { get; set; }
    public decimal LengthM { get; set; }
    public decimal HeightM { get; set; }
    public decimal? AreaM2 { get; set; }
    public decimal? VolumeM3 { get; set; }
    public bool ClimateControlled { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public string? Description { get; set; }

    // Rates from core.facility_rates
    public decimal MonthlyRate { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal BookingFee { get; set; }
    public DateOnly RateValidFrom { get; set; }
    public DateOnly? RateValidTo { get; set; }

    // Availability breakdown & honest labeling
    public int TotalListedUnits { get; set; }
    public int EstimatedAvailableUnits { get; set; }
    public string AvailabilityNote { get; set; } = string.Empty;
}
