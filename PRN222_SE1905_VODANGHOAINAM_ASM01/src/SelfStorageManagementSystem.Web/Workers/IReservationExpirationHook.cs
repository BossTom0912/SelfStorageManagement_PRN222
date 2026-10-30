using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;

namespace SelfStorageManagementSystem.Web.Workers;

/// <summary>
/// Hook được kích hoạt sau khi worker giải phóng các đơn giữ chỗ quá hạn 15 phút.
/// Chuẩn bị sẵn cho Task T15 để phát thông báo realtime (CatalogInvalidated, ReservationChanged qua SignalR).
/// </summary>
public interface IReservationExpirationHook
{
    /// <summary>
    /// Xử lý hậu kỳ sau khi giải phóng thành công các đơn giữ chỗ quá hạn.
    /// </summary>
    /// <param name="expiredCount">Số lượng đơn giữ chỗ đã được chuyển trạng thái sang expired.</param>
    /// <param name="cancellationToken">CancellationToken cho tác vụ.</param>
    Task OnHoldsExpiredAsync(int expiredCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xử lý hậu kỳ với danh sách định danh chi tiết các đơn giữ chỗ đã giải phóng thành công (T15 R2 Fix F02).
    /// Mặc định ủy nhiệm sang OnHoldsExpiredAsync để đảm bảo tương thích ngược hoàn toàn.
    /// </summary>
    Task OnHoldsExpiredWithDetailsAsync(
        IReadOnlyList<ExpiredReservationHoldInfo> expiredHolds,
        CancellationToken cancellationToken = default)
    {
        return OnHoldsExpiredAsync(expiredHolds.Count, cancellationToken);
    }
}

/// <summary>
/// Triển khai mặc định (No-Op) của IReservationExpirationHook trước khi T15 tích hợp SignalR realtime.
/// </summary>
public class NullReservationExpirationHook : IReservationExpirationHook
{
    public Task OnHoldsExpiredAsync(int expiredCount, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task OnHoldsExpiredWithDetailsAsync(
        IReadOnlyList<ExpiredReservationHoldInfo> expiredHolds,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
