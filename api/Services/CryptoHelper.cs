using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using api.Options;

namespace api.Services;

// Encrypt/Decrypt: reversible AES-256-GCM, used for values we need back in plaintext (e.g. JWT claims).
// Hash/VerifyPassword: one-way PBKDF2, used for passwords — never reversible.
public class CryptoHelper
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Pbkdf2Iterations = 100_000;

    private readonly byte[] _key;

    public CryptoHelper(IOptions<CryptoOptions> options)
    {
        var configuredKey = options.Value.Key;
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException(
                "Crypto:Key is not configured. Set it via 'dotnet user-secrets set \"Crypto:Key\" \"<base64-32-bytes>\"'.");
        }

        _key = Convert.FromBase64String(configuredKey);
        if (_key.Length != 32)
        {
            throw new InvalidOperationException("Crypto:Key must decode to exactly 32 bytes (AES-256).");
        }
    }

    public string Encrypt(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        var result = new byte[NonceSize + cipherBytes.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(cipherBytes, 0, result, NonceSize, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSize + cipherBytes.Length, TagSize);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertextBase64)
    {
        var data = Convert.FromBase64String(ciphertextBase64);

        var nonce = data[..NonceSize];
        var cipherBytes = data[NonceSize..^TagSize];
        var tag = data[^TagSize..];
        var plainBytes = new byte[cipherBytes.Length];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Pbkdf2Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        var parts = hashedPassword.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var expectedHash = Convert.FromBase64String(parts[2]);

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
