namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;

public class DemoPaymentCompleteRequest
{
    public bool IsSuccess { get; set; } = true;

    public string? FailureReason { get; set; }

    public string? ProviderTransactionId { get; set; }
}
