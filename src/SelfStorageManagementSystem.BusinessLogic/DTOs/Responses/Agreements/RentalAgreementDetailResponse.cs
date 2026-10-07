namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Agreements;

public class RentalAgreementDetailResponse
{
    public long AgreementId { get; set; }
    public string AgreementNo { get; set; } = string.Empty;
    public long ReservationId { get; set; }
    public string? ReservationCode { get; set; }
    public long CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public long FacilityId { get; set; }
    public string? FacilityName { get; set; }
    public long PolicyVersionId { get; set; }
    public string? PolicyVersion { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal MonthlyRateSnapshot { get; set; }
    public decimal DepositSnapshot { get; set; }
    public decimal DepositBalance { get; set; }
    public string Status { get; set; } = "scheduled";
    public DateTimeOffset? SignedAt { get; set; }
    public bool IsStorageUnitAllocated { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; }
    public string Notice { get; set; } =
        "Hợp đồng ở trạng thái scheduled (chờ bàn giao kho). Ô kho cụ thể và biên bản bàn giao sẽ được thực hiện khi làm thủ tục check-in tại quầy (Function 5).";
}
