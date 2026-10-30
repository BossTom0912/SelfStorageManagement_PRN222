using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Catalog;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Catalog;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.Catalog;

public class IndexModel : PageModel
{
    private readonly IFacilityCatalogService _catalogService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IFacilityCatalogService catalogService,
        ILogger<IndexModel> logger)
    {
        _catalogService = catalogService;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? City { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? District { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 9;

    public PagedResult<FacilityCatalogDto> Facilities { get; set; } = new(new List<FacilityCatalogDto>(), 0, 1, 9);

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (PageNumber < 1) PageNumber = 1;
        if (PageSize < 1) PageSize = 9;

        try
        {
            var request = new GetFacilitiesCatalogRequest
            {
                City = string.IsNullOrWhiteSpace(City) ? null : City.Trim(),
                District = string.IsNullOrWhiteSpace(District) ? null : District.Trim(),
                SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm.Trim(),
                PageNumber = PageNumber,
                PageSize = PageSize
            };

            Facilities = await _catalogService.GetFacilitiesAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi truy vấn danh sách cơ sở lưu trữ.");
            ErrorMessage = "Không thể tải danh sách cơ sở lưu trữ vào lúc này. Vui lòng thử lại sau.";
            Facilities = new PagedResult<FacilityCatalogDto>(new List<FacilityCatalogDto>(), 0, PageNumber, PageSize);
        }
    }

    /// <summary>
    /// Tạo URL phân trang giữ nguyên toàn bộ các tham số lọc tìm kiếm trong Query String
    /// </summary>
    public string GetPaginationUrl(int targetPage)
    {
        var queryParams = new List<string>
        {
            $"PageNumber={targetPage}",
            $"PageSize={PageSize}"
        };

        if (!string.IsNullOrWhiteSpace(City))
        {
            queryParams.Add($"City={Uri.EscapeDataString(City.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(District))
        {
            queryParams.Add($"District={Uri.EscapeDataString(District.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            queryParams.Add($"SearchTerm={Uri.EscapeDataString(SearchTerm.Trim())}");
        }

        return $"/Catalog?{string.Join("&", queryParams)}";
    }
}
