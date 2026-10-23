using SelfStorageManagementSystem.BusinessLogic.Common;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;

public class GetMyReservationsRequest : PagedRequest
{
    public string? Status { get; set; }
}
