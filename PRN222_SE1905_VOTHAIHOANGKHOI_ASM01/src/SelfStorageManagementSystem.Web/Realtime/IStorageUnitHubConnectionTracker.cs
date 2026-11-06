using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// In-memory tracker of active SignalR connections and their assigned groups for StorageUnitHub.
/// Thread-safe singleton enabling real-time group revocation and synchronization upon user role,
/// facility mutation, or temporal assignment expiration/activation.
/// </summary>
public interface IStorageUnitHubConnectionTracker
{
    void TrackConnection(long userId, string connectionId, IEnumerable<string> initialGroups, Action? abortAction = null);
    void RemoveConnection(long userId, string connectionId);
    IReadOnlyCollection<string> GetUserConnections(long userId);
    IReadOnlyCollection<string> GetConnectionGroups(string connectionId);
    void UpdateConnectionGroups(string connectionId, IEnumerable<string> newGroups);
    void AddConnectionGroup(string connectionId, string group);
    void RemoveConnectionGroup(string connectionId, string group);
    IReadOnlyCollection<long> GetActiveUserIds();
    IReadOnlyCollection<(string ConnectionId, long UserId)> GetConnectionsInGroup(string group);
    void AbortConnection(string connectionId);
}
