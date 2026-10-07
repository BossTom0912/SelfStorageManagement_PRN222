using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;

public class CancelReservationRequest
{
    [MaxLength(255, ErrorMessage = "Lý do hủy không được vượt quá 255 ký tự.")]
    public string? Reason { get; set; }
}
