namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public class PaymentGatewayOrder
{
    public long PaymentId { get; set; }
    public long InvoiceId { get; set; }
    public long ReservationId { get; set; }
    public string ReservationCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public DateTimeOffset HoldUntil { get; set; }
    public string ClientIp { get; set; } = "127.0.0.1";
    public string Description { get; set; } = string.Empty;
}

public class PaymentGatewayCallbackResult
{
    public bool IsValidSignature { get; set; }
    public bool IsSuccess { get; set; }
    public string? TxnRef { get; set; }
    public string? ProviderTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string? ResponseCode { get; set; }
    public string? TransactionStatus { get; set; }
    public string? FailureReason { get; set; }
    public string? BankCode { get; set; }
    public DateTimeOffset? PayDate { get; set; }
    public string? IpnResponseCode { get; set; }
    public string? IpnResponseMessage { get; set; }
    public IDictionary<string, string> RawParameters { get; set; } = new Dictionary<string, string>();
}

public interface IPaymentGateway
{
    string ProviderName { get; }

    Task<string> CreateCheckoutUrlAsync(
        PaymentGatewayOrder order,
        CancellationToken cancellationToken = default);

    Task<PaymentGatewayCallbackResult> VerifyCallbackAsync(
        IDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);
}
