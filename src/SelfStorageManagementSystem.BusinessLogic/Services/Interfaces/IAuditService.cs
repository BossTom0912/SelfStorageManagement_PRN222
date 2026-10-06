namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IAuditService
{
    Task LogAsync(
        long? actorUserId,
        string? actorRole,
        string entityType,
        string entityId,
        string action,
        object? oldValues,
        object? newValues,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);
}
