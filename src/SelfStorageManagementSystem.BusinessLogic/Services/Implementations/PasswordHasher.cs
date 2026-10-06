using System.Security.Cryptography;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16; // 128-bit
    private const int KeySize = 32;  // 256-bit
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            Algorithm,
            KeySize);

        return $"PBKDF2$SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool VerifyPassword(string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrWhiteSpace(hashedPassword) || string.IsNullOrWhiteSpace(providedPassword))
        {
            return false;
        }

        var parts = hashedPassword.Split('$');
        if (parts.Length != 5 || parts[0] != "PBKDF2" || parts[1] != "SHA256")
        {
            return false;
        }

        if (!int.TryParse(parts[2], out int iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(parts[3]);
            byte[] expectedKey = Convert.FromBase64String(parts[4]);

            byte[] actualKey = Rfc2898DeriveBytes.Pbkdf2(
                providedPassword,
                salt,
                iterations,
                Algorithm,
                expectedKey.Length);

            return CryptographicOperations.FixedTimeEquals(expectedKey, actualKey);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
