namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;

public class ReservationReportItemResponse
{
    public long ReservationId { get; set; }
    public string ReservationCode { get; set; } = string.Empty;
    public long FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public long UnitTypeId { get; set; }
    public string UnitTypeName { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal QuotedTotal { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReservationReportResponse
{
    public DateOnly? FilterStartDate { get; set; }
    public DateOnly? FilterEndDate { get; set; }
    public long? FilterFacilityId { get; set; }
    public string? FilterStatus { get; set; }

    public int TotalReservationCount { get; set; }
    public int ActiveHoldCount { get; set; }
    public int ConfirmedCount { get; set; }
    public int CancelledCount { get; set; }
    public int ExpiredCount { get; set; }
    public decimal TotalQuotedAmount { get; set; }

    public IReadOnlyList<ReservationReportItemResponse> Items { get; set; } = Array.Empty<ReservationReportItemResponse>();
}
