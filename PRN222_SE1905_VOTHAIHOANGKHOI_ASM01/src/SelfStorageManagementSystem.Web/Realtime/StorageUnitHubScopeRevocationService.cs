using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Hubs;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// T13-R2-F02 / T13-R3-F01 / T13-R3-F02: Realtime scope and permission reconciliation service for StorageUnitHub.
/// Provides a unified reconciliation pipeline executed upon:
/// 1. Client connection (StorageUnitHub.OnConnectedAsync)
/// 2. Admin account / facility assignment mutations (IUserScopeChangedNotifier)
/// 3. Temporal assignment expiration (ends_at) or activation (starts_at) (StorageUnitScopeSyncWorker)
/// Guarantees reliable group revocation with bounded retries and fallback connection abort,
/// strictly recording only verified group memberships in tracker.
/// </summary>
public class StorageUnitHubScopeRevocationService : IUserScopeChangedNotifier
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, SemaphoreSlim> _userReconciliationLocks = new();
    private readonly IHubContext<StorageUnitHub> _hubContext;
    private readonly IStorageUnitHubConnectionTracker _tracker;
    private readonly IFacilityScopeService _facilityScopeService;
    private readonly IAccountIdentityService _accountIdentityService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StorageUnitHubScopeRevocationService> _logger;

    public StorageUnitHubScopeRevocationService(
        IHubContext<StorageUnitHub> hubContext,
        IStorageUnitHubConnectionTracker tracker,
        IFacilityScopeService facilityScopeService,
        IAccountIdentityService accountIdentityService,
        ILogger<StorageUnitHubScopeRevocationService> logger,
        TimeProvider? timeProvider = null)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _facilityScopeService = facilityScopeService ?? throw new ArgumentNullException(nameof(facilityScopeService));
        _accountIdentityService = accountIdentityService ?? throw new ArgumentNullException(nameof(accountIdentityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task NotifyUserScopeChangedAsync(long userId, CancellationToken cancellationToken = default)
    {
        await ReconcileUserConnectionsAsync(userId, cancellationToken);

        try
        {
            var payload = new
            {
                userId = userId,
                action = "scope_changed",
                timestampUtc = _timeProvider.GetUtcNow().ToString("o")
            };
            await _hubContext.Clients.Groups("admin_global", $"user_{userId}").SendAsync("AccountChanged", payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast AccountChanged event for user {UserId}", userId);
        }
    }

    /// <summary>
    /// T13-R7-F01: Prunes and reconciles connections across target broadcast groups (facility group and/or admin_global)
    /// at the exact moment of broadcasting, eliminating temporal and demotion event leakage between worker ticks.
    /// </summary>
    public async Task PruneConnectionsForBroadcastAsync(long? facilityId, bool includeAdminGlobal, CancellationToken cancellationToken = default)
    {
        var userIdsToReconcile = new HashSet<long>();

        if (facilityId.HasValue && facilityId.Value > 0)
        {
            var facilityConns = _tracker.GetConnectionsInGroup($"facility_{facilityId.Value}");
            foreach (var c in facilityConns)
            {
                userIdsToReconcile.Add(c.UserId);
            }
        }

        if (includeAdminGlobal)
        {
            var adminConns = _tracker.GetConnectionsInGroup("admin_global");
            foreach (var c in adminConns)
            {
                userIdsToReconcile.Add(c.UserId);
            }
        }

        if (userIdsToReconcile.Count == 0) return;

        foreach (var userId in userIdsToReconcile)
        {
            await ReconcileUserConnectionsAsync(userId, cancellationToken);
        }
    }

    /// <summary>
    /// T13-R4-F02: Prunes any connections in the specified facility group whose assignment has expired.
    /// </summary>
    public async Task PruneExpiredConnectionsForFacilityAsync(long facilityId, CancellationToken cancellationToken = default)
    {
        await PruneConnectionsForBroadcastAsync(facilityId, includeAdminGlobal: false, cancellationToken);
    }

    /// <summary>
    /// T13-R7-F01: Prunes and verifies all active connections currently in admin_global group.
    /// </summary>
    public async Task PruneUnverifiedAdminConnectionsAsync(CancellationToken cancellationToken = default)
    {
        await PruneConnectionsForBroadcastAsync(null, includeAdminGlobal: true, cancellationToken);
    }

    /// <summary>
    /// Reconciles all active WebSocket connections for a user against current database permissions and assignments.
    /// Serialized per user (T13-R4-F03) to eliminate concurrency races between worker and admin mutations.
    /// </summary>
    public async Task ReconcileUserConnectionsAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return;

        // T13-R4-F03: Serialize reconciliation per user to prevent concurrent reconcile race conditions
        var userLock = _userReconciliationLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var connections = _tracker.GetUserConnections(userId);
            if (connections.Count == 0)
            {
                _logger.LogDebug("No active SignalR connections found for user {UserId} during scope reconciliation.", userId);
                return;
            }

            var currentUser = await _accountIdentityService.GetCurrentUserAsync(userId, cancellationToken);
            var isEmploymentInactive = !string.IsNullOrWhiteSpace(currentUser?.EmploymentStatus) &&
                !string.Equals(currentUser.EmploymentStatus, EmploymentStatusConstants.Active, StringComparison.OrdinalIgnoreCase);

            // Case 1: User does not exist, is inactive/locked, or employment is not active -> remove from ALL groups
            if (currentUser == null || currentUser.Status != UserStatusConstants.Active || isEmploymentInactive)
            {
                _logger.LogInformation("User {UserId} status is {Status} (EmploymentStatus={EmploymentStatus}). Revoking all realtime groups for {ConnCount} active connections.",
                    userId, currentUser?.Status ?? "NotFound", currentUser?.EmploymentStatus ?? "N/A", connections.Count);

                foreach (var connId in connections)
                {
                    var currentGroups = _tracker.GetConnectionGroups(connId);
                    foreach (var g in currentGroups)
                    {
                        var removed = await TryRemoveFromGroupWithRetryAsync(connId, g, cancellationToken);
                        if (removed)
                        {
                            _tracker.RemoveConnectionGroup(connId, g);
                        }
                        else
                        {
                            _logger.LogCritical("Permanent failure revoking group {Group} from connection {ConnId}. Forcibly aborting connection.", g, connId);
                            try
                            {
                                await _hubContext.Clients.Client(connId).SendAsync("ForceDisconnect", "Phiên làm việc đã bị chấm dứt do thay đổi tài khoản.", CancellationToken.None);
                            }
                            catch { }

                            // T13-R8-F01: Attempt abort connection as auxiliary protection
                            try
                            {
                                _tracker.AbortConnection(connId);
                            }
                            catch (Exception abortEx)
                            {
                                _logger.LogError(abortEx, "Abort connection failed for connection {ConnId}", connId);
                            }

                            // T13-R8-F01: Group removal failed; Context.Abort() is only auxiliary and cannot prove synchronous group departure.
                            // Must throw to fail-closed so publisher does not broadcast CRUD to unverified groups.
                            throw new RealtimeScopeRevocationException(
                                $"Không thể xác minh việc thu hồi nhóm '{g}' hoặc ngắt kết nối cho connection '{connId}' của user {userId} (trạng thái: {currentUser?.Status ?? "NotFound"}).");
                        }
                    }
                }
                return;
            }

            // Case 2: User is active -> recalculate valid allowed groups from current DB state
            var isGlobalAdmin = currentUser.Roles.Contains(RoleConstants.SystemAdministrator) ||
                                currentUser.Roles.Contains(RoleConstants.BusinessOperationsManager);

            var isFacilityStaffOrManager = currentUser.Roles.Contains(RoleConstants.FacilityManager) ||
                                          currentUser.Roles.Contains(RoleConstants.FacilityStaff);

            var allowedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            allowedGroups.Add($"user_{userId}");
            if (isGlobalAdmin)
            {
                allowedGroups.Add("admin_global");
            }

            if (isFacilityStaffOrManager)
            {
                var accessibleFacilityIds = await _facilityScopeService.GetAccessibleFacilityIdsAsync(userId, cancellationToken);
                foreach (var fId in accessibleFacilityIds)
                {
                    allowedGroups.Add($"facility_{fId}");
                }
            }

            _logger.LogDebug("Reconciling realtime groups for user {UserId}. Allowed groups: [{AllowedGroups}], Active connections: {ConnCount}",
                userId, string.Join(", ", allowedGroups), connections.Count);

            foreach (var connId in connections)
            {
                var currentGroups = _tracker.GetConnectionGroups(connId);

                // Remove groups that are no longer allowed
                var toRemove = currentGroups.Where(g => !allowedGroups.Contains(g)).ToList();
                foreach (var g in toRemove)
                {
                    var removed = await TryRemoveFromGroupWithRetryAsync(connId, g, cancellationToken);
                    if (removed)
                    {
                        _tracker.RemoveConnectionGroup(connId, g);
                        _logger.LogInformation("Revoked connection {ConnId} of user {UserId} from SignalR group {Group}", connId, userId, g);
                    }
                    else
                    {
                        _logger.LogCritical("Permanent failure revoking group {Group} from connection {ConnId}. Forcibly aborting connection for security.", g, connId);
                        try
                        {
                            await _hubContext.Clients.Client(connId).SendAsync("ForceDisconnect", "Quyền truy cập cơ sở đã bị thu hồi.", CancellationToken.None);
                        }
                        catch { }

                        // T13-R8-F01: Attempt abort connection as auxiliary protection
                        try
                        {
                            _tracker.AbortConnection(connId);
                        }
                        catch (Exception abortEx)
                        {
                            _logger.LogError(abortEx, "Abort connection failed for connection {ConnId}", connId);
                        }

                        // T13-R8-F01: Group removal failed; Context.Abort() is only auxiliary and cannot prove synchronous group departure.
                        // Must throw to fail-closed so publisher does not broadcast CRUD to unverified groups.
                        throw new RealtimeScopeRevocationException(
                            $"Không thể xác minh việc thu hồi nhóm '{g}' hoặc ngắt kết nối cho connection '{connId}' của user {userId}.");
                    }
                }

                // Add newly granted groups
                var toAdd = allowedGroups.Where(g => !currentGroups.Contains(g, StringComparer.OrdinalIgnoreCase)).ToList();
                foreach (var g in toAdd)
                {
                    try
                    {
                        await _hubContext.Groups.AddToGroupAsync(connId, g, cancellationToken);
                        _tracker.AddConnectionGroup(connId, g);
                        _logger.LogInformation("Added connection {ConnId} of user {UserId} to SignalR group {Group}", connId, userId, g);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to add connection {ConnId} to group {Group}. Not recording group in tracker.", connId, g);
                    }
                }
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    /// <summary>
    /// Reconciles all active users currently tracked in memory. Invoked periodically by StorageUnitScopeSyncWorker.
    /// </summary>
    public async Task ReconcileAllActiveUsersAsync(CancellationToken cancellationToken = default)
    {
        var activeUserIds = _tracker.GetActiveUserIds();
        foreach (var userId in activeUserIds)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                await ReconcileUserConnectionsAsync(userId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Periodic scope reconciliation failed for active user {UserId}", userId);
            }
        }
    }

    private async Task<bool> TryRemoveFromGroupWithRetryAsync(string connectionId, string groupName, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await _hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries} failed to remove connection {ConnId} from group {Group}",
                    attempt, maxRetries, connectionId, groupName);

                if (attempt < maxRetries)
                {
                    try
                    {
                        await Task.Delay(40 * attempt, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                }
            }
        }
        return false;
    }
}
