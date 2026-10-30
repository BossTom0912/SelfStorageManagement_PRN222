using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_ShouldGenerateValidPBKDF2Format()
    {
        var password = "SecurePassword123!";
        var hash = _hasher.HashPassword(password);

        Assert.NotNull(hash);
        Assert.StartsWith("PBKDF2$SHA256$100000$", hash);
        var parts = hash.Split('$');
        Assert.Equal(5, parts.Length);
    }

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ShouldReturnTrue()
    {
        var password = "MyP@ssw0rd!2026";
        var hash = _hasher.HashPassword(password);

        var isValid = _hasher.VerifyPassword(hash, password);

        Assert.True(isValid);
    }

    [Fact]
    public void VerifyPassword_WithWrongPassword_ShouldReturnFalse()
    {
        var password = "CorrectPassword123!";
        var wrongPassword = "WrongPassword123!";
        var hash = _hasher.HashPassword(password);

        var isValid = _hasher.VerifyPassword(hash, wrongPassword);

        Assert.False(isValid);
    }

    [Fact]
    public void VerifyPassword_WithExternalAuthDemoOnlyPlaceholder_ShouldReturnFalseSafely()
    {
        var placeholderHash = "external-auth-demo-only";

        var isValid = _hasher.VerifyPassword(placeholderHash, "anypassword");

        Assert.False(isValid);
    }

    [Fact]
    public void VerifyPassword_WithTamperedHash_ShouldReturnFalse()
    {
        var hash = _hasher.HashPassword("TestPass123");
        var tamperedHash = hash.Substring(0, hash.Length - 4) + "AAAA";

        var isValid = _hasher.VerifyPassword(tamperedHash, "TestPass123");

        Assert.False(isValid);
    }
}
