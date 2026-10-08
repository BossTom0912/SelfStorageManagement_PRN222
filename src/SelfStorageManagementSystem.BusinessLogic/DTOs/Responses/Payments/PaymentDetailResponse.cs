namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;

public class PaymentDetailResponse
{
    public long PaymentId { get; set; }
    public long CustomerId { get; set; }
    public long TargetInvoiceId { get; set; }
    public long? ReservationId { get; set; }
    public string? ReservationCode { get; set; }
    public long? AgreementId { get; set; }
    public string? AgreementNo { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Method { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? PaidAt { get; set; }
    public string? FailureReason { get; set; }
    public bool ReconciliationRequired { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? HoldUntil { get; set; }
    public bool HoldExpired { get; set; }
}
