using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin;

public class CreateFacilityAssignmentRequest
{
    [Required(ErrorMessage = "Facility ID is required.")]
    public long FacilityId { get; set; }

    [Required(ErrorMessage = "Assignment role is required.")]
    public string AssignmentRole { get; set; } = string.Empty; // 'facility_staff' or 'facility_manager'

    [Required(ErrorMessage = "StartsAt is required.")]
    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }
}
