using System;
using System.Security.Cryptography;

class Program
{
    static void Main()
    {
        var hash = "PBKDF2$SHA256$100000$f860fyp2nuCIZCGltnHPoA==$Rk9EA4vF/MvmAO+qQZaJDaAHFjNTJ+d3etMq2Unxtbs=";
        var parts = hash.Split('$');
        var salt = Convert.FromBase64String(parts[3]);
        var expectedKey = Convert.FromBase64String(parts[4]);

        string[] candidates = {
            "TestPass123!", "SecretAdminPassword123!", "Admin@123", "Password123!",
            "@a", "123456", "admin", "admin123", "Demo123!", "Pass@word1"
        };
        foreach (var c in candidates)
        {
            var actualKey = Rfc2898DeriveBytes.Pbkdf2(c, salt, 100000, HashAlgorithmName.SHA256, 32);
            if (CryptographicOperations.FixedTimeEquals(expectedKey, actualKey))
            {
                Console.WriteLine("MATCH: " + c);
                return;
            }
        }
        Console.WriteLine("NO MATCH");

        // Also check admin@selfstorage.local hash:
        var adminHash = "PBKDF2$SHA256$100000$BbvHrzX0WecDIHTElyXr5g==$TA+9Zrg6xaGwOb5aTU8PYCFJEsW+0hKPBmgrw4/eGXg=";
        var adminParts = adminHash.Split('$');
        var adminSalt = Convert.FromBase64String(adminParts[3]);
        var adminExpKey = Convert.FromBase64String(adminParts[4]);
        foreach (var c in candidates)
        {
            var actualKey = Rfc2898DeriveBytes.Pbkdf2(c, adminSalt, 100000, HashAlgorithmName.SHA256, 32);
            if (CryptographicOperations.FixedTimeEquals(adminExpKey, actualKey))
            {
                Console.WriteLine("ADMIN MATCH: " + c);
                return;
            }
        }
        Console.WriteLine("ADMIN NO MATCH");
    }
}
