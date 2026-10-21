using System.Text.Json.Serialization;

namespace SelfStorageManagementSystem.WpfClient.Models;

public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("errors")]
    public object? Errors { get; set; }
}

public class PagedResult<T>
{
    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = new();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("hasPreviousPage")]
    public bool HasPreviousPage { get; set; }

    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage { get; set; }
}

public class LoginModel
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }
}

public class CurrentUserResponse
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    public string FullName => DisplayName;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("userType")]
    public string? UserType { get; set; }

    [JsonPropertyName("employeeCode")]
    public string? EmployeeCode { get; set; }

    [JsonPropertyName("activeAssignments")]
    public List<FacilityAssignmentModel> ActiveAssignments { get; set; } = new();
}

public class CustomerRegisterModel
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? IdentityNumber { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
}

public class CustomerRegisterResponse
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

public class UserAccountModel
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("lastLoginAt")]
    public DateTimeOffset? LastLoginAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("identityNumber")]
    public string? IdentityNumber { get; set; }

    [JsonPropertyName("employeeCode")]
    public string? EmployeeCode { get; set; }

    [JsonPropertyName("employmentStatus")]
    public string? EmploymentStatus { get; set; }

    [JsonPropertyName("hireDate")]
    public DateOnly? HireDate { get; set; }

    [JsonPropertyName("facilityAssignments")]
    public List<FacilityAssignmentModel> FacilityAssignments { get; set; } = new();

    public string RolesDisplay => Roles.Count > 0 ? string.Join(", ", Roles) : "None";
    public string DisplayName => FullName ?? Email;
}

public class FacilityAssignmentModel
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("employeeId")]
    public long EmployeeId { get; set; }

    [JsonPropertyName("facilityId")]
    public long FacilityId { get; set; }

    [JsonPropertyName("facilityCode")]
    public string FacilityCode { get; set; } = string.Empty;

    [JsonPropertyName("facilityName")]
    public string FacilityName { get; set; } = string.Empty;

    [JsonPropertyName("assignmentRole")]
    public string AssignmentRole { get; set; } = string.Empty;

    [JsonPropertyName("startsAt")]
    public DateTimeOffset StartsAt { get; set; }

    [JsonPropertyName("endsAt")]
    public DateTimeOffset? EndsAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    public string DateRangeDisplay => EndsAt.HasValue
        ? $"{StartsAt:yyyy-MM-dd} to {EndsAt.Value:yyyy-MM-dd}"
        : $"{StartsAt:yyyy-MM-dd} (Present)";
}

public class FacilityLookupModel
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("addressLine")]
    public string AddressLine { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    public string DisplayText => $"{Code} - {Name} ({City})";
}

public class CreateStaffModel
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public DateOnly HireDate { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public long? InitialFacilityId { get; set; }
}
