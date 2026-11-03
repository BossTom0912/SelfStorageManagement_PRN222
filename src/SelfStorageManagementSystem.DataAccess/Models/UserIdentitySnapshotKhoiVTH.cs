namespace SelfStorageManagementSystem.DataAccess.Models;

public class StaffAssignmentSnapshotKhoiVTH
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

public class UserIdentitySnapshotKhoiVTH
{
    public long UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public string? CustomerFullName { get; set; }
    public string? IdentityNumber { get; set; }

    public string? EmployeeFullName { get; set; }
    public string? EmployeeCode { get; set; }
    public string? EmploymentStatus { get; set; }

    public List<string> Roles { get; set; } = new();
    public List<StaffAssignmentSnapshotKhoiVTH> ActiveAssignments { get; set; } = new();

    public string DisplayName => CustomerFullName
                                  ?? EmployeeFullName
                                  ?? Email;

    public bool HasEmployeeProfile => EmploymentStatus != null || !string.IsNullOrWhiteSpace(EmployeeCode) || !string.IsNullOrWhiteSpace(EmployeeFullName);

    public string? UserType
    {
        get
        {
            if (Roles.Contains("system_administrator")) return "admin";
            if (!string.IsNullOrWhiteSpace(EmployeeCode) || !string.IsNullOrWhiteSpace(EmployeeFullName)) return "employee";
            if (!string.IsNullOrWhiteSpace(CustomerFullName)) return "customer";
            return null;
        }
    }
}
