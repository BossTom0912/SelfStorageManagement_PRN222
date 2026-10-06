using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth;

public class CustomerRegisterRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    [MaxLength(255, ErrorMessage = "Email cannot exceed 255 characters.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters long.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full name is required.")]
    [MaxLength(255, ErrorMessage = "Full name cannot exceed 255 characters.")]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(255, ErrorMessage = "Phone number cannot exceed 255 characters.")]
    public string? PhoneNumber { get; set; }

    [MaxLength(255, ErrorMessage = "Identity number cannot exceed 255 characters.")]
    public string? IdentityNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    [MaxLength(255, ErrorMessage = "Address cannot exceed 255 characters.")]
    public string? Address { get; set; }

    [MaxLength(255, ErrorMessage = "Emergency contact name cannot exceed 255 characters.")]
    public string? EmergencyContactName { get; set; }

    [MaxLength(255, ErrorMessage = "Emergency contact phone cannot exceed 255 characters.")]
    public string? EmergencyContactPhone { get; set; }
}
