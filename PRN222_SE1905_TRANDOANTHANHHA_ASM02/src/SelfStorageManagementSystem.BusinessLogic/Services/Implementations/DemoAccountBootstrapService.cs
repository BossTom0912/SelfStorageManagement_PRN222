using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class DemoAccountBootstrapService : IDemoAccountBootstrapService
{
    private readonly SelfStorageDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DemoAccountBootstrapService> _logger;

    private const string PlaceholderHash = "external-auth-demo-only";

    public DemoAccountBootstrapService(
        SelfStorageDbContext context,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ILogger<DemoAccountBootstrapService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task BootstrapDemoAccountsAsync(CancellationToken cancellationToken = default)
    {
        var adminEmail = _configuration["AdminAccount:Email"];

        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            throw new InvalidOperationException(
                "Admin account identity configuration is missing. " +
                "Please configure 'AdminAccount:Email' in appsettings.json or configuration.");
        }

        var adminUser = await _context.users
            .Include(u => u.user_roleusers)
                .ThenInclude(ur => ur.role)
            .FirstOrDefaultAsync(u => u.email == adminEmail, cancellationToken);

        if (adminUser == null)
        {
            throw new InvalidOperationException(
                $"Admin user with email '{adminEmail}' specified in 'AdminAccount:Email' does not exist in the database.");
        }

        var hasAdminRole = adminUser.user_roleusers
            .Any(ur => ur.role != null && ur.role.code == RoleConstants.SystemAdministrator);

        if (!hasAdminRole)
        {
            throw new InvalidOperationException(
                $"Configured admin user '{adminEmail}' exists in the database but does not have the required role '{RoleConstants.SystemAdministrator}'.");
        }

        var adminPassword = _configuration["AdminAccount:Password"]
                            ?? Environment.GetEnvironmentVariable("ADMIN_ACCOUNT_PASSWORD");

        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            throw new InvalidOperationException(
                "Admin account bootstrapping is enabled, but admin password is not configured. " +
                "Please configure a password via User Secrets ('dotnet user-secrets set \"AdminAccount:Password\" \"<password>\"') " +
                "or the 'ADMIN_ACCOUNT_PASSWORD' environment variable (never commit passwords to appsettings.json).");
        }

        var validAdminHash = _passwordHasher.HashPassword(adminPassword);
        adminUser.password_hash = validAdminHash;
        adminUser.updated_at = DateTimeOffset.UtcNow;
        _logger.LogInformation("[Bootstrap] Verified role '{Role}' and updated password hash for admin account '{Email}'.", RoleConstants.SystemAdministrator, adminEmail);

        var demoUsersWithPlaceholderHash = await _context.users
            .Where(u => u.id != adminUser.id && u.password_hash == PlaceholderHash)
            .ToListAsync(cancellationToken);

        if (demoUsersWithPlaceholderHash.Count > 0)
        {
            var demoPassword = _configuration["DemoAccounts:DefaultPassword"]
                               ?? Environment.GetEnvironmentVariable("DEMO_DEFAULT_PASSWORD")
                               ?? adminPassword;

            var validDemoHash = _passwordHasher.HashPassword(demoPassword);
            foreach (var user in demoUsersWithPlaceholderHash)
            {
                user.password_hash = validDemoHash;
                user.updated_at = DateTimeOffset.UtcNow;
                _logger.LogInformation("[Bootstrap] Replaced placeholder hash for demo user '{Email}'", user.email);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[Bootstrap] Successfully bootstrapped admin and {Count} demo accounts.", demoUsersWithPlaceholderHash.Count);
    }
}
