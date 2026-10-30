namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;

public class UserAccountDto
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<string> Roles { get; set; } = new();

    public string? FullName { get; set; }
    public string? IdentityNumber { get; set; }
    public string? EmployeeCode { get; set; }
    public string? EmploymentStatus { get; set; }
    public DateOnly? HireDate { get; set; }

    public List<FacilityAssignmentDto> FacilityAssignments { get; set; } = new();
}
