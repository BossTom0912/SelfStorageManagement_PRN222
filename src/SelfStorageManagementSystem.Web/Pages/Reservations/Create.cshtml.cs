using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Catalog;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Catalog;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Helpers;
using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.Web.Pages.Reservations;

[Authorize(Policy = "CustomerOnly")]
public class CreateModel : PageModel
{
    private readonly IReservationService _reservationService;
    private readonly IFacilityCatalogService _catalogService;
    private readonly IWebRealtimePublisher? _realtimePublisher;
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(
        IReservationService reservationService,
        IFacilityCatalogService catalogService,
        ILogger<CreateModel> logger,
        IWebRealtimePublisher? realtimePublisher = null)
    {
        _reservationService = reservationService ?? throw new ArgumentNullException(nameof(reservationService));
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _realtimePublisher = realtimePublisher;
    }

    [BindProperty]
    public CreateReservationInput Input { get; set; } = new();

    public FacilityCatalogDto? Facility { get; set; }
    public FacilityUnitTypeCatalogDto? UnitType { get; set; }
    public DateOnly MinStartDate { get; set; }
    public DateOnly MinEndDate { get; set; }
    public DateOnly MaxEndDate { get; set; }
    public string? ConflictMessage { get; set; }

    public class CreateReservationInput
    {
        [Required(ErrorMessage = "Mã cơ sở kho là bắt buộc.")]
        [Range(1, long.MaxValue, ErrorMessage = "Mã cơ sở không hợp lệ.")]
        public long FacilityId { get; set; }

        [Required(ErrorMessage = "Mã loại kho là bắt buộc.")]
        [Range(1, long.MaxValue, ErrorMessage = "Mã loại kho không hợp lệ.")]
        public long UnitTypeId { get; set; }

        [Required(ErrorMessage = "Ngày bắt đầu thuê là bắt buộc.")]
        public DateOnly RentalStartDate { get; set; }

        [Required(ErrorMessage = "Ngày kết thúc thuê là bắt buộc.")]
        public DateOnly RentalEndDate { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(
        long? facilityId,
        long? unitTypeId,
        DateOnly? rentalStartDate,
        DateOnly? rentalEndDate,
        CancellationToken cancellationToken)
    {
        Input.FacilityId = facilityId ?? 0;
        Input.UnitTypeId = unitTypeId ?? 0;

        if (Input.FacilityId > 0 && Input.UnitTypeId > 0)
        {
            await LoadPreviewDataAsync(Input.FacilityId, Input.UnitTypeId, cancellationToken);
        }
        else if (Input.FacilityId > 0)
        {
            try
            {
                Facility = await _catalogService.GetFacilityByIdAsync(Input.FacilityId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể tải thông tin cơ sở {FacilityId}.", Input.FacilityId);
            }
        }

        // Tính ngày hiện tại theo múi giờ cơ sở được chọn, đồng bộ hoàn toàn với ReservationServiceKhoiVTH (BR-RSV-02)
        var facilityTz = AppFormatters.ResolveFacilityTimeZone(Facility?.Timezone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, facilityTz));
        MinStartDate = today;

        var start = rentalStartDate ?? today;
        if (start < today)
        {
            start = today;
        }

        var end = rentalEndDate ?? start.AddMonths(1);

        Input.RentalStartDate = start;
        Input.RentalEndDate = end;

        MinEndDate = start.AddMonths(1);
        MaxEndDate = start.AddMonths(12);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // 1. Xác thực danh tính actor từ Cookie Claims - Tuyệt đối không cho client chọn customer chủ sở hữu
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdClaim, out var currentUserId))
        {
            return Challenge();
        }

        if (Input.FacilityId > 0)
        {
            await LoadPreviewDataAsync(Input.FacilityId, Input.UnitTypeId, cancellationToken);
        }

        // Tính ngày hiện tại theo múi giờ cơ sở được chọn, đồng bộ hoàn toàn với ReservationServiceKhoiVTH (BR-RSV-02)
        var facilityTz = AppFormatters.ResolveFacilityTimeZone(Facility?.Timezone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, facilityTz));
        MinStartDate = today;
        MinEndDate = Input.RentalStartDate.AddMonths(1);
        MaxEndDate = Input.RentalStartDate.AddMonths(12);

