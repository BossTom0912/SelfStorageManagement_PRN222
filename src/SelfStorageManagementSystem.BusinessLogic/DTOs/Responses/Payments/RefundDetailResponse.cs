namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;

public class RefundDetailResponse
{
    public long Id { get; set; }
    public long PaymentId { get; set; }
    public long? AgreementId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Reason { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? ProviderRefundId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long? RequestedBy { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? Decision { get; set; }
    public long? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
