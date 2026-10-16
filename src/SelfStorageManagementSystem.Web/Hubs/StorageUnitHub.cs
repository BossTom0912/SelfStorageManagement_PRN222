using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SelfStorageManagementSystem.Web.Hubs;

/// <summary>
/// Realtime SignalR Hub for Storage Units and System Notifications.
/// Route: /storageUnitHub
/// Follows TeacherCode pattern (HubCreate / HubUpdate / HubDelete).
/// Secured by Cookie Authentication ([Authorize]).
/// Business verification and service calls are implemented in T13 and T15.
/// </summary>
[Authorize]
public class StorageUnitHub : Hub
{
    private readonly ILogger<StorageUnitHub> _logger;

    public StorageUnitHub(ILogger<StorageUnitHub> logger)
    {
        _logger = logger;
    }

    public async Task HubCreate_StorageUnit(string dataObj)
    {
        _logger.LogInformation("SignalR HubCreate_StorageUnit called by connection {ConnectionId}", Context.ConnectionId);
        // Skeleton entrypoint - full payload deserialization and BLL service call wired in T13
        await Task.CompletedTask;
    }

    public async Task HubUpdate_StorageUnit(string dataObj)
    {
        _logger.LogInformation("SignalR HubUpdate_StorageUnit called by connection {ConnectionId}", Context.ConnectionId);
        // Skeleton entrypoint - full payload deserialization and BLL service call wired in T13
        await Task.CompletedTask;
    }

    public async Task HubDelete_StorageUnit(string id, string? expectedUpdatedAt)
    {
        _logger.LogInformation("SignalR HubDelete_StorageUnit called for id {Id} by connection {ConnectionId}", id, Context.ConnectionId);
        // Skeleton entrypoint - full authorization and atomic delete wired in T13
        await Task.CompletedTask;
    }
}
