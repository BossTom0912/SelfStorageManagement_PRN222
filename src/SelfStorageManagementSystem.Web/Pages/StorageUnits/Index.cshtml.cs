using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.StorageUnits;

/// <summary>
/// PageModel cho trang danh sách quản trị ô kho (/StorageUnits/Index).
/// Refactor từ khung scaffold dotnet aspnet-codegenerator sang BLL Service IStorageUnitAdminService.
/// Tuyệt đối không truy cập trực tiếp EF DbContext hoặc Repository.
/// </summary>
[Authorize(Policy = "StaffOnly")]
public class IndexModel : PageModel
{
    private readonly IStorageUnitAdminService _storageUnitAdminService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IStorageUnitAdminService storageUnitAdminService,
        ILogger<IndexModel> logger)
    {
        _storageUnitAdminService = storageUnitAdminService ?? throw new ArgumentNullException(nameof(storageUnitAdminService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public long? FacilityId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? UnitTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? AreaId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PhysicalStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool? IsListed { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SortBy { get; set; } = "unit_code";

    [BindProperty(SupportsGet = true)]
    public bool SortDescending { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;

    public PagedResult<StorageUnitAdminDto> StorageUnits { get; set; } =
        new(Array.Empty<StorageUnitAdminDto>(), 0, 1, 10);

    public StorageUnitLookupsDto Lookups { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public bool CanManageUnits { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0)
        {
            return RedirectToPage("/Account/Login");
        }

        CanManageUnits = User.IsInRole(RoleConstants.FacilityManager) ||
                         User.IsInRole(RoleConstants.BusinessOperationsManager) ||
                         User.IsInRole(RoleConstants.SystemAdministrator);

        if (PageNumber < 1) PageNumber = 1;
        if (PageSize < 1) PageSize = 10;

        try
        {
            // Tải lookups đúng phạm vi cơ sở phân công của người dùng hiện hành
            Lookups = await _storageUnitAdminService.GetLookupsAsync(actorUserId, FacilityId, cancellationToken);

            var filter = new StorageUnitFilterRequest
            {
                FacilityId = FacilityId,
                UnitTypeId = UnitTypeId,
                AreaId = AreaId,
                PhysicalStatus = string.IsNullOrWhiteSpace(PhysicalStatus) ? null : PhysicalStatus.Trim(),
                IsListed = IsListed,
                SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm.Trim(),
                SortBy = SortBy,
                SortDescending = SortDescending,
                PageNumber = PageNumber,
                PageSize = PageSize
            };

            StorageUnits = await _storageUnitAdminService.ListAsync(filter, actorUserId, cancellationToken);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Truy cập bị từ chối khi tải danh sách ô kho cho user {UserId}.", actorUserId);
            ErrorMessage = ex.Message;
            StorageUnits = new PagedResult<StorageUnitAdminDto>(Array.Empty<StorageUnitAdminDto>(), 0, PageNumber, PageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi bất ngờ khi tải danh sách ô kho cho user {UserId}.", actorUserId);
            ErrorMessage = "Không thể tải danh sách ô kho vào lúc này. Vui lòng thử lại sau.";
            StorageUnits = new PagedResult<StorageUnitAdminDto>(Array.Empty<StorageUnitAdminDto>(), 0, PageNumber, PageSize);
        }

        return Page();
    }

    public string GetPaginationUrl(int targetPage)
    {
        var parameters = new System.Collections.Generic.List<string>
        {
            $"pageNumber={targetPage}",
            $"pageSize={PageSize}"
        };

        if (FacilityId.HasValue) parameters.Add($"facilityId={FacilityId.Value}");
        if (UnitTypeId.HasValue) parameters.Add($"unitTypeId={UnitTypeId.Value}");
        if (AreaId.HasValue) parameters.Add($"areaId={AreaId.Value}");
        if (!string.IsNullOrWhiteSpace(PhysicalStatus)) parameters.Add($"physicalStatus={Uri.EscapeDataString(PhysicalStatus)}");
        if (IsListed.HasValue) parameters.Add($"isListed={IsListed.Value.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(SearchTerm)) parameters.Add($"searchTerm={Uri.EscapeDataString(SearchTerm)}");
        if (!string.IsNullOrWhiteSpace(SortBy)) parameters.Add($"sortBy={Uri.EscapeDataString(SortBy)}");
        if (SortDescending) parameters.Add("sortDescending=true");

        return $"/StorageUnits?{string.Join("&", parameters)}";
    }

    private long GetActorUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdStr, out var id) ? id : 0;
    }
}
