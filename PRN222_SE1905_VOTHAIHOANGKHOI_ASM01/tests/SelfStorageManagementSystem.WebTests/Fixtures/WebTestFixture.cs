using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.DataAccess.Context;

namespace SelfStorageManagementSystem.WebTests.Fixtures;

public class WebTestFixture : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove existing SQL Server registration for tests
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<SelfStorageDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Use in-memory DB for pure web host tests
            services.AddDbContext<SelfStorageDbContext>(options =>
            {
                options.UseInMemoryDatabase($"SelfStorage_WebTests_{_dbName}");
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Register test authentication scheme that responds to X-Test-Role and X-Test-User headers
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}
