using System.Text.Json.Serialization;

namespace SelfStorageManagementSystem.WpfClient.Models;

public class CreateReservationClientRequest
{
    public long FacilityId { get; set; }
    public long UnitTypeId { get; set; }
    public DateOnly RentalStartDate { get; set; }
    public DateOnly RentalEndDate { get; set; }
}

public class CancelReservationClientRequest
{
    public string? Reason { get; set; }
}

public class ReservationDetailClientModel
{
    public long Id { get; set; }
    public string ReservationCode { get; set; } = string.Empty;

    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }

    public long FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public string FacilityCode { get; set; } = string.Empty;
    public string FacilityAddress { get; set; } = string.Empty;

    public long UnitTypeId { get; set; }
    public string UnitTypeCode { get; set; } = string.Empty;
    public string UnitTypeName { get; set; } = string.Empty;
    public decimal UnitTypeAreaM2 { get; set; }
    public bool ClimateControlled { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int RentalMonths { get; set; }

    public decimal MonthlyRateSnapshot { get; set; }
    public decimal DepositSnapshot { get; set; }
    public decimal BookingFeeSnapshot { get; set; }
    public decimal DiscountSnapshot { get; set; }
    public decimal QuotedTotal { get; set; }

    public string Status { get; set; } = string.Empty;
    public string StatusDisplayName { get; set; } = string.Empty;

    public DateTimeOffset HoldUntil { get; set; }
    public DateTimeOffset ServerNow { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsHoldActive { get; set; }
    public int RemainingSeconds { get; set; }
    public bool CanCancel { get; set; }
    public string PolicyNote { get; set; } = string.Empty;

    // UI helpers
    [JsonIgnore]
    public string FormattedHoldUntil => HoldUntil.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    [JsonIgnore]
    public string FormattedDateRange => $"{StartDate:dd/MM/yyyy} ➜ {EndDate:dd/MM/yyyy} ({RentalMonths} tháng)";

    [JsonIgnore]
    public string StatusColorHex => Status switch
    {
        "pending" => IsHoldActive ? "#D97706" : "#DC2626", // Amber-600 if active, Red-600 if expired
        "awaiting_deposit" => "#D97706",
        "confirmed" => "#059669", // Emerald-600
        "checked_in" => "#2563EB", // Blue-600
        "completed" => "#475569", // Slate-600
        "cancelled" => "#64748B", // Slate-500
        "expired" => "#DC2626", // Red-600
        _ => "#6B7280"
    };
}

public class ReservationListItemClientModel
{
    public long Id { get; set; }
    public string ReservationCode { get; set; } = string.Empty;

    public long FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;

    public long UnitTypeId { get; set; }
    public string UnitTypeName { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public decimal MonthlyRateSnapshot { get; set; }
    public decimal DepositSnapshot { get; set; }
    public decimal QuotedTotal { get; set; }

    public string Status { get; set; } = string.Empty;
    public string StatusDisplayName { get; set; } = string.Empty;

    public DateTimeOffset HoldUntil { get; set; }
    public DateTimeOffset ServerNow { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public bool IsHoldActive { get; set; }
    public int RemainingSeconds { get; set; }
    public bool CanCancel { get; set; }

    [JsonIgnore]
    public string FormattedDateRange => $"{StartDate:dd/MM/yyyy} ➜ {EndDate:dd/MM/yyyy}";

    [JsonIgnore]
    public string StatusColorHex => Status switch
    {
        "pending" => IsHoldActive ? "#D97706" : "#DC2626",
        "confirmed" => "#059669",
        "cancelled" => "#64748B",
        "expired" => "#DC2626",
        _ => "#6B7280"
    };
}
