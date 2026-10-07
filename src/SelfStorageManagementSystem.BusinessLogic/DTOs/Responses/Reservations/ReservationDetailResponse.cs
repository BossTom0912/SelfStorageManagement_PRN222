namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;

public class ReservationDetailResponse
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

    public string PolicyNote { get; set; } =
        "Theo quy tắc BR-RSV-03: Đơn đặt chỗ giữ sức chứa theo Loại Kho. Ô kho cụ thể sẽ được gán tối đa 24 giờ trước hoặc tại thời điểm Check-in.";
}
