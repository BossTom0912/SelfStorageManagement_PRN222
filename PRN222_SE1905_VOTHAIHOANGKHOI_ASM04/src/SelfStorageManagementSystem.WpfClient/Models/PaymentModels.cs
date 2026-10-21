namespace SelfStorageManagementSystem.WpfClient.Models;

public class CheckoutQuoteClientModel
{
    public long ReservationId { get; set; }
    public string ReservationCode { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public long FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public long UnitTypeId { get; set; }
    public string UnitTypeName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int RentalMonths { get; set; }
    public decimal MonthlyRateSnapshot { get; set; }
    public decimal DepositSnapshot { get; set; }
    public decimal BookingFeeSnapshot { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal SubtotalAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal QuotedTotal { get; set; }
    public DateTimeOffset HoldUntil { get; set; }
    public bool IsHoldActive { get; set; }

    public string? PromotionCode { get; set; }
    public string? PromotionName { get; set; }
    public string? DiscountType { get; set; }
    public decimal? DiscountValue { get; set; }

    public long PolicyVersionId { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicyContentJson { get; set; } = string.Empty;
    public string DemoNotice { get; set; } = string.Empty;
}

public class CheckoutClientRequest
{
    public long ReservationId { get; set; }
    public string? PromotionCode { get; set; }
    public long AcceptedPolicyVersionId { get; set; }
    public string PaymentMethod { get; set; } = "demo";
}

public class CheckoutClientResponse
{
    public long PaymentId { get; set; }
    public long InvoiceId { get; set; }
    public long ReservationId { get; set; }
    public string ReservationCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Status { get; set; } = "pending";
    public string CheckoutUrl { get; set; } = string.Empty;
    public DateTimeOffset HoldUntil { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
}

public class PaymentDetailClientModel
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

public class InvoiceDetailClientModel
{
    public long InvoiceId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public long? ReservationId { get; set; }
    public string? ReservationCode { get; set; }
    public long? AgreementId { get; set; }
    public string? AgreementNo { get; set; }
    public string? BillingPeriod { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Currency { get; set; } = "VND";
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<InvoiceLineClientModel> Lines { get; set; } = new();
    public string Notice { get; set; } = string.Empty;
}

public class InvoiceLineClientModel
{
    public long Id { get; set; }
    public string LineType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }
}

public class RentalAgreementDetailClientModel
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
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? SignedAt { get; set; }
    public bool IsStorageUnitAllocated { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Notice { get; set; } = string.Empty;
}

public class DemoPaymentCompleteClientRequest
{
    public bool IsSuccess { get; set; } = true;
    public string? FailureReason { get; set; }
}
