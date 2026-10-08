using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class VnpayGatewayTests
{
    private readonly VnpayGateway _gateway;
    private const string TmnCode = "DEMOTMN01";
    private const string HashSecret = "SECRETTESTKEY1234567890ABCDEF12";

    public VnpayGatewayTests()
    {
        var configInMemory = new Dictionary<string, string?>
        {
            ["VnPay:TmnCode"] = TmnCode,
            ["VnPay:HashSecret"] = HashSecret,
            ["VnPay:BaseUrl"] = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            ["VnPay:ReturnUrl"] = "http://localhost:5000/api/payments/vnpay/return"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configInMemory).Build();
        _gateway = new VnpayGateway(config, NullLogger<VnpayGateway>.Instance);
    }

    [Fact]
    public async Task CreateCheckoutUrlAsync_GeneratesValidVnpayUrlWithIntegerAmountTimes100()
    {
        var order = new PaymentGatewayOrder
        {
            PaymentId = 12345,
            InvoiceId = 888,
            ReservationId = 999,
            ReservationCode = "RES-001",
            Amount = 1_500_000m, // VND amounts must be integer VND
            Description = "Thanh toan don giu cho RES-001",
            ClientIp = "127.0.0.1",
            HoldUntil = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var url = await _gateway.CreateCheckoutUrlAsync(order);

        Assert.NotNull(url);
        Assert.StartsWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?", url);
        Assert.Contains($"vnp_TmnCode={TmnCode}", url);
        Assert.Contains("vnp_Amount=150000000", url); // 1,500,000 * 100
        Assert.Contains("vnp_CurrCode=VND", url);
        Assert.Contains("vnp_Command=pay", url);
        Assert.Contains("vnp_SecureHash=", url);
        Assert.Contains("vnp_ExpireDate=", url);
    }

    [Fact]
    public async Task CreateCheckoutUrlAsync_FractionalAmount_ThrowsArgumentException()
    {
        var order = new PaymentGatewayOrder
        {
            PaymentId = 12345,
            InvoiceId = 888,
            ReservationId = 999,
            ReservationCode = "RES-001",
            Amount = 1_500_000.45m,
            Description = "Thanh toan don giu cho RES-001",
            ClientIp = "127.0.0.1",
            HoldUntil = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _gateway.CreateCheckoutUrlAsync(order));
    }

    [Fact]
    public async Task CreateCheckoutUrlAsync_MissingCredentials_ThrowsInvalidOperationException()
    {
        var emptyConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var gateway = new VnpayGateway(emptyConfig, NullLogger<VnpayGateway>.Instance);

        var order = new PaymentGatewayOrder
        {
            PaymentId = 1,
            Amount = 100_000m
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.CreateCheckoutUrlAsync(order));
    }

    [Fact]
    public async Task VerifyCallbackAsync_ValidHmacChecksum_ReturnsSuccess()
    {
        var txnRef = "PAY-999-ABCD";
        var amountRaw = 200000000L; // 2,000,000 VND * 100
        var nowString = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        var rawParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = amountRaw.ToString(),
            ["vnp_BankCode"] = "NCB",
            ["vnp_CardType"] = "ATM",
            ["vnp_OrderInfo"] = "Thanh toan test",
            ["vnp_PayDate"] = nowString,
            ["vnp_ResponseCode"] = "00",
            ["vnp_TmnCode"] = TmnCode,
            ["vnp_TransactionNo"] = "14567890",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = txnRef
        };

        var hashDataBuilder = new StringBuilder();
        foreach (var (key, value) in rawParams)
        {
            if (hashDataBuilder.Length > 0) hashDataBuilder.Append('&');
            hashDataBuilder.Append(System.Net.WebUtility.UrlEncode(key)).Append('=').Append(System.Net.WebUtility.UrlEncode(value));
        }
        var validHash = VnpayGateway.ComputeHmacSha512(HashSecret, hashDataBuilder.ToString());

        var callbackParams = new Dictionary<string, string>(rawParams)
        {
            ["vnp_SecureHash"] = validHash
        };

        var result = await _gateway.VerifyCallbackAsync(callbackParams);

        Assert.True(result.IsValidSignature);
        Assert.True(result.IsSuccess);
        Assert.Equal(txnRef, result.TxnRef);
        Assert.Equal(2_000_000m, result.Amount);
        Assert.Equal("14567890", result.ProviderTransactionId);
        Assert.Equal("00", result.ResponseCode);
    }

    [Fact]
    public async Task VerifyCallbackAsync_TamperedAmount_FailsSignature()
    {
        var callbackParams = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = TmnCode,
            ["vnp_Amount"] = "10000000",
            ["vnp_ResponseCode"] = "00",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = "PAY-111",
            ["vnp_SecureHash"] = "tampered_fake_signature_hash"
        };

        var result = await _gateway.VerifyCallbackAsync(callbackParams);

        Assert.False(result.IsValidSignature);
        Assert.False(result.IsSuccess);
    }
}
