using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin;

public class ManageUserRolesRequest
{
    [Required(ErrorMessage = "At least one role code must be specified.")]
    public List<string> RoleCodes { get; set; } = new();
}
