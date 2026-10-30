using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Realtime;

namespace SelfStorageManagementSystem.Web.Hubs;

/// <summary>
/// Realtime SignalR Hub for Storage Units Management.
/// Route: /storageUnitHub
/// Follows TeacherCode pattern:
/// - Invocations: HubCreate_StorageUnit, HubUpdate_StorageUnit, HubDelete_StorageUnit
/// - Broadcasts: Receiver_CreateStorageUnit, Receiver_UpdateStorageUnit, Receiver_DeleteStorageUnit
/// Secured by Cookie Authentication ([Authorize]).
/// Enforces BLL services, role validation, DTO deserialization validation, and safe error mapping.
/// </summary>
[Authorize]
public class StorageUnitHub : Hub
{
    private readonly IStorageUnitAdminService _storageUnitAdminService;
    private readonly IStorageUnitRealtimePublisher _realtimePublisher;
    private readonly IFacilityScopeService _facilityScopeService;
    private readonly IStorageUnitHubConnectionTracker _connectionTracker;
    private readonly ILogger<StorageUnitHub> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly StorageUnitHubScopeRevocationService _scopeRevocationService;

    public StorageUnitHub(
        IStorageUnitAdminService storageUnitAdminService,
        IStorageUnitRealtimePublisher realtimePublisher,
        IFacilityScopeService facilityScopeService,
        IStorageUnitHubConnectionTracker connectionTracker,
        StorageUnitHubScopeRevocationService scopeRevocationService,
        ILogger<StorageUnitHub> logger)
    {
        _storageUnitAdminService = storageUnitAdminService ?? throw new ArgumentNullException(nameof(storageUnitAdminService));
        _realtimePublisher = realtimePublisher ?? throw new ArgumentNullException(nameof(realtimePublisher));
        _facilityScopeService = facilityScopeService ?? throw new ArgumentNullException(nameof(facilityScopeService));
        _connectionTracker = connectionTracker ?? throw new ArgumentNullException(nameof(connectionTracker));
        _scopeRevocationService = scopeRevocationService ?? throw new ArgumentNullException(nameof(scopeRevocationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// T13-F01 / T13-R2-F02 / T13-R3-F01: Quản lý nhóm SignalR theo phạm vi cơ sở và quyền hạn khi client kết nối.
    /// Tách quyền kết nối/nhận sự kiện khỏi quyền gọi CRUD.
    /// Tái sử dụng thống nhất pipeline ReconcileUserConnectionsAsync giữa kết nối, DB mutation và worker định kỳ.
    /// Đăng ký handler Context.Abort để ngắt kết nối cưỡng chế nếu việc thu hồi quyền gặp lỗi hạ tầng lặp lại.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        var userIdStr = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (long.TryParse(userIdStr, out var actorUserId) && actorUserId > 0)
        {
            _connectionTracker.TrackConnection(actorUserId, Context.ConnectionId, Array.Empty<string>(), () => Context.Abort());
            await _scopeRevocationService.ReconcileUserConnectionsAsync(actorUserId, Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var user = Context.User;
        var userIdStr = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (long.TryParse(userIdStr, out var actorUserId) && actorUserId > 0)
        {
            _connectionTracker.RemoveConnection(actorUserId, Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Invocable by client: HubCreate_StorageUnit(string dataObj)
    /// Receives JSON string containing create storage unit request, validates DTO, executes BLL service,
    /// and broadcasts Receiver_CreateStorageUnit event to all clients after commit.
    /// </summary>
    public async Task<StorageUnitRealtimeEventDto> HubCreate_StorageUnit(string dataObj)
    {
        var (actorUserId, canManage) = GetActorInfo();
        if (!canManage)
        {
            throw new HubException("Nhân viên thường không có quyền tạo mới ô kho.");
        }

        if (string.IsNullOrWhiteSpace(dataObj))
        {
            throw new HubException("Dữ liệu tạo ô kho không được để trống.");
        }

        CreateStorageUnitRequest request;
        try
        {
            request = JsonSerializer.Deserialize<CreateStorageUnitRequest>(dataObj, JsonOptions)
                      ?? throw new JsonException("Deserialized request is null.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi phân tích JSON khi tạo ô kho qua SignalR.");
            throw new HubException("Dữ liệu JSON không hợp lệ.");
        }

        ValidateDto(request);

        StorageUnitAdminDto created;
        try
        {
            created = await _storageUnitAdminService.CreateAsync(request, actorUserId);
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning(ex, "Xung đột khi tạo ô kho qua SignalR: {Message}", ex.Message);
            throw new HubException(ex.Message);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền khi tạo ô kho qua SignalR: {Message}", ex.Message);
            throw new HubException(ex.Message);
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(ex, "Yêu cầu không hợp lệ khi tạo ô kho qua SignalR: {Message}", ex.Message);
            throw new HubException(ex.Message);
        }
        catch (HubException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi tạo ô kho qua SignalR.");
            throw new HubException("Đã xảy ra lỗi không mong muốn khi tạo ô kho.");
        }

        // T13-R6-F01: Thao tác DB đã lưu thành công. Xử lý đồng bộ realtime sau commit độc lập.
        try
        {
            await _realtimePublisher.PublishCreatedAsync(created);
        }
        catch (RealtimePublishDegradedException ex)
        {
            _logger.LogWarning(ex, "Đã tạo ô kho '{UnitCode}' (ID: {UnitId}) thành công trong CSDL qua SignalR nhưng đồng bộ realtime bị suy giảm cho cơ sở {FacilityId}.", created.UnitCode, created.Id, created.FacilityId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi ngoài dự kiến khi phát realtime tạo mới qua SignalR cho ô kho '{UnitCode}' (ID: {UnitId}).", created.UnitCode, created.Id);
        }

        return StorageUnitRealtimeEventDto.FromAdminDto(created);
    }

    /// <summary>
    /// Invocable by client: HubUpdate_StorageUnit(string dataObj)
    /// Receives JSON string containing update fields and optional concurrency timestamp,
    /// validates DTO, executes BLL update, and broadcasts Receiver_UpdateStorageUnit after commit.
    /// </summary>
    public async Task<StorageUnitRealtimeEventDto> HubUpdate_StorageUnit(string dataObj)
    {
        var (actorUserId, canManage) = GetActorInfo();
        if (!canManage)
        {
            throw new HubException("Nhân viên thường không có quyền chỉnh sửa ô kho.");
        }

        if (string.IsNullOrWhiteSpace(dataObj))
        {
            throw new HubException("Dữ liệu cập nhật ô kho không được để trống.");
        }

        StorageUnitHubUpdateRequest hubReq;
        try
        {
            hubReq = JsonSerializer.Deserialize<StorageUnitHubUpdateRequest>(dataObj, JsonOptions)
                     ?? throw new JsonException("Deserialized request is null.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi phân tích JSON khi cập nhật ô kho qua SignalR.");
            throw new HubException("Dữ liệu JSON không hợp lệ.");
        }

        ValidateDto(hubReq);

        StorageUnitAdminDto updated;
        try
        {
            // Nếu không cung cấp ExpectedUpdatedAt từ client, nạp mốc thời gian hiện tại từ DB để bảo đảm concurrency
            DateTimeOffset expectedUpdatedAt;
            if (hubReq.ExpectedUpdatedAt.HasValue && hubReq.ExpectedUpdatedAt.Value != default)
            {
                expectedUpdatedAt = hubReq.ExpectedUpdatedAt.Value;
            }
            else
            {
                var existing = await _storageUnitAdminService.GetByIdAsync(hubReq.Id, actorUserId);
                if (existing == null)
                {
                    throw new HubException($"Ô kho ID {hubReq.Id} không tồn tại hoặc đã bị xóa.");
                }
                expectedUpdatedAt = existing.UpdatedAt;
            }

            var updateRequest = new UpdateStorageUnitRequest
            {
                ExpectedUpdatedAt = expectedUpdatedAt,
                UnitCode = hubReq.UnitCode,
                AreaId = hubReq.AreaId,
                FloorLabel = hubReq.FloorLabel,
                ZoneLabel = hubReq.ZoneLabel,
                Notes = hubReq.Notes
            };

            updated = await _storageUnitAdminService.UpdateAsync(hubReq.Id, updateRequest, actorUserId);
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning(ex, "Xung đột đồng thời khi cập nhật ô kho {Id} qua SignalR: {Message}", hubReq.Id, ex.Message);
            throw new HubException(ex.Message);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền khi cập nhật ô kho {Id} qua SignalR: {Message}", hubReq.Id, ex.Message);
            throw new HubException(ex.Message);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Không tìm thấy ô kho {Id} qua SignalR: {Message}", hubReq.Id, ex.Message);
            throw new HubException(ex.Message);
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(ex, "Yêu cầu không hợp lệ khi cập nhật ô kho {Id} qua SignalR: {Message}", hubReq.Id, ex.Message);
            throw new HubException(ex.Message);
        }
        catch (HubException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi cập nhật ô kho {Id} qua SignalR.", hubReq.Id);
            throw new HubException("Đã xảy ra lỗi không mong muốn khi cập nhật ô kho.");
        }

        // T13-R6-F01: Thao tác DB đã lưu thành công. Xử lý đồng bộ realtime sau commit độc lập.
        try
        {
            await _realtimePublisher.PublishUpdatedAsync(updated);
        }
        catch (RealtimePublishDegradedException ex)
        {
            _logger.LogWarning(ex, "Đã cập nhật ô kho '{UnitCode}' (ID: {UnitId}) thành công trong CSDL qua SignalR nhưng đồng bộ realtime bị suy giảm cho cơ sở {FacilityId}.", updated.UnitCode, updated.Id, updated.FacilityId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi ngoài dự kiến khi phát realtime cập nhật qua SignalR cho ô kho '{UnitCode}' (ID: {UnitId}).", updated.UnitCode, updated.Id);
        }

        return StorageUnitRealtimeEventDto.FromAdminDto(updated);
    }

    /// <summary>
    /// Invocable by client: HubDelete_StorageUnit(string id, string? expectedUpdatedAt = null)
    /// Receives unit ID and optional expected update timestamp, executes BLL atomic deletion,
    /// and broadcasts Receiver_DeleteStorageUnit(string unitId) after commit.
    /// </summary>
    public async Task<string> HubDelete_StorageUnit(string id, string? expectedUpdatedAt = null)
    {
        var (actorUserId, canManage) = GetActorInfo();
        if (!canManage)
        {
            throw new HubException("Nhân viên thường không có quyền xóa ô kho.");
        }

        if (!long.TryParse(id, out var unitId) || unitId <= 0)
        {
            throw new HubException("Mã định danh ô kho không hợp lệ.");
        }

        DeleteStorageUnitResult result;
        long facilityId;
        try
        {
            var existing = await _storageUnitAdminService.GetByIdAsync(unitId, actorUserId);
            if (existing == null)
            {
                throw new HubException($"Ô kho ID {unitId} không tồn tại hoặc đã bị xóa.");
            }

            DateTimeOffset expUpdatedAt;
            if (!string.IsNullOrWhiteSpace(expectedUpdatedAt) && DateTimeOffset.TryParse(expectedUpdatedAt, out var parsed))
            {
                expUpdatedAt = parsed;
            }
            else
            {
                expUpdatedAt = existing.UpdatedAt;
            }

            facilityId = existing.FacilityId;
            result = await _storageUnitAdminService.DeleteAsync(unitId, expUpdatedAt, actorUserId);
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning(ex, "Xung đột chính sách khi xóa ô kho {Id} qua SignalR: {Message}", unitId, ex.Message);
            throw new HubException(ex.Message);
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(ex, "Từ chối quyền khi xóa ô kho {Id} qua SignalR: {Message}", unitId, ex.Message);
            throw new HubException(ex.Message);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Không tìm thấy ô kho {Id} khi xóa qua SignalR: {Message}", unitId, ex.Message);
            throw new HubException(ex.Message);
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(ex, "Yêu cầu không hợp lệ khi xóa ô kho {Id} qua SignalR: {Message}", unitId, ex.Message);
            throw new HubException(ex.Message);
        }
        catch (HubException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi xóa ô kho {Id} qua SignalR.", unitId);
            throw new HubException("Đã xảy ra lỗi không mong muốn khi xóa ô kho.");
        }

        // T13-R6-F01: Thao tác DB đã lưu thành công. Xử lý đồng bộ realtime sau commit độc lập.
        try
        {
            await _realtimePublisher.PublishDeletedAsync(unitId, facilityId);
        }
        catch (RealtimePublishDegradedException ex)
        {
            _logger.LogWarning(ex, "Đã xóa ô kho '{UnitCode}' (ID: {UnitId}) thành công trong CSDL qua SignalR nhưng đồng bộ realtime bị suy giảm cho cơ sở {FacilityId}.", result.UnitCode, unitId, facilityId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi ngoài dự kiến khi phát realtime xóa qua SignalR cho ô kho '{UnitCode}' (ID: {UnitId}).", result.UnitCode, unitId);
        }

        return unitId.ToString();
    }

    private (long actorUserId, bool canManage) GetActorInfo()
    {
        var user = Context.User;
        var userIdStr = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdStr, out var actorUserId) || actorUserId <= 0)
        {
            throw new HubException("Chưa đăng nhập hoặc phiên làm việc đã hết hạn.");
        }

        var canManage = user != null &&
            (user.IsInRole(RoleConstants.FacilityManager) ||
             user.IsInRole(RoleConstants.BusinessOperationsManager) ||
             user.IsInRole(RoleConstants.SystemAdministrator));

        return (actorUserId, canManage);
    }

    private static void ValidateDto<T>(T dto) where T : class
    {
        var context = new ValidationContext(dto);
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(dto, context, results, validateAllProperties: true))
        {
            var errors = string.Join("; ", results.Select(r => r.ErrorMessage));
            throw new HubException($"Dữ liệu không hợp lệ: {errors}");
        }
    }
}
