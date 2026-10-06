using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth;

public class CurrentUserResponse
{
    public long UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public string? UserType { get; set; } // "customer", "employee", "admin"
    public string? EmployeeCode { get; set; }
    public List<FacilityAssignmentDto> ActiveAssignments { get; set; } = new();
}
