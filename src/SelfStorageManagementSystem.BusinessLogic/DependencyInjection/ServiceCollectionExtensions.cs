using Microsoft.Extensions.DependencyInjection;

namespace SelfStorageManagementSystem.BusinessLogic.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessLogic(
        this IServiceCollection services)
    {
        // Feature services (e.g. ReservationService, PaymentService) will be registered here as implemented.
        return services;
    }
}
