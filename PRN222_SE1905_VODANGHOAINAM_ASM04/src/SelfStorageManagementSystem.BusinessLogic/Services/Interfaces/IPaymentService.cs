using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IPaymentService
{
    Task<CheckoutQuoteResponse> GetCheckoutQuoteAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long reservationId,
        string? promotionCode,
        CancellationToken cancellationToken = default);

    Task<CheckoutResponse> CheckoutAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        CheckoutRequest request,
        string? idempotencyKeyHeader,
        string? clientIp,
        CancellationToken cancellationToken = default);

    Task<PaymentGatewayCallbackResult> ProcessVnpayIpnAsync(
        IDictionary<string, string> queryParams,
        CancellationToken cancellationToken = default);

    Task<PaymentDetailResponse> ProcessDemoCompleteAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long paymentId,
        DemoPaymentCompleteRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentDetailResponse> GetPaymentByIdAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long paymentId,
        CancellationToken cancellationToken = default);

    Task<List<PaymentDetailResponse>> GetPaymentsRequiringReconciliationAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<List<RefundDetailResponse>> GetRefundsAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        string? status,
        CancellationToken cancellationToken = default);

    Task<RefundDetailResponse> ReviewRefundAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long refundId,
        ReviewRefundRequest request,
        CancellationToken cancellationToken = default);
}
