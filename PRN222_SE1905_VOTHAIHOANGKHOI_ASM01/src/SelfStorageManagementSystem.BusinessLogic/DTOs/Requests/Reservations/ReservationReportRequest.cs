namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;

public class ReservationReportRequest
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public long? FacilityId { get; set; }
    public string? Status { get; set; }
}
