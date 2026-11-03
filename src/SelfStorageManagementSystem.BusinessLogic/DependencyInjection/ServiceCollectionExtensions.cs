using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.DependencyInjection;

namespace SelfStorageManagementSystem.BusinessLogic.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all core domain services that do not depend on JWT token generation or secrets.
    /// </summary>
    public static IServiceCollection AddBusinessLogicCore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, PasswordHasherKhoiVTH>();
        services.AddScoped<IAuditService, AuditServiceKhoiVTH>();
        services.AddScoped<IFacilityScopeService, FacilityScopeServiceKhoiVTH>();
        services.AddScoped<IAdminAccountService, AdminAccountServiceKhoiVTH>();
        services.AddScoped<IDemoAccountBootstrapService, DemoAccountBootstrapServiceKhoiVTH>();
        services.AddScoped<IFacilityCatalogService, FacilityCatalogServiceKhoiVTH>();
        services.AddScoped<IReservationService, ReservationServiceKhoiVTH>();
        services.AddScoped<DemoGatewayKhoiVTH>();
        services.AddScoped<VnpayGatewayKhoiVTH>();
        services.AddScoped<IPaymentService, PaymentServiceKhoiVTH>();
        services.AddScoped<IInvoiceService, InvoiceServiceKhoiVTH>();
        services.AddScoped<IRentalAgreementService, RentalAgreementServiceKhoiVTH>();
        services.AddScoped<ICheckInService, CheckInServiceKhoiVTH>();
        services.AddScoped<IAccountIdentityService, AccountIdentityServiceKhoiVTH>();
        services.AddScoped<IStorageUnitKhoiVTHService, StorageUnitServiceKhoiVTH>();
        services.AddScoped<IStorageUnitAdminService>(sp => sp.GetRequiredService<IStorageUnitKhoiVTHService>());
        services.AddScoped<IUserScopeChangedNotifier, NullUserScopeChangedNotifierKhoiVTH>();

        return services;
    }

    /// <summary>
    /// Registers services specific to JWT authentication (used by API host).
    /// Requires JwtOptions / JWT secret configuration when resolved.
    /// </summary>
    public static IServiceCollection AddBusinessLogicJwt(this IServiceCollection services)
    {
        services.AddScoped<IJwtTokenGenerator, JwtTokenGeneratorKhoiVTH>();
        services.AddScoped<IAuthService, AuthServiceKhoiVTH>();
        return services;
    }

    /// <summary>
    /// Legacy composition entrypoint for existing API host and existing regression tests.
    /// Combines core domain services and JWT authentication services.
    /// </summary>
    public static IServiceCollection AddBusinessLogic(this IServiceCollection services)
    {
        services.AddBusinessLogicCore();
        services.AddBusinessLogicJwt();
        return services;
    }

    /// <summary>
    /// Composition entrypoint for web application hosts (MvcWebApp).
    /// Directly registers DataAccess and BusinessLogicCore without exposing DataAccess references to Web,
    /// and strictly avoids registering JWT dependencies so that Web does not require a JWT secret.
    /// </summary>
    public static IServiceCollection AddSelfStorageWebComposition(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDataAccess(configuration);
        services.AddBusinessLogicCore();
        return services;
    }
}
