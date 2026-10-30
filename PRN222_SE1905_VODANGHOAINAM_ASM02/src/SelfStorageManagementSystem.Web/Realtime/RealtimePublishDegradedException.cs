using System;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// Thrown when a business lifecycle mutation has been successfully committed to the database,
/// but realtime broadcast was blocked/degraded due to a failure in scope verification or connection pruning.
/// Inherits from InvalidOperationException for backwards compatibility with existing exception handlers.
/// </summary>
public class RealtimePublishDegradedException : InvalidOperationException
{
    public string Operation { get; }
    public long FacilityId { get; }
    public long? UnitId { get; }

    public RealtimePublishDegradedException(string operation, long facilityId, long? unitId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        FacilityId = facilityId;
        UnitId = unitId;
    }

    public RealtimePublishDegradedException(string operation, string message, Exception? innerException = null)
        : this(operation, 0, null, message, innerException)
    {
    }
}
