using System;
using System.Security.Cryptography;

class Program {
    static void Main() {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2("12345", salt, 100000, HashAlgorithmName.SHA256, 32);
        string hash = $"PBKDF2$SHA256$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        Console.WriteLine("HASH:" + hash);
    }
}
