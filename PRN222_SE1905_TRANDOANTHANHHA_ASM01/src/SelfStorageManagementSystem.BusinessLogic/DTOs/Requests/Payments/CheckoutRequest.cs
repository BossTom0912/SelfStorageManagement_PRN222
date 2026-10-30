using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Payments;

public class CheckoutRequest
{
    [Required]
    public long ReservationId { get; set; }

    public string? PromotionCode { get; set; }

    [Required]
    public long AcceptedPolicyVersionId { get; set; }

    /// <summary>
    /// Gateway method/provider selected: "demo" or "vnpay". Default is "demo".
    /// </summary>
    public string PaymentMethod { get; set; } = "demo";
}
