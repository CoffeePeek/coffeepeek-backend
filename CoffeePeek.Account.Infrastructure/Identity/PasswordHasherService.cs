using System.Security.Cryptography;
using CoffeePeek.Account.Domain.Services;

namespace CoffeePeek.Account.Infrastructure.Identity;

/// <summary>Base64(salt || PBKDF2-HMACSHA256(UTF-8 password)); the format of every stored hash.</summary>
public class PasswordHasherService : IPasswordHasherService
{
    private const int SaltSize = 16; // 128 bit
    private const int KeySize = 32; // 256 bit
    private const int Iterations = 100_000;

    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return Convert.ToBase64String([.. salt, .. DeriveKey(password, salt)]);
    }

    public bool VerifyPassword(string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrWhiteSpace(hashedPassword))
            throw new ArgumentException("Hashed password cannot be empty", nameof(hashedPassword));
        if (string.IsNullOrWhiteSpace(providedPassword))
            throw new ArgumentException("Provided password cannot be empty", nameof(providedPassword));

        var hashBytes = Convert.FromBase64String(hashedPassword);
        if (hashBytes.Length != SaltSize + KeySize)
            return false;

        var stored = hashBytes.AsSpan();
        return CryptographicOperations.FixedTimeEquals(stored[SaltSize..], DeriveKey(providedPassword, stored[..SaltSize]));
    }

    private static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
}