        if (Input.RentalStartDate < today)
        {
            ModelState.AddModelError(string.Empty, $"Ngày bắt đầu thuê ({Input.RentalStartDate:yyyy-MM-dd}) không được ở quá khứ (hôm nay theo giờ cơ sở là {today:yyyy-MM-dd}).");
        }

        if (Input.RentalEndDate < Input.RentalStartDate.AddMonths(1))
        {
            ModelState.AddModelError(string.Empty, $"Thời hạn thuê tối thiểu là 1 tháng theo quy định BR-RSV-02 (ngày kết thúc tối thiểu: {Input.RentalStartDate.AddMonths(1):yyyy-MM-dd}).");
        }

        if (Input.RentalEndDate > Input.RentalStartDate.AddMonths(12))
        {
            ModelState.AddModelError(string.Empty, $"Thời hạn thuê tối đa là 12 tháng theo quy định BR-RSV-02 (ngày kết thúc tối đa: {Input.RentalStartDate.AddMonths(12):yyyy-MM-dd}).");
        }

        if (!ModelState.IsValid)
        {
            _logger.LogWarning("POST Create invalid ModelState: {Errors}", string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            return Page();
        }

        var request = new CreateReservationRequest
        {
            FacilityId = Input.FacilityId,
            UnitTypeId = Input.UnitTypeId,
            RentalStartDate = Input.RentalStartDate,
            RentalEndDate = Input.RentalEndDate
        };

        try
        {
            var result = await _reservationService.CreateReservationHoldAsync(currentUserId, request, cancellationToken);

            if (_realtimePublisher != null)
            {
                try
                {
                    await _realtimePublisher.PublishReservationChangedAsync(result.Id, result.FacilityId, currentUserId, "hold", cancellationToken);
                    await _realtimePublisher.PublishCatalogInvalidatedAsync(result.FacilityId, result.UnitTypeId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (RealtimePublishDegradedException ex)
                {
                    _logger.LogWarning(ex, "Realtime publish degraded after creating reservation {ReservationId}", result.Id);
                    TempData["WarningMessage"] = "Đơn đặt chỗ đã được lưu thành công vào hệ thống. Tuy nhiên, đồng bộ thời gian thực gặp sự cố tạm thời; thông tin sẽ tự động làm mới khi tải lại trang.";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unexpected realtime publish error after creating reservation {ReservationId}", result.Id);
                    TempData["WarningMessage"] = "Đơn đặt chỗ đã được lưu thành công vào hệ thống, nhưng đồng bộ thời gian thực gặp sự cố.";
                }
            }

            TempData["SuccessMessage"] = $"Đã khởi tạo đơn đặt chỗ {result.ReservationCode} thành công. Sức chứa được tạm giữ trong 15 phút.";
            return RedirectToPage("Details", new { id = result.Id });
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning("POST Create ConflictException: {Message}", ex.Message);
            ConflictMessage = ex.Message;
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadPreviewDataAsync(Input.FacilityId, Input.UnitTypeId, cancellationToken);
            return Page();
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning("POST Create BadRequestException: {Message}", ex.Message);
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadPreviewDataAsync(Input.FacilityId, Input.UnitTypeId, cancellationToken);
            return Page();
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("POST Create NotFoundException: {Message}", ex.Message);
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadPreviewDataAsync(Input.FacilityId, Input.UnitTypeId, cancellationToken);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning("POST Create ForbiddenException: {Message}", ex.Message);
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadPreviewDataAsync(Input.FacilityId, Input.UnitTypeId, cancellationToken);
            return Page();
        }
    }

    private async Task LoadPreviewDataAsync(long facilityId, long unitTypeId, CancellationToken cancellationToken)
    {
        try
        {
            Facility = await _catalogService.GetFacilityByIdAsync(facilityId, cancellationToken);
            var unitTypes = await _catalogService.GetFacilityUnitTypesAsync(
                facilityId,
                new GetFacilityUnitTypesRequest(),
                cancellationToken);

            UnitType = unitTypes.FirstOrDefault(u => u.UnitTypeId == unitTypeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể tải trước thông tin cơ sở {FacilityId} hoặc loại kho {UnitTypeId}.", facilityId, unitTypeId);
        }
    }
}
