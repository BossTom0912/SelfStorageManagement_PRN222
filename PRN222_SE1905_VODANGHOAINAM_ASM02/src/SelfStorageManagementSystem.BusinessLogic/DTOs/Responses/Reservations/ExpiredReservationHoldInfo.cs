namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations;

/// <summary>
/// DTO chứa định danh chi tiết của đơn đặt chỗ tạm giữ đã hết hạn được worker giải phóng.
/// Đặt tại tầng BusinessLogic để đảm bảo tính phân tầng sạch (Web -> BLL -> DAL) theo quy chuẩn kiến trúc.
/// </summary>
/// <param name="ReservationId">ID đơn đặt chỗ.</param>
/// <param name="FacilityId">ID cơ sở lưu trữ.</param>
/// <param name="CustomerUserId">ID tài khoản khách hàng sở hữu.</param>
public record ExpiredReservationHoldInfo(long ReservationId, long FacilityId, long CustomerUserId);
