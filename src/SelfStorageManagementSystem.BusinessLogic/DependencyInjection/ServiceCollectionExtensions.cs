using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessLogic(
        this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IFacilityScopeService, FacilityScopeService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();
        services.AddScoped<IDemoAccountBootstrapService, DemoAccountBootstrapService>();
        services.AddScoped<IFacilityCatalogService, FacilityCatalogService>();

        return services;
    }
}
