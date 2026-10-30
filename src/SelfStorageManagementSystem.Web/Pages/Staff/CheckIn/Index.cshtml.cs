using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.Web.Pages.Staff.CheckIn;

[Authorize(Policy = "StaffOnly")]
public class IndexModel : PageModel
{
    private readonly ICheckInService _checkInService;
    private readonly IWebRealtimePublisher? _realtimePublisher;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ICheckInService checkInService,
        ILogger<IndexModel> logger,
        IWebRealtimePublisher? realtimePublisher = null)
    {
        _checkInService = checkInService ?? throw new ArgumentNullException(nameof(checkInService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _realtimePublisher = realtimePublisher;
    }

    [BindProperty(SupportsGet = true)]
    public string? Code { get; set; }

    public CheckInSearchResponse? CheckInData { get; set; }

    public List<EligibleUnitResponse> EligibleUnits { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public string? SuccessMessage { get; set; }

    public string? WarningMessage { get; set; }

    public bool IsStaleConflict { get; set; }

    public bool IsForbiddenAccess { get; set; }

    public string? ForbiddenReason { get; set; }

    // PIN is only populated in memory for the single response after Complete or Reissue
    public string? IssuedPin { get; set; }

    public long? IssuedCredentialId { get; set; }

    public DateTimeOffset? IssuedCredentialExpiresAt { get; set; }

    public bool IsReissueResult { get; set; }

    public HandoverCompleteResponse? HandoverResult { get; set; }

    public PinReissueResponse? PinReissueResult { get; set; }

    // Input model for inspection with default 4 items
    public PerformInspectionInput InspectionForm { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string? code = null, CancellationToken cancellationToken = default)
    {
        var (userId, roles) = GetUserContext();

        // Counter operations strictly require facility_staff or facility_manager role.
        // No bypass for system_administrator or business_operations_manager!
        if (!HasCounterRole(roles))
        {
            IsForbiddenAccess = true;
            ForbiddenReason = "Tài khoản của bạn không có vai trò Nhân viên quầy (facility_staff) hoặc Quản lý cơ sở (facility_manager). Hệ thống không cho phép quyền bypass đối với Quản trị viên hoặc Quản lý vận hành khi tác nghiệp quầy.";
            return Page();
        }

        var searchCode = !string.IsNullOrWhiteSpace(code) ? code.Trim() : Code?.Trim();
        if (!string.IsNullOrWhiteSpace(searchCode))
        {
            await LoadCheckInDataAsync(searchCode, cancellationToken);
        }

        return Page();
    }

    public IActionResult OnPostSearch(string? searchCode)
    {
        if (string.IsNullOrWhiteSpace(searchCode))
        {
            return RedirectToPage("/Staff/CheckIn/Index");
        }

        return RedirectToPage("/Staff/CheckIn/Index", new { code = searchCode.Trim() });
    }

    public async Task<IActionResult> OnPostVerifyIdAsync([FromForm] VerifyIdentityInput input, CancellationToken cancellationToken = default)
    {
        var (userId, roles) = GetUserContext();
        if (!HasCounterRole(roles))
        {
            return Forbid();
        }

        try
        {
            var request = new VerifyIdentityRequest
            {
                VerificationMethod = input.VerificationMethod,
                DocumentNumber = input.DocumentNumber,
                IsConfirmedMatch = input.IsConfirmedMatch,
                Notes = input.Notes
            };

            var result = await _checkInService.VerifyIdentityAsync(input.ReservationId, request, userId, roles, cancellationToken);
            SuccessMessage = $"Xác minh giấy tờ tùy thân thành công ({result.VerificationMethod}).";

            await LoadCheckInDataAsync(input.Code, cancellationToken);

            if (_realtimePublisher != null)
            {
                try
                {
                    await _realtimePublisher.PublishCheckInChangedAsync(result.VerificationId, input.ReservationId, 0, CheckInData?.FacilityId ?? 0, CheckInData?.CustomerId, "verified", cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Realtime publish degraded after verify identity for reservation {ReservationId}", input.ReservationId);
                }
            }

            return Page();
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            ErrorMessage = ex.Message;
            IsForbiddenAccess = true;
            ForbiddenReason = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xác minh giấy tờ cho đơn đặt chỗ {ReservationId}", input.ReservationId);
            ErrorMessage = "Đã xảy ra lỗi hệ thống khi xác minh giấy tờ. Vui lòng thử lại.";
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAssignUnitAsync([FromForm] AssignUnitInput input, CancellationToken cancellationToken = default)
    {
        var (userId, roles) = GetUserContext();
        if (!HasCounterRole(roles))
        {
            return Forbid();
        }

        try
        {
            var request = new AssignUnitRequest
            {
                StorageUnitId = input.StorageUnitId
            };

            var result = await _checkInService.AssignUnitAsync(input.ReservationId, request, userId, roles, cancellationToken);
            SuccessMessage = $"Gán ô kho {result.UnitCode} thành công!";

            await LoadCheckInDataAsync(input.Code, cancellationToken);

            if (_realtimePublisher != null)
            {
                try
                {
                    await _realtimePublisher.PublishCheckInChangedAsync(result.AllocationId, input.ReservationId, result.StorageUnitId, CheckInData?.FacilityId ?? 0, CheckInData?.CustomerId, "allocated", cancellationToken);
                    await _realtimePublisher.PublishCatalogInvalidatedAsync(CheckInData?.FacilityId ?? 0, null, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Realtime publish degraded after assign unit for reservation {ReservationId}", input.ReservationId);
                }
            }

            return Page();
        }
        catch (ConflictException ex)
        {
            IsStaleConflict = true;
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            ErrorMessage = ex.Message;
            IsForbiddenAccess = true;
            ForbiddenReason = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi gán ô kho cho đơn đặt chỗ {ReservationId}", input.ReservationId);
            ErrorMessage = "Đã xảy ra lỗi hệ thống khi gán ô kho. Vui lòng thử lại.";
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostReleaseUnitAsync([FromForm] ReleaseUnitInput input, CancellationToken cancellationToken = default)
    {
        var (userId, roles) = GetUserContext();
        if (!HasCounterRole(roles))
        {
            return Forbid();
        }

        try
        {
            var request = new ReleaseUnitRequest
            {
                CurrentAllocationId = input.CurrentAllocationId,
                Reason = input.Reason
            };

            var result = await _checkInService.ReleaseUnitAsync(input.ReservationId, request, userId, roles, cancellationToken);
            SuccessMessage = $"Đã giải phóng ô kho {result.UnitCode}. Vui lòng chọn và gán ô kho mới.";

            await LoadCheckInDataAsync(input.Code, cancellationToken);

            if (_realtimePublisher != null)
            {
                try
                {
                    await _realtimePublisher.PublishCheckInChangedAsync(result.AllocationId, input.ReservationId, result.StorageUnitId, CheckInData?.FacilityId ?? 0, CheckInData?.CustomerId, "verified", cancellationToken);
                    await _realtimePublisher.PublishCatalogInvalidatedAsync(CheckInData?.FacilityId ?? 0, null, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (RealtimePublishDegradedException ex)
                {
                    _logger.LogWarning(ex, "Realtime publish degraded after release unit for reservation {ReservationId}", input.ReservationId);
                    WarningMessage = "Thao tác gán ô kho đã lưu thành công vào cơ sở dữ liệu, nhưng đồng bộ thời gian thực gặp sự cố tạm thời.";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unexpected error in realtime publish after release unit for reservation {ReservationId}", input.ReservationId);
                    WarningMessage = "Thao tác gán ô kho đã lưu thành công vào cơ sở dữ liệu, nhưng thông báo thời gian thực gặp sự cố.";
                }
            }

            return Page();
        }
        catch (ConflictException ex)
        {
            IsStaleConflict = true;
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            ErrorMessage = ex.Message;
            IsForbiddenAccess = true;
            ForbiddenReason = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi giải phóng ô kho cho đơn đặt chỗ {ReservationId}", input.ReservationId);
            ErrorMessage = "Đã xảy ra lỗi hệ thống khi giải phóng ô kho. Vui lòng thử lại.";
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPerformInspectionAsync([FromForm] PerformInspectionInput input, CancellationToken cancellationToken = default)
    {
        var (userId, roles) = GetUserContext();
        if (!HasCounterRole(roles))
        {
            return Forbid();
        }

        try
        {
            var items = input.Items ?? new List<InspectionItemInput>();
            if (items.Count < 4)
            {
                ErrorMessage = "Checklist kiểm tra phải bao gồm đầy đủ 4 hạng mục bắt buộc: Cửa, Khóa, Đèn và Vệ sinh.";
                await LoadCheckInDataAsync(input.Code, cancellationToken);
                return Page();
            }

            var request = new UnitInspectionRequest
            {
                CurrentAllocationId = input.CurrentAllocationId,
                IsUnsafe = input.IsUnsafe,
                OverallSummary = input.OverallSummary,
                Items = items.Select(i => new InspectionItemRequest
                {
                    ItemName = i.ItemName,
                    Condition = i.Condition,
                    Notes = i.Notes
                }).ToList()
            };

            var result = await _checkInService.PerformInspectionAsync(input.ReservationId, request, userId, roles, cancellationToken);

            if (result.Status == "failed" || result.UnitReleasedDueToFailure)
            {
                ErrorMessage = $"Biên bản kiểm tra KHÔNG ĐẠT (Tình trạng: {result.OverallCondition}). Ô kho {result.UnitCode ?? string.Empty} đã được tự động giải phóng khỏi hợp đồng và chuyển sang trạng thái Bảo trì (Maintenance). Vui lòng chọn và gán ô kho khác.";
            }
            else
            {
                SuccessMessage = $"Biên bản kiểm tra ô kho {result.UnitCode} hoàn tất ĐẠT yêu cầu (Tình trạng: {result.OverallCondition}).";
            }

            await LoadCheckInDataAsync(input.Code, cancellationToken);

            if (_realtimePublisher != null)
            {
                try
                {
                    await _realtimePublisher.PublishCheckInChangedAsync(result.InspectionId, input.ReservationId, result.StorageUnitId ?? 0, CheckInData?.FacilityId ?? 0, CheckInData?.CustomerId, "inspected", cancellationToken);
                    if (result.Status == "failed" || result.UnitReleasedDueToFailure)
                    {
                        await _realtimePublisher.PublishCatalogInvalidatedAsync(CheckInData?.FacilityId ?? 0, null, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (RealtimePublishDegradedException ex)
                {
                    _logger.LogWarning(ex, "Realtime publish degraded after perform inspection for reservation {ReservationId}", input.ReservationId);
                    WarningMessage = "Biên bản kiểm tra đã lưu thành công vào cơ sở dữ liệu, nhưng đồng bộ thời gian thực gặp sự cố tạm thời.";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unexpected error in realtime publish after perform inspection for reservation {ReservationId}", input.ReservationId);
                    WarningMessage = "Biên bản kiểm tra đã lưu thành công vào cơ sở dữ liệu, nhưng thông báo thời gian thực gặp sự cố.";
                }
            }

            return Page();
        }
        catch (ConflictException ex)
        {
            IsStaleConflict = true;
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            ErrorMessage = ex.Message;
            IsForbiddenAccess = true;
            ForbiddenReason = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi kiểm tra ô kho cho đơn đặt chỗ {ReservationId}", input.ReservationId);
            ErrorMessage = "Đã xảy ra lỗi hệ thống khi lưu biên bản kiểm tra ô kho. Vui lòng thử lại.";
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCompleteCheckInAsync([FromForm] CompleteCheckInInput input, CancellationToken cancellationToken = default)
    {
        var (userId, roles) = GetUserContext();
        if (!HasCounterRole(roles))
        {
            return Forbid();
        }

        try
        {
            var request = new CompleteCheckInRequest
            {
                CurrentAllocationId = input.CurrentAllocationId,
                CurrentInspectionId = input.CurrentInspectionId,
                CustomerAcknowledged = input.CustomerAcknowledged,
                Notes = input.Notes
            };

            var result = await _checkInService.CompleteCheckInAsync(input.ReservationId, request, userId, roles, cancellationToken);

            // Response headers: no-store, no-cache for PIN response
            Response.Headers.CacheControl = "no-store, no-cache";
            Response.Headers.Pragma = "no-cache";

            IssuedPin = result.Pin;
            IssuedCredentialId = result.CredentialId;
            IssuedCredentialExpiresAt = result.CredentialExpiresAt;
            HandoverResult = result;
            IsReissueResult = false;
            SuccessMessage = "Bàn giao ô kho và kích hoạt hợp đồng thành công!";

            await LoadCheckInDataAsync(input.Code, cancellationToken);

            if (_realtimePublisher != null)
            {
                try
                {
                    var customerId = CheckInData?.CustomerId;
                    var facilityId = CheckInData?.FacilityId ?? 0;
                    await _realtimePublisher.PublishCheckInChangedAsync(result.UnitAllocationId, input.ReservationId, result.StorageUnitId, facilityId, customerId, "completed", cancellationToken);
                    await _realtimePublisher.PublishCatalogInvalidatedAsync(facilityId, null, cancellationToken);
                    if (customerId.HasValue && customerId.Value > 0 && result.NotificationId.HasValue && result.NotificationId.Value > 0)
                    {
                        await _realtimePublisher.PublishNotificationsChangedAsync(customerId.Value, result.NotificationId.Value, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (RealtimePublishDegradedException ex)
                {
                    _logger.LogWarning(ex, "Realtime publish degraded after complete checkin for reservation {ReservationId}", input.ReservationId);
                    WarningMessage = "Bàn giao kho đã lưu thành công vào cơ sở dữ liệu. Tuy nhiên, tính năng đồng bộ thời gian thực gặp sự cố tạm thời; các màn hình khác sẽ cập nhật khi tải lại trang.";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unexpected error in realtime publish after complete checkin for reservation {ReservationId}", input.ReservationId);
                    WarningMessage = "Bàn giao kho đã lưu thành công vào cơ sở dữ liệu. Tuy nhiên, thông báo thời gian thực gặp sự cố.";
                }
            }

            return Page();
        }
        catch (ConflictException ex)
        {
            IsStaleConflict = true;
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            ErrorMessage = ex.Message;
            IsForbiddenAccess = true;
            ForbiddenReason = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hoàn tất check-in đơn {ReservationId}", input.ReservationId);
            ErrorMessage = "Đã xảy ra lỗi hệ thống khi hoàn tất thủ tục check-in. Vui lòng thử lại.";
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostReissuePinAsync([FromForm] ReissuePinInput input, CancellationToken cancellationToken = default)
    {
        var (userId, roles) = GetUserContext();
        if (!HasCounterRole(roles))
        {
            return Forbid();
        }

        try
        {
            var request = new ReissuePinRequest
            {
                CurrentCredentialId = input.CurrentCredentialId,
                DocumentNumber = input.DocumentNumber,
                IsConfirmedMatch = input.IsConfirmedMatch,
                Reason = input.Reason
            };

            var result = await _checkInService.ReissuePinAsync(input.ReservationId, request, userId, roles, cancellationToken);

            // Response headers: no-store, no-cache for PIN response
            Response.Headers.CacheControl = "no-store, no-cache";
            Response.Headers.Pragma = "no-cache";

            IssuedPin = result.Pin;
            IssuedCredentialId = result.NewCredentialId;
            IssuedCredentialExpiresAt = result.ExpiresAt;
            PinReissueResult = result;
            IsReissueResult = true;
            SuccessMessage = "Cấp lại mã PIN mới thành công! Mã PIN cũ đã bị vô hiệu hóa.";

            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (ConflictException ex)
        {
            IsStaleConflict = true;
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (ForbiddenException ex)
        {
            ErrorMessage = ex.Message;
            IsForbiddenAccess = true;
            ForbiddenReason = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi cấp lại PIN cho đơn {ReservationId}", input.ReservationId);
            ErrorMessage = "Đã xảy ra lỗi hệ thống khi cấp lại mã PIN. Vui lòng thử lại.";
            await LoadCheckInDataAsync(input.Code, cancellationToken);
            return Page();
        }
    }

    private async Task<bool> LoadCheckInDataAsync(string code, CancellationToken cancellationToken = default)
    {
        var (userId, roles) = GetUserContext();
        if (!HasCounterRole(roles))
        {
            IsForbiddenAccess = true;
            ForbiddenReason = "Tài khoản của bạn không có vai trò Nhân viên quầy (facility_staff) hoặc Quản lý cơ sở (facility_manager).";
            return false;
        }

        try
        {
            CheckInData = await _checkInService.SearchCheckInAsync(code, userId, roles, cancellationToken);
            Code = code;

            // Initialize default 4 inspection items if active allocation exists
            if (CheckInData.ActiveAllocation != null)
            {
                InspectionForm = new PerformInspectionInput
                {
                    ReservationId = CheckInData.ReservationId,
                    Code = code,
                    CurrentAllocationId = CheckInData.ActiveAllocation.AllocationId,
                    Items = new List<InspectionItemInput>
                    {
                        new() { ItemName = "Cửa ô kho", Condition = "good", Notes = string.Empty },
                        new() { ItemName = "Khóa & then cài", Condition = "good", Notes = string.Empty },
                        new() { ItemName = "Đèn chiếu sáng", Condition = "good", Notes = string.Empty },
                        new() { ItemName = "Vệ sinh sạch sẽ", Condition = "good", Notes = string.Empty }
                    }
                };
            }

            // If identity is verified and no active allocation, fetch eligible units
            if (CheckInData.LatestVerification?.Result == "verified" && CheckInData.ActiveAllocation == null)
            {
                try
                {
                    EligibleUnits = await _checkInService.GetEligibleUnitsAsync(CheckInData.ReservationId, userId, roles, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể tải danh sách ô kho khả dụng cho đơn đặt chỗ {ReservationId}", CheckInData.ReservationId);
                }
            }

            return true;
        }
        catch (NotFoundException ex)
        {
            ErrorMessage = ex.Message;
            CheckInData = null;
            return false;
        }
        catch (ForbiddenException ex)
        {
            ErrorMessage = ex.Message;
            IsForbiddenAccess = true;
            ForbiddenReason = ex.Message;
            CheckInData = null;
            return false;
        }
        catch (BadRequestException ex)
        {
            ErrorMessage = ex.Message;
            CheckInData = null;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi tra cứu mã check-in {Code}", code);
            ErrorMessage = "Đã xảy ra lỗi không mong muốn khi tra cứu mã check-in.";
            CheckInData = null;
            return false;
        }
    }

    private (long UserId, IReadOnlyList<string> Roles) GetUserContext()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userId = long.TryParse(idClaim, out var id) ? id : 0;
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return (userId, roles);
    }

    private static bool HasCounterRole(IReadOnlyList<string> roles)
    {
        return roles.Contains(RoleConstants.FacilityStaff) || roles.Contains(RoleConstants.FacilityManager);
    }
}
