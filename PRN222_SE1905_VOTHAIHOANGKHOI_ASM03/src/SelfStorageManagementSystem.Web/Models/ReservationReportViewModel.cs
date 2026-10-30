using Microsoft.AspNetCore.Mvc.Rendering;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;

namespace SelfStorageManagementSystem.Web.Models;

public class ReservationReportViewModel
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public long? FacilityId { get; set; }
    public string? Status { get; set; }
    public ReservationReportResponse Report { get; set; } = new();
    public SelectList? FacilityOptions { get; set; }
    public string? ErrorMessage { get; set; }
}
