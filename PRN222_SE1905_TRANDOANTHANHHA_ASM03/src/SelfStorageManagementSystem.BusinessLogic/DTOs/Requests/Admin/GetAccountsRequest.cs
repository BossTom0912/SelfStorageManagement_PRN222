using SelfStorageManagementSystem.BusinessLogic.Common;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin;

public class GetAccountsRequest : PagedRequest
{
    public string? SearchTerm { get; set; }
    public string? Status { get; set; }
    public string? RoleCode { get; set; }
}
