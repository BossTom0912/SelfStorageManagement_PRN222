using SelfStorageManagementSystem.BusinessLogic.Common;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Catalog;

public class GetFacilitiesCatalogRequest : PagedRequest
{
    public string? City { get; set; }
    public string? District { get; set; }
    public string? SearchTerm { get; set; }
}
