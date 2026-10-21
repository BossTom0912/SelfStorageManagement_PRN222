using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin;

public class CreateStaffAccountRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    [MaxLength(255, ErrorMessage = "Email cannot exceed 255 characters.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters long.")]
    public string Password { get; set; } = string.Empty;

    [MaxLength(255, ErrorMessage = "Phone number cannot exceed 255 characters.")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Full name is required.")]
    [MaxLength(255, ErrorMessage = "Full name cannot exceed 255 characters.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Employee code is required.")]
    [MaxLength(255, ErrorMessage = "Employee code cannot exceed 255 characters.")]
    public string EmployeeCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Hire date is required.")]
    public DateOnly HireDate { get; set; }

    [Required(ErrorMessage = "Role code is required.")]
    public string RoleCode { get; set; } = string.Empty; // facility_staff, facility_manager, or business_operations_manager

    public long? InitialFacilityId { get; set; }
}
