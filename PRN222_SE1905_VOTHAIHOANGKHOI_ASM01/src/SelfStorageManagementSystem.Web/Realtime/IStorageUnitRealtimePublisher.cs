using System.Threading;
using System.Threading.Tasks;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// Web realtime publisher interface for broadcasting storage unit CRUD events to connected clients.
/// Shared between standard Razor Pages form handlers and SignalR Hub invocations.
/// </summary>
public interface IStorageUnitRealtimePublisher
{
    Task PublishCreatedAsync(StorageUnitAdminDto unit, CancellationToken cancellationToken = default);
    Task PublishUpdatedAsync(StorageUnitAdminDto unit, CancellationToken cancellationToken = default);
    Task PublishDeletedAsync(long unitId, long facilityId, CancellationToken cancellationToken = default);
    Task PublishDeletedAsync(long unitId, CancellationToken cancellationToken = default);
}
