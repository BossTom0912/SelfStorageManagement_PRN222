using System.ComponentModel.DataAnnotations;

namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Reservations;

public class CreateReservationRequest
{
    [Required(ErrorMessage = "Mã cơ sở (FacilityId) là bắt buộc.")]
    [Range(1, long.MaxValue, ErrorMessage = "Mã cơ sở phải lớn hơn 0.")]
    public long FacilityId { get; set; }

    [Required(ErrorMessage = "Mã loại kho (UnitTypeId) là bắt buộc.")]
    [Range(1, long.MaxValue, ErrorMessage = "Mã loại kho phải lớn hơn 0.")]
    public long UnitTypeId { get; set; }

    [Required(ErrorMessage = "Ngày bắt đầu thuê là bắt buộc.")]
    public DateOnly RentalStartDate { get; set; }

    [Required(ErrorMessage = "Ngày kết thúc thuê là bắt buộc.")]
    public DateOnly RentalEndDate { get; set; }
}
