namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;

public class ReservationListItemResponse
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
}
