namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;

public class CheckoutResponse
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
