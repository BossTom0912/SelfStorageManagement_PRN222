using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

public class CheckoutTransactionParams
{
    public long ReservationId { get; set; }
    public long CustomerId { get; set; }
    public long PolicyVersionId { get; set; }
    public string PolicyVersionNumber { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentProvider { get; set; } = string.Empty;
    public decimal ComputedDeposit { get; set; }
    public decimal ComputedRent { get; set; }
    public decimal ComputedBookingFee { get; set; }
    public decimal ComputedDiscount { get; set; }
    public decimal ComputedTotal { get; set; }
    public promotion? AppliedPromotion { get; set; }
    public DateTimeOffset NowUtc { get; set; }
    public string? ClientIp { get; set; }
    public string EvidenceMetadataJson { get; set; } = "{}";
}

public class CheckoutTransactionResult
{
    public bool IsIdempotentReplay { get; set; }
    public payment Payment { get; set; } = null!;
    public invoice Invoice { get; set; } = null!;
    public reservation Reservation { get; set; } = null!;
}

public class FinalizePaymentParams
{
    public long PaymentId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string ExternalEventId { get; set; } = string.Empty;
    public string RawPayloadJson { get; set; } = "{}";
    public DateTimeOffset PaidAt { get; set; }
}

public class FinalizePaymentResult
{
    public bool IsAlreadyProcessed { get; set; }
    public bool ReconciliationRequired { get; set; }
    public string? ReconciliationReason { get; set; }
    public payment Payment { get; set; } = null!;
    public invoice Invoice { get; set; } = null!;
    public rental_agreement? Agreement { get; set; }
    public reservation Reservation { get; set; } = null!;
}

public class FinalizeFailedPaymentParams
{
    public long PaymentId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalEventId { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public string RawPayloadJson { get; set; } = "{}";
    public DateTimeOffset FailedAt { get; set; }
}

public interface IPaymentRepository
{
    Task<reservation?> GetReservationForQuoteAsync(
        long reservationId,
        CancellationToken cancellationToken = default);

    Task<policy_version?> GetActiveRentalTermsPolicyVersionAsync(
        DateOnly forDate,
        CancellationToken cancellationToken = default);

    Task<promotion?> GetPromotionByCodeAsync(
        string code,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<int> GetPromotionActiveRedemptionsCountAsync(
        long promotionId,
        CancellationToken cancellationToken = default);

    Task<int> GetCustomerActiveRedemptionsCountAsync(
        long promotionId,
        long customerId,
        CancellationToken cancellationToken = default);

    Task<bool> IsNewCustomerAsync(
        long customerId,
        CancellationToken cancellationToken = default);

    Task<payment?> GetPaymentByIdAsync(
        long paymentId,
        CancellationToken cancellationToken = default);

    Task<payment?> GetPaymentByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<payment?> GetActivePendingPaymentByReservationIdAsync(
        long reservationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<invoice?> GetInvoiceByIdAsync(
        long invoiceId,
        CancellationToken cancellationToken = default);

    Task<rental_agreement?> GetAgreementByIdAsync(
        long agreementId,
        CancellationToken cancellationToken = default);

    Task<rental_agreement?> GetAgreementByReservationIdAsync(
        long reservationId,
        CancellationToken cancellationToken = default);

    Task<integration_event?> GetIntegrationEventAsync(
        string source,
        string externalEventId,
        CancellationToken cancellationToken = default);

    Task<List<payment>> GetPaymentsRequiringReconciliationAsync(
        List<long>? accessibleFacilityIds,
        CancellationToken cancellationToken = default);

    Task<CheckoutTransactionResult> ExecuteCheckoutTransactionAsync(
        CheckoutTransactionParams parameters,
        CancellationToken cancellationToken = default);

    Task<FinalizePaymentResult> ExecuteFinalizePaymentTransactionAsync(
        FinalizePaymentParams parameters,
        CancellationToken cancellationToken = default);

    Task<bool> ExecuteFinalizeFailedPaymentTransactionAsync(
        FinalizeFailedPaymentParams parameters,
        CancellationToken cancellationToken = default);

    Task UpdatePaymentStatusToPendingAsync(
        long paymentId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<List<refund>> GetRefundsAsync(
        List<long>? accessibleFacilityIds,
        string? status,
        CancellationToken cancellationToken = default);

    Task<refund?> GetRefundByIdAsync(
        long refundId,
        CancellationToken cancellationToken = default);

    Task<refund> ReviewRefundAsync(
        long refundId,
        long employeeUserId,
        string decision,
        string? reason,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default);
}
