using System.Text.Json;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class AuditService : IAuditService
{
    private readonly IRepository<audit_log> _auditLogRepository;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IRepository<audit_log> auditLogRepository,
        ILogger<AuditService> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public async Task LogAsync(
        long? actorUserId,
        string? actorRole,
        string entityType,
        string entityId,
        string action,
        object? oldValues,
        object? newValues,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = false
            };

            var logEntry = new audit_log
            {
                actor_user_id = actorUserId,
                actor_role = actorRole,
                entity_schema = "core",
                entity_type = entityType,
                entity_id = entityId,
                action = action,
                old_values = oldValues != null ? JsonSerializer.Serialize(oldValues, options) : null,
                new_values = newValues != null ? JsonSerializer.Serialize(newValues, options) : null,
                request_id = requestId,
                ip_address = ipAddress,
                occurred_at = DateTimeOffset.UtcNow
            };

            await _auditLogRepository.AddAsync(logEntry, cancellationToken);
            await _auditLogRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Do not fail user action if non-critical audit log write fails, but log error
            _logger.LogError(ex, "Failed to record audit log for entity {EntityType} with ID {EntityId}", entityType, entityId);
        }
    }
}
