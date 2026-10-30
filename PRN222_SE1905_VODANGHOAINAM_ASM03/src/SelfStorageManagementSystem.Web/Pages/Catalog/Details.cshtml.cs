using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Catalog;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Catalog;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.Catalog;

public class DetailsModel : PageModel
{
    private readonly IFacilityCatalogService _catalogService;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(
        IFacilityCatalogService catalogService,
        ILogger<DetailsModel> logger)
    {
        _catalogService = catalogService;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public long? FacilityId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? RentalStartDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? RentalEndDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public decimal? MaxPrice { get; set; }

    [BindProperty(SupportsGet = true)]
    public decimal? MinAreaM2 { get; set; }

    [BindProperty(SupportsGet = true)]
    public decimal? MaxAreaM2 { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool? ClimateControlled { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? SelectedAreaId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? CandidateUnitTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? CandidateAreaId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;

    [BindProperty(SupportsGet = true)]
    public string ActiveTab { get; set; } = "unit-types";

    public FacilityCatalogDto? Facility { get; set; }

    public List<FacilityUnitTypeCatalogDto> UnitTypes { get; set; } = new();

    public PagedResult<AvailableStorageUnitDto> AvailableUnits { get; set; } = new(new List<AvailableStorageUnitDto>(), 0, 1, 10);

    public FacilityFloorMapDto? FloorMap { get; set; }

    public bool FacilityNotFound { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ValidationMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(long? facilityId, CancellationToken cancellationToken)
    {
        if (facilityId.HasValue && facilityId.Value > 0)
        {
            FacilityId = facilityId.Value;
        }

        if (!FacilityId.HasValue || FacilityId.Value <= 0)
        {
            return SetNotFoundAndReturnPage("Mã cơ sở lưu trữ không hợp lệ hoặc không được cung cấp.");
        }

        if (PageNumber < 1) PageNumber = 1;
        if (PageSize < 1) PageSize = 10;

        try
        {
            Facility = await _catalogService.GetFacilityByIdAsync(FacilityId.Value, cancellationToken);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Không tìm thấy cơ sở lưu trữ ID {FacilityId}", FacilityId);
            return SetNotFoundAndReturnPage(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi tải thông tin cơ sở ID {FacilityId}", FacilityId);
            ErrorMessage = "Không thể tải thông tin cơ sở lưu trữ vào lúc này. Vui lòng thử lại sau.";
            return Page();
        }

        if (Facility == null || Facility.Status != "active")
        {
            return SetNotFoundAndReturnPage("Cơ sở lưu trữ không tồn tại hoặc hiện không mở cửa hoạt động.");
        }

        var todayInTz = FacilityCatalogService.GetCurrentDateInFacilityTimezone(Facility.Timezone);

        if (!RentalStartDate.HasValue)
        {
            RentalStartDate = todayInTz;
        }

        if (!RentalEndDate.HasValue)
        {
            RentalEndDate = RentalStartDate.Value.AddMonths(1);
        }

        // Kiểm tra hợp lệ các tham số lọc trước khi gửi tới service
        DateOnly effectiveStartDate = RentalStartDate.Value;
        DateOnly effectiveEndDate = RentalEndDate.Value;

        if (RentalStartDate.Value < todayInTz)
        {
            ValidationMessage = $"Ngày bắt đầu thuê không được nhỏ hơn ngày hiện tại ({todayInTz:dd/MM/yyyy}) theo giờ cơ sở.";
            effectiveStartDate = todayInTz;
            effectiveEndDate = effectiveStartDate.AddMonths(1);
        }
        else if (RentalEndDate.Value <= RentalStartDate.Value)
        {
            ValidationMessage = "Ngày kết thúc thuê phải lớn hơn ngày bắt đầu thuê.";
            effectiveEndDate = effectiveStartDate.AddMonths(1);
        }
        else if (RentalEndDate.Value < RentalStartDate.Value.AddMonths(1))
        {
            ValidationMessage = "Thời hạn thuê tối thiểu là 1 tháng theo quy định BR-RSV-02.";
            effectiveEndDate = effectiveStartDate.AddMonths(1);
        }
        else if (RentalEndDate.Value > RentalStartDate.Value.AddMonths(12))
        {
            ValidationMessage = "Thời hạn thuê tối đa cho mỗi lần đặt là 12 tháng theo quy định BR-RSV-02.";
            effectiveEndDate = effectiveStartDate.AddMonths(12);
        }

        // FINDING 3 FIX: Đồng bộ lại RentalStartDate và RentalEndDate sang giá trị hiệu lực đã chuẩn hóa
        // để toàn bộ UI date picker, availability queries, pagination URLs và reservation link dùng cùng một cặp ngày hợp lệ
        RentalStartDate = effectiveStartDate;
        RentalEndDate = effectiveEndDate;

        if (MaxPrice.HasValue && MaxPrice.Value < 0)
        {
            ValidationMessage = "Giá thuê tối đa không được là số âm.";
            MaxPrice = null;
        }

        if (MinAreaM2.HasValue && MinAreaM2.Value < 0)
        {
            ValidationMessage = "Diện tích tối thiểu không được là số âm.";
            MinAreaM2 = null;
        }

        if (MaxAreaM2.HasValue && MaxAreaM2.Value < 0)
        {
            ValidationMessage = "Diện tích tối đa không được là số âm.";
            MaxAreaM2 = null;
        }

        if (MinAreaM2.HasValue && MaxAreaM2.HasValue && MinAreaM2.Value > MaxAreaM2.Value)
        {
            ValidationMessage = "Diện tích tối thiểu không được lớn hơn diện tích tối đa.";
            MaxAreaM2 = null;
        }

        // Tải danh mục loại kho và bảng giá
        try
        {
            var unitTypesRequest = new GetFacilityUnitTypesRequest
            {
                RentalStartDate = effectiveStartDate,
                RentalEndDate = effectiveEndDate,
                MaxPrice = MaxPrice,
                MinAreaM2 = MinAreaM2,
                MaxAreaM2 = MaxAreaM2,
                ClimateControlled = ClimateControlled
            };
            UnitTypes = await _catalogService.GetFacilityUnitTypesAsync(FacilityId.Value, unitTypesRequest, cancellationToken);
        }
        catch (BadRequestException ex)
        {
            ValidationMessage = ex.Message;
            UnitTypes = new List<FacilityUnitTypeCatalogDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải danh mục loại kho cho cơ sở ID {FacilityId}", FacilityId);
            ErrorMessage = "Không thể tải danh mục loại kho vào lúc này.";
            UnitTypes = new List<FacilityUnitTypeCatalogDto>();
        }

        // Tải sơ đồ mặt bằng ô kho
        try
        {
            var floorMapRequest = new GetFacilityFloorMapRequest
            {
                AreaId = SelectedAreaId,
                RentalStartDate = effectiveStartDate,
                RentalEndDate = effectiveEndDate
            };
            FloorMap = await _catalogService.GetFacilityFloorMapAsync(FacilityId.Value, floorMapRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải sơ đồ mặt bằng cho cơ sở ID {FacilityId}", FacilityId);
            FloorMap = new FacilityFloorMapDto
            {
                FacilityId = Facility.Id,
                FacilityName = Facility.Name
            };
        }

        // Tải danh sách ô kho ứng viên khả dụng
        try
        {
            var unitsRequest = new GetAvailableUnitsRequest
            {
                UnitTypeId = CandidateUnitTypeId,
                AreaId = CandidateAreaId,
                ClimateControlled = ClimateControlled,
                MaxPrice = MaxPrice,
                MinAreaM2 = MinAreaM2,
                MaxAreaM2 = MaxAreaM2,
                RentalStartDate = effectiveStartDate,
                RentalEndDate = effectiveEndDate,
                PageNumber = PageNumber,
                PageSize = PageSize
            };
            AvailableUnits = await _catalogService.GetAvailableUnitsAsync(FacilityId.Value, unitsRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải danh sách ô kho khả dụng cho cơ sở ID {FacilityId}", FacilityId);
            AvailableUnits = new PagedResult<AvailableStorageUnitDto>(new List<AvailableStorageUnitDto>(), 0, PageNumber, PageSize);
        }

        return Page();
    }

    /// <summary>
    /// Tạo đường dẫn đặt chỗ theo quy chuẩn BR-RSV-03.
    /// Nếu người dùng chưa đăng nhập, trả về URL đăng nhập có chứa ReturnUrl nội bộ.
    /// </summary>
    public string GetReservationUrl(long unitTypeId)
    {
        var targetReservationPath = $"/Reservations?facilityId={FacilityId}&unitTypeId={unitTypeId}&rentalStartDate={RentalStartDate:yyyy-MM-dd}&rentalEndDate={RentalEndDate:yyyy-MM-dd}";

        if (User.Identity?.IsAuthenticated == true)
        {
            return targetReservationPath;
        }

        // Giữ ReturnUrl nội bộ khi cần đăng nhập
        return $"/Account/Login?returnUrl={Uri.EscapeDataString(targetReservationPath)}";
    }

    /// <summary>
    /// Tạo URL phân trang cho bảng danh sách ô ứng viên
    /// </summary>
    public string GetCandidateUnitsPaginationUrl(int targetPage)
    {
        var queryParams = BuildBaseQueryParams();
        queryParams.Add($"PageNumber={targetPage}");
        queryParams.Add($"PageSize={PageSize}");
        queryParams.Add("ActiveTab=candidate-units");

        if (CandidateUnitTypeId.HasValue)
        {
            queryParams.Add($"CandidateUnitTypeId={CandidateUnitTypeId.Value}");
        }

        if (CandidateAreaId.HasValue)
        {
            queryParams.Add($"CandidateAreaId={CandidateAreaId.Value}");
        }

        return $"/Catalog/Details?{string.Join("&", queryParams)}";
    }

    /// <summary>
    /// Xây dựng danh sách tham số cơ bản từ bộ lọc hiện thời
    /// </summary>
    public List<string> BuildBaseQueryParams()
    {
        var list = new List<string>
        {
            $"facilityId={FacilityId}"
        };

        if (RentalStartDate.HasValue)
        {
            list.Add($"RentalStartDate={RentalStartDate.Value:yyyy-MM-dd}");
        }

        if (RentalEndDate.HasValue)
        {
            list.Add($"RentalEndDate={RentalEndDate.Value:yyyy-MM-dd}");
        }

        if (MaxPrice.HasValue)
        {
            list.Add($"MaxPrice={MaxPrice.Value}");
        }

        if (MinAreaM2.HasValue)
        {
            list.Add($"MinAreaM2={MinAreaM2.Value}");
        }

        if (MaxAreaM2.HasValue)
        {
            list.Add($"MaxAreaM2={MaxAreaM2.Value}");
        }

        if (ClimateControlled.HasValue)
        {
            list.Add($"ClimateControlled={ClimateControlled.Value.ToString().ToLower()}");
        }

        if (SelectedAreaId.HasValue)
        {
            list.Add($"SelectedAreaId={SelectedAreaId.Value}");
        }

        return list;
    }

    /// <summary>
    /// FINDING 4 FIX: Thiết lập mã trạng thái HTTP 404 NotFound nhưng vẫn xuất giao diện
    /// thông báo thân thiện của trang Catalog/Details (vô hiệu hóa StatusCodePagesMiddleware tự re-execute).
    /// </summary>
    private IActionResult SetNotFoundAndReturnPage(string message)
    {
        var statusCodePagesFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>();
        if (statusCodePagesFeature != null)
        {
            statusCodePagesFeature.Enabled = false;
        }

        Response.StatusCode = StatusCodes.Status404NotFound;
        FacilityNotFound = true;
        ErrorMessage = message;
        return Page();
    }
}
