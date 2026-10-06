using Microsoft.Extensions.Options;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.DataAccess.Entities;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class JwtTokenGeneratorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short-key")]
    [InlineData("1234567890123456789012345678901")] // 31 characters
    public void Constructor_WithMissingOrShortKey_ShouldThrowInvalidOperationException(string invalidKey)
    {
        var options = Options.Create(new JwtOptions { Key = invalidKey });

        var ex = Assert.Throws<InvalidOperationException>(() => new JwtTokenGenerator(options));
        Assert.Contains("JWT signing key is missing or shorter than", ex.Message);
    }

    [Fact]
    public void GenerateToken_WithValidKey_ShouldGenerateValidTokenString()
    {
        var validKey = "ThisIsAValid32ByteKeyForSigning!"; // exactly 32 bytes
        var options = Options.Create(new JwtOptions
        {
            Key = validKey,
            Issuer = "SelfStoragePRN222",
            Audience = "SelfStoragePRN222Clients",
            ExpiryMinutes = 60
        });

        var generator = new JwtTokenGenerator(options);
        var user = new user
        {
            id = 100,
            email = "user100@example.test"
        };

        var (token, expiresAt) = generator.GenerateToken(user, new[] { RoleConstants.StorageCustomer });

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(expiresAt > DateTimeOffset.UtcNow);
    }
}
