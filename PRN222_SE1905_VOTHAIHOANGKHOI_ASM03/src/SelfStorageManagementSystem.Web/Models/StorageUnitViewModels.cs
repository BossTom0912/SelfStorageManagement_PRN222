using System;
using System.Collections.Generic;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;

namespace SelfStorageManagementSystem.Web.Models;

public class StorageUnitsIndexViewModel
{
    public StorageUnitFilterRequest Filter { get; set; } = new();
    public PagedResult<StorageUnitAdminDto> StorageUnits { get; set; } = new(Array.Empty<StorageUnitAdminDto>(), 0, 1, 10);
    public StorageUnitLookupsDto Lookups { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public bool CanManageUnits { get; set; }

    public string GetPaginationUrl(int targetPage)
    {
        var parameters = new List<string>
        {
            $"pageNumber={targetPage}",
            $"pageSize={Filter.PageSize}"
        };

        if (Filter.FacilityId.HasValue) parameters.Add($"facilityId={Filter.FacilityId.Value}");
        if (Filter.UnitTypeId.HasValue) parameters.Add($"unitTypeId={Filter.UnitTypeId.Value}");
        if (Filter.AreaId.HasValue) parameters.Add($"areaId={Filter.AreaId.Value}");
        if (!string.IsNullOrWhiteSpace(Filter.PhysicalStatus)) parameters.Add($"physicalStatus={Uri.EscapeDataString(Filter.PhysicalStatus)}");
        if (Filter.IsListed.HasValue) parameters.Add($"isListed={Filter.IsListed.Value.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(Filter.SearchTerm)) parameters.Add($"searchTerm={Uri.EscapeDataString(Filter.SearchTerm)}");
        if (!string.IsNullOrWhiteSpace(Filter.SortBy)) parameters.Add($"sortBy={Uri.EscapeDataString(Filter.SortBy)}");
        if (Filter.SortDescending) parameters.Add("sortDescending=true");

        return $"/StorageUnits?{string.Join("&", parameters)}";
    }
}

public class StorageUnitCreateViewModel
{
    public CreateStorageUnitRequest Input { get; set; } = new();
    public StorageUnitLookupsDto Lookups { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public bool CanManage { get; set; } = true;
}

public class StorageUnitEditViewModel
{
    public long Id { get; set; }
    public UpdateStorageUnitRequest Input { get; set; } = new();
    public string FacilityName { get; set; } = string.Empty;
    public string FacilityCode { get; set; } = string.Empty;
    public long FacilityId { get; set; }
    public string UnitTypeName { get; set; } = string.Empty;
    public string UnitTypeCode { get; set; } = string.Empty;
    public long UnitTypeId { get; set; }
    public string PhysicalStatus { get; set; } = string.Empty;
    public bool IsListed { get; set; }
    public StorageUnitLookupsDto Lookups { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public bool IsConcurrencyConflict { get; set; }
    public DateTimeOffset? LatestDbUpdatedAt { get; set; }
    public bool CanManage { get; set; } = true;
}

public class StorageUnitDetailsViewModel
{
    public StorageUnitAdminDetailDto StorageUnit { get; set; } = default!;
    public bool CanManage { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

public class StorageUnitDeleteViewModel
{
    public long Id { get; set; }
    public DateTimeOffset ExpectedUpdatedAt { get; set; }
    public StorageUnitAdminDetailDto StorageUnit { get; set; } = default!;
    public bool CanDelete { get; set; } = true;
    public string? BlockReason { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CanManage { get; set; } = true;
}
