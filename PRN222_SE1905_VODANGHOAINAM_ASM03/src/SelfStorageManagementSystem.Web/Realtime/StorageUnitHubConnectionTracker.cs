using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// Thread-safe in-memory implementation of IStorageUnitHubConnectionTracker.
/// Tracks live WebSocket connections mapped to users, SignalR groups, and server-side abort handlers.
/// </summary>
public class StorageUnitHubConnectionTracker : IStorageUnitHubConnectionTracker
{
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<string, byte>> _userConnections = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connectionGroups = new();
    private readonly ConcurrentDictionary<string, Action> _connectionAborts = new();

    public void TrackConnection(long userId, string connectionId, IEnumerable<string> initialGroups, Action? abortAction = null)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(connectionId)) return;

        var userConns = _userConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
        userConns.TryAdd(connectionId, 0);

        if (abortAction != null)
        {
            _connectionAborts[connectionId] = abortAction;
        }

        var groupsDict = _connectionGroups.GetOrAdd(connectionId, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        groupsDict.Clear();
        if (initialGroups != null)
        {
            foreach (var g in initialGroups)
            {
                if (!string.IsNullOrWhiteSpace(g))
                {
                    groupsDict.TryAdd(g, 0);
                }
            }
        }
    }

    public void RemoveConnection(long userId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return;

        if (userId > 0 && _userConnections.TryGetValue(userId, out var userConns))
        {
            userConns.TryRemove(connectionId, out _);
            if (userConns.IsEmpty)
            {
                _userConnections.TryRemove(userId, out _);
            }
        }
        else
        {
            // Fallback: search all users if userId not provided
            foreach (var kvp in _userConnections)
            {
                if (kvp.Value.TryRemove(connectionId, out _) && kvp.Value.IsEmpty)
                {
                    _userConnections.TryRemove(kvp.Key, out _);
                }
            }
        }

        _connectionGroups.TryRemove(connectionId, out _);
        _connectionAborts.TryRemove(connectionId, out _);
    }

    public IReadOnlyCollection<string> GetUserConnections(long userId)
    {
        if (userId <= 0) return Array.Empty<string>();
        if (_userConnections.TryGetValue(userId, out var userConns))
        {
            return userConns.Keys.ToList();
        }
        return Array.Empty<string>();
    }

    public IReadOnlyCollection<string> GetConnectionGroups(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return Array.Empty<string>();
        if (_connectionGroups.TryGetValue(connectionId, out var groupsDict))
        {
            return groupsDict.Keys.ToList();
        }
        return Array.Empty<string>();
    }

    public void UpdateConnectionGroups(string connectionId, IEnumerable<string> newGroups)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return;
        var groupsDict = _connectionGroups.GetOrAdd(connectionId, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        groupsDict.Clear();
        if (newGroups != null)
        {
            foreach (var g in newGroups)
            {
                if (!string.IsNullOrWhiteSpace(g))
                {
                    groupsDict.TryAdd(g, 0);
                }
            }
        }
    }

    public void AddConnectionGroup(string connectionId, string group)
    {
        if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(group)) return;
        var groupsDict = _connectionGroups.GetOrAdd(connectionId, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        groupsDict.TryAdd(group, 0);
    }

    public void RemoveConnectionGroup(string connectionId, string group)
    {
        if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(group)) return;
        if (_connectionGroups.TryGetValue(connectionId, out var groupsDict))
        {
            groupsDict.TryRemove(group, out _);
        }
    }

    public IReadOnlyCollection<long> GetActiveUserIds()
    {
        return _userConnections.Keys.ToList();
    }

    public IReadOnlyCollection<(string ConnectionId, long UserId)> GetConnectionsInGroup(string group)
    {
        if (string.IsNullOrWhiteSpace(group)) return Array.Empty<(string, long)>();

        var result = new List<(string ConnectionId, long UserId)>();
        foreach (var (userId, conns) in _userConnections)
        {
            foreach (var connId in conns.Keys)
            {
                if (_connectionGroups.TryGetValue(connId, out var groups) && groups.ContainsKey(group))
                {
                    result.Add((connId, userId));
                }
            }
        }
        return result;
    }

    public void AbortConnection(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return;
        if (_connectionAborts.TryGetValue(connectionId, out var abortAction) && abortAction != null)
        {
            // T13-R8-F01: Do not swallow abort exceptions! If abort delegate throws, rethrow
            // and do NOT remove connection or groups from tracker, preventing false verified states.
            abortAction.Invoke();
            _connectionAborts.TryRemove(connectionId, out _);
        }
        else
        {
            throw new InvalidOperationException($"No abort delegate registered for connection {connectionId}.");
        }

        // Only remove tracker records if abort delegate executed without throwing
        _connectionGroups.TryRemove(connectionId, out _);

        // Remove from user connections
        foreach (var kvp in _userConnections)
        {
            if (kvp.Value.TryRemove(connectionId, out _) && kvp.Value.IsEmpty)
            {
                _userConnections.TryRemove(kvp.Key, out _);
            }
        }
    }
}
