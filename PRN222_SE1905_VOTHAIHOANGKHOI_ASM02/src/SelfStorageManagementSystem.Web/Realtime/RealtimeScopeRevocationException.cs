using System;

namespace SelfStorageManagementSystem.Web.Realtime;

/// <summary>
/// T13-R8-F01: Ngoại lệ ném ra khi việc thu hồi nhóm SignalR hoặc ngắt kết nối không thể xác minh thành công.
/// Kích hoạt cơ chế Fail-Closed trong StorageUnitRealtimePublisher để ngăn chặn rò rỉ sự kiện tới tài khoản bị hạ quyền.
/// </summary>
public class RealtimeScopeRevocationException : InvalidOperationException
{
    public RealtimeScopeRevocationException(string message) : base(message)
    {
    }

    public RealtimeScopeRevocationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
