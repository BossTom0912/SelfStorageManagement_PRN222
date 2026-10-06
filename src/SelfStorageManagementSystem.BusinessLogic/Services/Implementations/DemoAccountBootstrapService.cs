using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
        var demoPassword = _configuration["DemoAccounts:DefaultPassword"]
                           ?? Environment.GetEnvironmentVariable("DEMO_DEFAULT_PASSWORD");

        if (string.IsNullOrWhiteSpace(demoPassword))
        {
            throw new InvalidOperationException(
                "Demo account bootstrapping is enabled, but 'DemoAccounts:DefaultPassword' is not configured. " +
                "Please configure a password via User Secrets ('dotnet user-secrets set \"DemoAccounts:DefaultPassword\" \"<password>\"') " +
                "or the 'DEMO_DEFAULT_PASSWORD' environment variable.");
        }

        var usersWithPlaceholderHash = await _context.users
            .Where(u => u.password_hash == PlaceholderHash)
            .ToListAsync(cancellationToken);

        if (usersWithPlaceholderHash.Count == 0)
        {
            _logger.LogInformation("[DemoBootstrap] All seed accounts already have active password hashes.");
            return;
        }

        var validHash = _passwordHasher.HashPassword(demoPassword);

        foreach (var user in usersWithPlaceholderHash)
        {
            user.password_hash = validHash;
            user.updated_at = DateTimeOffset.UtcNow;
            _logger.LogInformation("[DemoBootstrap] Replaced placeholder hash for demo user '{Email}'", user.email);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[DemoBootstrap] Successfully bootstrapped {Count} demo accounts with valid development password hash.", usersWithPlaceholderHash.Count);
    }
}
