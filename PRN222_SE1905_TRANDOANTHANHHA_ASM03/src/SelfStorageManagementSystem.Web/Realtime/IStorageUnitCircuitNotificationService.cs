using System;
using System.Threading.Tasks;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// Lightweight notification payload emitted when storage units are created, updated, or deleted.
/// Carries only non-sensitive routing IDs so receiving Blazor Server circuits or background services
/// can independently reload their views via scoped BLL services under their own authenticated actor.
/// </summary>
public class StorageUnitCircuitNotification
{
    public long FacilityId { get; init; }
    public long UnitId { get; init; }
    public string Action { get; init; } = string.Empty; // "Created", "Updated", "Deleted"
}

/// <summary>
/// Cross-circuit notification broker for Blazor Server circuits running within the web host.
/// In Blazor Server, each circuit runs independently; this singleton service notifies active circuits
/// when data mutations occur, allowing components to refresh state without broadcasting private entity data
/// or performing full-page HTML refetches.
/// </summary>
public interface IStorageUnitCircuitNotificationService
{
    event Func<StorageUnitCircuitNotification, Task>? OnStorageUnitChanged;
    Task NotifyChangedAsync(StorageUnitCircuitNotification notification);
}

/// <summary>
/// Default thread-safe in-process implementation of IStorageUnitCircuitNotificationService.
/// </summary>
public class StorageUnitCircuitNotificationService : IStorageUnitCircuitNotificationService
{
    public event Func<StorageUnitCircuitNotification, Task>? OnStorageUnitChanged;

    public async Task NotifyChangedAsync(StorageUnitCircuitNotification notification)
    {
        var handler = OnStorageUnitChanged;
        if (handler != null)
        {
            foreach (Func<StorageUnitCircuitNotification, Task> invocation in handler.GetInvocationList())
            {
                try
                {
                    await invocation(notification);
                }
                catch
                {
                    // Ignore transient subscriber exceptions to ensure all active circuits receive notification
                }
            }
        }
    }
}
