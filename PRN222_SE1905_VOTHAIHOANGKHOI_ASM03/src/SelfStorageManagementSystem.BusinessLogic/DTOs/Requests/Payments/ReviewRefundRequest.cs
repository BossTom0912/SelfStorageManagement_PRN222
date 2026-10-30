namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;

public class ReviewRefundRequest
{
    public string Decision { get; set; } = string.Empty; // "approved" or "rejected"
    public string? Reason { get; set; }
}
