using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
/// Tích hợp đầy đủ tính năng Xuất (JSON/CSV) và Nhập an toàn chống Insecure Deserialization (ASM 04).
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

    [TempData]
    public string? SuccessMessage { get; set; }

    [BindProperty]
    public IFormFile? ImportFile { get; set; }

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

    public async Task<IActionResult> OnGetExportJsonAsync(CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0) return RedirectToPage("/Account/Login");

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
            PageNumber = 1,
            PageSize = 5000
        };

        var result = await _storageUnitAdminService.ListAsync(filter, actorUserId, cancellationToken);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result.Items, options);
        var fileName = $"storage_units_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        return File(bytes, "application/json", fileName);
    }

    public async Task<IActionResult> OnGetExportCsvAsync(CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0) return RedirectToPage("/Account/Login");

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
            PageNumber = 1,
            PageSize = 5000
        };

        var result = await _storageUnitAdminService.ListAsync(filter, actorUserId, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("Id,UnitCode,FacilityCode,FacilityName,UnitTypeCode,UnitTypeName,AreaName,FloorLabel,ZoneLabel,PhysicalStatus,IsListed,CreatedAt");

        foreach (var item in result.Items)
        {
            sb.AppendLine($"\"{item.Id}\",\"{EscapeCsv(item.UnitCode)}\",\"{EscapeCsv(item.FacilityCode)}\",\"{EscapeCsv(item.FacilityName)}\",\"{EscapeCsv(item.UnitTypeCode)}\",\"{EscapeCsv(item.UnitTypeName)}\",\"{EscapeCsv(item.AreaName)}\",\"{EscapeCsv(item.FloorLabel)}\",\"{EscapeCsv(item.ZoneLabel)}\",\"{EscapeCsv(item.PhysicalStatus)}\",\"{item.IsListed}\",\"{item.CreatedAt:yyyy-MM-dd HH:mm:ss}\"");
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var fileName = $"storage_units_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    public async Task<IActionResult> OnPostImportJsonAsync(CancellationToken cancellationToken)
    {
        var actorUserId = GetActorUserId();
        if (actorUserId <= 0) return RedirectToPage("/Account/Login");

        if (!User.IsInRole(RoleConstants.FacilityManager) &&
            !User.IsInRole(RoleConstants.BusinessOperationsManager) &&
            !User.IsInRole(RoleConstants.SystemAdministrator))
        {
            return Forbid();
        }

        if (ImportFile == null || ImportFile.Length == 0)
        {
            ErrorMessage = "Vui lòng chọn file JSON để nhập dữ liệu.";
            return await OnGetAsync(cancellationToken);
        }

        // Giới hạn kích thước file upload 2MB chống DoS
        if (ImportFile.Length > 2 * 1024 * 1024)
        {
            ErrorMessage = "Kích thước file nhập vượt quá giới hạn 2MB cho phép.";
            return await OnGetAsync(cancellationToken);
        }

        var extension = Path.GetExtension(ImportFile.FileName);
        if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Chỉ chấp nhận file định dạng .json hợp lệ.";
            return await OnGetAsync(cancellationToken);
        }

        List<StorageUnitImportDto>? importItems;
        try
        {
            using var stream = ImportFile.OpenReadStream();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            importItems = await JsonSerializer.DeserializeAsync<List<StorageUnitImportDto>>(stream, options, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Lỗi phân tích cú pháp JSON khi nhập ô kho");
            ErrorMessage = "Cấu trúc file JSON không hợp lệ hoặc sai định dạng schema.";
            return await OnGetAsync(cancellationToken);
        }

        if (importItems == null || importItems.Count == 0)
        {
            ErrorMessage = "File JSON không chứa bản ghi ô kho nào để nhập.";
            return await OnGetAsync(cancellationToken);
        }

        int successCount = 0;
        int failureCount = 0;
        var failureReasons = new List<string>();

        foreach (var item in importItems)
        {
            // Kiểm tra ràng buộc hợp lệ DataAnnotations chặt chẽ
            var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(item);
            var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(item, validationContext, validationResults, true))
            {
                failureCount++;
                failureReasons.Add($"Ô kho '{item.UnitCode}': {validationResults[0].ErrorMessage}");
                continue;
            }

            try
            {
                var createRequest = new CreateStorageUnitRequest
                {
                    FacilityId = item.FacilityId,
                    UnitTypeId = item.UnitTypeId,
                    AreaId = item.AreaId,
                    UnitCode = item.UnitCode.Trim(),
                    FloorLabel = item.FloorLabel?.Trim(),
                    ZoneLabel = item.ZoneLabel?.Trim(),
                    Notes = item.Notes?.Trim()
                };

                await _storageUnitAdminService.CreateAsync(createRequest, actorUserId, cancellationToken);
                successCount++;
            }
            catch (Exception ex)
            {
                failureCount++;
                failureReasons.Add($"Ô kho '{item.UnitCode}': {ex.Message}");
            }
        }

        if (successCount > 0 && failureCount == 0)
        {
            SuccessMessage = $"Nhập thành công toàn bộ {successCount} ô kho từ file JSON.";
        }
        else if (successCount > 0 && failureCount > 0)
        {
            SuccessMessage = $"Nhập thành công {successCount} ô kho.";
            ErrorMessage = $"Bỏ qua {failureCount} bản ghi lỗi. Chi tiết: {string.Join("; ", failureReasons.Take(3))}";
        }
        else
        {
            ErrorMessage = $"Nhập file thất bại ({failureCount} lỗi). Chi tiết: {string.Join("; ", failureReasons.Take(3))}";
        }

        return RedirectToPage();
    }

    public string GetPaginationUrl(int targetPage)
    {
        var parameters = new List<string>
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

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\"", "\"\"");
    }

    private long GetActorUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdStr, out var id) ? id : 0;
    }
}
