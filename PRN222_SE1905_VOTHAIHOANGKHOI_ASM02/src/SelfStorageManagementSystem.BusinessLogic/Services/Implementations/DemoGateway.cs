using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

/// <summary>
/// Simulated payment gateway for Development / Test environments.
/// Generates demo hosted payment URLs and handles test outcomes.
/// </summary>
public class DemoGateway : IPaymentGateway
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DemoGateway> _logger;

    public string ProviderName => "demo";

    public DemoGateway(IConfiguration configuration, ILogger<DemoGateway> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<string> CreateCheckoutUrlAsync(
        PaymentGatewayOrder order,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var baseUrl = _configuration["Payment:DemoBaseUrl"] ?? "https://localhost:7031";
        var cleanBase = baseUrl.TrimEnd('/');
        var checkoutUrl = $"{cleanBase}/api/payments/demo/{order.PaymentId}/simulator?amount={order.Amount}&holdUntil={Uri.EscapeDataString(order.HoldUntil.ToString("O"))}";

        _logger.LogInformation("Generated Demo checkout URL for payment attempt {PaymentId}: {Url}", order.PaymentId, checkoutUrl);
        return Task.FromResult(checkoutUrl);
    }

    public Task<PaymentGatewayCallbackResult> VerifyCallbackAsync(
        IDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var txnRef = parameters.TryGetValue("txnRef", out var tRef) ? tRef : null;
        var isSuccess = !parameters.TryGetValue("isSuccess", out var succStr) ||
                        !bool.TryParse(succStr, out var succ) || succ;

        decimal amount = 0m;
        if (parameters.TryGetValue("amount", out var amtStr))
        {
            decimal.TryParse(amtStr, out amount);
        }

        var failureReason = parameters.TryGetValue("failureReason", out var reason) ? reason : null;
        var providerTxnId = parameters.TryGetValue("providerTxnId", out var pTxn)
            ? pTxn
            : $"DEMO-TXN-{Guid.NewGuid():N}".Substring(0, 20);

        var result = new PaymentGatewayCallbackResult
        {
            IsValidSignature = true, // Demo gateway simulator is inherently trusted in dev/test
            IsSuccess = isSuccess,
            TxnRef = txnRef,
            ProviderTransactionId = providerTxnId,
            Amount = amount,
            ResponseCode = isSuccess ? "00" : "99",
            TransactionStatus = isSuccess ? "00" : "02",
            FailureReason = isSuccess ? null : (failureReason ?? "Demo payment failed by simulated request."),
            BankCode = "DEMOBANK",
            PayDate = DateTimeOffset.UtcNow,
            RawParameters = parameters
        };

        return Task.FromResult(result);
    }
}
