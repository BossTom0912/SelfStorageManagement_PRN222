namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;

public class FacilityAssignmentDto
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public long FacilityId { get; set; }
    public string FacilityCode { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public string AssignmentRole { get; set; } = string.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
