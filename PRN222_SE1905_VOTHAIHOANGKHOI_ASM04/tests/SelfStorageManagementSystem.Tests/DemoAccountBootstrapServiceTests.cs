using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class DemoAccountBootstrapServiceTests
{
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<ILogger<DemoAccountBootstrapService>> _loggerMock = new();

    private SelfStorageDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SelfStorageDbContext(options);
    }

    [Fact]
    public async Task BootstrapDemoAccountsAsync_WhenPasswordNotConfigured_ShouldThrowInvalidOperationException()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        context.users.Add(new user
        {
            id = 1,
            email = "demo@example.test",
            password_hash = "external-auth-demo-only",
            status = UserStatusConstants.Active
        });
        await context.SaveChangesAsync();

        var inMemoryConfig = new Dictionary<string, string?>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var service = new DemoAccountBootstrapService(
            context,
            _passwordHasherMock.Object,
            configuration,
            _loggerMock.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BootstrapDemoAccountsAsync());

        Assert.Contains("DemoAccounts:DefaultPassword", ex.Message);
    }

    [Fact]
    public async Task BootstrapDemoAccountsAsync_WhenPasswordConfigured_ShouldUpdatePlaceholderHashes()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        context.users.Add(new user
        {
            id = 1,
            email = "demo@example.test",
            password_hash = "external-auth-demo-only",
            status = UserStatusConstants.Active
        });
        await context.SaveChangesAsync();

        _passwordHasherMock.Setup(h => h.HashPassword("ConfiguredSecurePassword123!"))
            .Returns("hashed_configured_pw");

        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "DemoAccounts:DefaultPassword", "ConfiguredSecurePassword123!" }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var service = new DemoAccountBootstrapService(
            context,
            _passwordHasherMock.Object,
            configuration,
            _loggerMock.Object);

        await service.BootstrapDemoAccountsAsync();

        var user = await context.users.FindAsync((long)1);
        Assert.NotNull(user);
        Assert.Equal("hashed_configured_pw", user.password_hash);
    }
}
