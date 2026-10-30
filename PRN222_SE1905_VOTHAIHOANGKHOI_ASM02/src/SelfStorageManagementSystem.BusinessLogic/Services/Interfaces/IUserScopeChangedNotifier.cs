using System.Threading;
using System.Threading.Tasks;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

/// <summary>
/// Hook interface invoked when a user's role, account status, or facility assignments are mutated.
/// Allows presentation layers (e.g. SignalR Hubs) to dynamically revoke or adjust active connections.
/// </summary>
public interface IUserScopeChangedNotifier
{
    Task NotifyUserScopeChangedAsync(long userId, CancellationToken cancellationToken = default);
}
