using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.Reservations;

[Authorize(Policy = "CustomerOnly")]
public class IndexModel : PageModel
{
    private readonly IReservationService _reservationService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IReservationService reservationService,
        ILogger<IndexModel> logger)
    {
        _reservationService = reservationService ?? throw new ArgumentNullException(nameof(reservationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PagedResult<ReservationListItemResponse>? Reservations { get; set; }
    public string? CurrentStatus { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    public async Task<IActionResult> OnGetAsync(
        long? facilityId,
        long? unitTypeId,
        DateOnly? rentalStartDate,
        DateOnly? rentalEndDate,
        string? status,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        // 1. Tương thích liên kết từ Catalog/Details: Nếu truyền facilityId và unitTypeId, chuyển hướng sang trang Create
        if (facilityId.HasValue && unitTypeId.HasValue)
        {
            return RedirectToPage("Create", new
            {
                facilityId = facilityId.Value,
                unitTypeId = unitTypeId.Value,
                rentalStartDate = rentalStartDate?.ToString("yyyy-MM-dd"),
                rentalEndDate = rentalEndDate?.ToString("yyyy-MM-dd")
            });
        }

        // 2. Xác thực danh tính actor từ Cookie Claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdClaim, out var currentUserId))
        {
            return Challenge();
        }

        CurrentStatus = status;
        PageNumber = Math.Max(1, pageNumber);

        var request = new GetMyReservationsRequest
        {
            Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            PageNumber = PageNumber,
            PageSize = PageSize
        };

        try
        {
            Reservations = await _reservationService.GetMyReservationsAsync(currentUserId, request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi tải danh sách đơn đặt chỗ của khách hàng {CustomerId}.", currentUserId);
            Reservations = new PagedResult<ReservationListItemResponse>(new List<ReservationListItemResponse>(), 0, PageNumber, PageSize);
        }

        return Page();
    }
}
