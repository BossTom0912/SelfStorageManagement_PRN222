using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

/// <summary>
/// Adapter for VNPAY Sandbox payment gateway (Version 2.1.0).
/// Implements HMAC-SHA512 signing, URL generation, IPN verification,
/// and prepared helpers for QueryDR and Refund.
/// </summary>
public class VnpayGateway : IPaymentGateway
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<VnpayGateway> _logger;

    public string ProviderName => "vnpay";

    public VnpayGateway(IConfiguration configuration, ILogger<VnpayGateway> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<string> CreateCheckoutUrlAsync(
        PaymentGatewayOrder order,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var tmnCode = _configuration["Vnpay:TmnCode"] ?? "DEMOTMN1";
        var hashSecret = _configuration["Vnpay:HashSecret"] ?? "SECRETKEYVNPAYTEST20261007XYZABC";
        var baseUrl = _configuration["Vnpay:BaseUrl"] ?? "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
        var returnUrl = _configuration["Vnpay:ReturnUrl"] ?? "https://localhost:7031/api/payments/vnpay/return";

        var gmt7 = ResolveGmt7TimeZone();
        var nowGmt7 = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, gmt7);
        var holdUntilGmt7 = TimeZoneInfo.ConvertTimeFromUtc(order.HoldUntil.UtcDateTime, gmt7);

        // VNPAY Amount is integer VND multiplied by 100
        var amountVnd = (long)Math.Round(order.Amount, MidpointRounding.AwayFromZero);
        var vnpAmount = (amountVnd * 100).ToString();

        var paramsMap = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            { "vnp_Version", "2.1.0" },
            { "vnp_Command", "pay" },
            { "vnp_TmnCode", tmnCode },
            { "vnp_Amount", vnpAmount },
            { "vnp_CreateDate", nowGmt7.ToString("yyyyMMddHHmmss") },
            { "vnp_CurrCode", "VND" },
            { "vnp_IpAddr", string.IsNullOrWhiteSpace(order.ClientIp) ? "127.0.0.1" : order.ClientIp },
            { "vnp_Locale", "vn" },
            { "vnp_OrderInfo", $"Thanh toan giu cho {order.ReservationCode}" },
            { "vnp_OrderType", "other" },
            { "vnp_ReturnUrl", returnUrl },
            { "vnp_TxnRef", order.PaymentId.ToString() },
            { "vnp_ExpireDate", holdUntilGmt7.ToString("yyyyMMddHHmmss") }
        };

        var queryBuilder = new StringBuilder();
        var hashDataBuilder = new StringBuilder();

        foreach (var (key, value) in paramsMap)
        {
            if (string.IsNullOrEmpty(value)) continue;

            if (queryBuilder.Length > 0)
            {
                queryBuilder.Append('&');
                hashDataBuilder.Append('&');
            }

            var encodedKey = WebUtility.UrlEncode(key);
            var encodedVal = WebUtility.UrlEncode(value);

            queryBuilder.Append(encodedKey).Append('=').Append(encodedVal);
            hashDataBuilder.Append(encodedKey).Append('=').Append(encodedVal);
        }

        var secureHash = ComputeHmacSha512(hashSecret, hashDataBuilder.ToString());
        queryBuilder.Append("&vnp_SecureHash=").Append(secureHash);

        var paymentUrl = $"{baseUrl}?{queryBuilder}";
        _logger.LogInformation("Created VNPAY checkout URL for Payment attempt {PaymentId}, TxnRef: {TxnRef}", order.PaymentId, order.PaymentId);

        return Task.FromResult(paymentUrl);
    }

    public Task<PaymentGatewayCallbackResult> VerifyCallbackAsync(
        IDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var hashSecret = _configuration["Vnpay:HashSecret"] ?? "SECRETKEYVNPAYTEST20261007XYZABC";

        var inputHash = parameters.TryGetValue("vnp_SecureHash", out var sHash) ? sHash : string.Empty;

        // Collect all vnp_ fields except secure hash fields
        var filteredParams = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in parameters)
        {
            if (!string.IsNullOrEmpty(k) &&
                k.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase) &&
                !k.Equals("vnp_SecureHash", StringComparison.OrdinalIgnoreCase) &&
                !k.Equals("vnp_SecureHashType", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(v))
            {
                filteredParams.Add(k, v);
            }
        }

        var hashDataBuilder = new StringBuilder();
        foreach (var (key, value) in filteredParams)
        {
            if (hashDataBuilder.Length > 0)
            {
                hashDataBuilder.Append('&');
            }
            hashDataBuilder.Append(WebUtility.UrlEncode(key)).Append('=').Append(WebUtility.UrlEncode(value));
        }

        var expectedHash = ComputeHmacSha512(hashSecret, hashDataBuilder.ToString());
        var isValidSignature = !string.IsNullOrEmpty(inputHash) &&
                               string.Equals(inputHash, expectedHash, StringComparison.OrdinalIgnoreCase);

        var txnRef = parameters.TryGetValue("vnp_TxnRef", out var tRef) ? tRef : null;
        var transactionNo = parameters.TryGetValue("vnp_TransactionNo", out var tNo) ? tNo : null;
        var responseCode = parameters.TryGetValue("vnp_ResponseCode", out var rCode) ? rCode : null;
        var transactionStatus = parameters.TryGetValue("vnp_TransactionStatus", out var tStat) ? tStat : null;
        var bankCode = parameters.TryGetValue("vnp_BankCode", out var bCode) ? bCode : null;

        decimal amount = 0m;
        if (parameters.TryGetValue("vnp_Amount", out var amtStr) && long.TryParse(amtStr, out var rawAmt))
        {
            amount = rawAmt / 100m; // Reverse provider x100 multiplication
        }

        DateTimeOffset? payDate = null;
        if (parameters.TryGetValue("vnp_PayDate", out var payDateStr) &&
            DateTime.TryParseExact(payDateStr, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            var gmt7 = ResolveGmt7TimeZone();
            payDate = new DateTimeOffset(parsedDate, gmt7.GetUtcOffset(parsedDate));
        }

        var isSuccess = isValidSignature && responseCode == "00" && transactionStatus == "00";
        string? failureReason = null;
        if (!isValidSignature)
        {
            failureReason = "Invalid VNPAY HMAC-SHA512 signature.";
        }
        else if (!isSuccess)
        {
            failureReason = $"VNPAY payment declined with ResponseCode '{responseCode}' and TransactionStatus '{transactionStatus}'.";
        }

        var result = new PaymentGatewayCallbackResult
        {
            IsValidSignature = isValidSignature,
            IsSuccess = isSuccess,
            TxnRef = txnRef,
            ProviderTransactionId = transactionNo,
            Amount = amount,
            ResponseCode = responseCode,
            TransactionStatus = transactionStatus,
            FailureReason = failureReason,
            BankCode = bankCode,
            PayDate = payDate,
            RawParameters = parameters
        };

        return Task.FromResult(result);
    }

    public static string ComputeHmacSha512(string key, string inputData)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var inputBytes = Encoding.UTF8.GetBytes(inputData);

        using var hmac = new HMACSHA512(keyBytes);
        var hashBytes = hmac.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes).ToUpperInvariant();
    }

    private static TimeZoneInfo ResolveGmt7TimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch
        {
            return TimeZoneInfo.CreateCustomTimeZone("GMT+7", TimeSpan.FromHours(7), "GMT+7", "GMT+7");
        }
    }
}
