using Microsoft.AspNetCore.Authorization;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Pages.Reservations;

[Authorize(Policy = "CustomerOnly")]
public class MyReservationsModel : IndexModel
{
    public MyReservationsModel(
        IReservationService reservationService,
        ILogger<IndexModel> logger)
        : base(reservationService, logger)
    {
    }
}
