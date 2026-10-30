namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Payments;

public class CheckoutQuoteResponse
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
    public decimal TaxAmount { get; set; } = 0m;
    public decimal QuotedTotal { get; set; }
    public DateTimeOffset HoldUntil { get; set; }
    public bool IsHoldActive { get; set; }

    // Voucher details if applied
    public string? PromotionCode { get; set; }
    public string? PromotionName { get; set; }
    public string? DiscountType { get; set; }
    public decimal? DiscountValue { get; set; }

    // Active policy terms version
    public long PolicyVersionId { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicyContentJson { get; set; } = string.Empty;

    // Academic project demo notice
    public string DemoNotice { get; set; } =
        "Lưu ý: tax_amount = 0. Hóa đơn/Biên nhận hiển thị là tài liệu nội bộ phục vụ demo đồ án học phần PRN222, không phải hóa đơn điện tử GTGT hợp pháp.";
}
