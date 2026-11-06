using System.Threading;
using System.Threading.Tasks;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

/// <summary>
/// Default no-op implementation of IUserScopeChangedNotifier.
/// Used when no presentation-layer connection manager is registered.
/// </summary>
public class NullUserScopeChangedNotifier : IUserScopeChangedNotifier
{
    public Task NotifyUserScopeChangedAsync(long userId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
