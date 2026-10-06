using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin;

public class UpdateUserStatusRequest
{
    [Required(ErrorMessage = "Status is required.")]
    public string Status { get; set; } = string.Empty; // 'active', 'locked', 'disabled'
}
