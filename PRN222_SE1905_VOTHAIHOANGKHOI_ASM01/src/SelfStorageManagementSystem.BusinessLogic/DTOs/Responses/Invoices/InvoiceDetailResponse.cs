namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Invoices;

public class InvoiceDetailResponse
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
    public decimal TaxAmount { get; set; } = 0m;
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<InvoiceLineResponse> Lines { get; set; } = new();
    public string Notice { get; set; } =
        "Biên nhận / Hóa đơn nội bộ mô phỏng đồ án PRN222 (tax_amount = 0). Không phải hóa đơn điện tử GTGT hợp pháp.";
}

public class InvoiceLineResponse
{
    public long Id { get; set; }
    public string LineType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }
}
