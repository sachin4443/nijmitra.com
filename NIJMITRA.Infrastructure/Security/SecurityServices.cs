using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NIJMITRA.Application.Abstractions;
using OtpNet;
using QRCoder;

namespace NIJMITRA.Infrastructure.Security;

public class SecurityOptions
{
    /// <summary>Base64 encoded 32-byte master key for AES-256-GCM field encryption. Supplied via env/secrets only.</summary>
    public string DataEncryptionKey { get; set; } = "";
}

/// <summary>PBKDF2 (HMAC-SHA512, 100k iterations) via ASP.NET Core Identity's PasswordHasher.</summary>
public sealed class PasswordHasherService : IPasswordHasherService
{
    private static readonly PasswordHasher<object> Hasher = new(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = 210_000
    }));
    private static readonly object Subject = new();

    public string Hash(string secret) => Hasher.HashPassword(Subject, secret);

    public bool Verify(string hash, string secret, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(hash)) return false;
        var r = Hasher.VerifyHashedPassword(Subject, hash, secret);
        needsRehash = r == PasswordVerificationResult.SuccessRehashNeeded;
        return r != PasswordVerificationResult.Failed;
    }
}

/// <summary>
/// AES-256-GCM authenticated encryption for server-side secrets (TOTP secrets, provider API keys,
/// diary content, queued message bodies). Format: "v1:" + base64(nonce[12] | tag[16] | ciphertext).
/// A separate HMAC key is derived with HKDF for deterministic lookup hashes.
/// </summary>
public sealed class FieldEncryptor : IFieldEncryptor
{
    private readonly byte[] _encKey;
    private readonly byte[] _macKey;

    public FieldEncryptor(IOptions<SecurityOptions> options)
    {
        var raw = options.Value.DataEncryptionKey;
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Security:DataEncryptionKey is not configured. Provide a base64 32-byte key via environment variable NIJMITRA_Security__DataEncryptionKey.");
        var master = Convert.FromBase64String(raw);
        if (master.Length != 32) throw new InvalidOperationException("Security:DataEncryptionKey must be exactly 32 bytes (base64).");
        _encKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: Encoding.UTF8.GetBytes("nijmitra-field-enc-v1"));
        _macKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: Encoding.UTF8.GetBytes("nijmitra-lookup-mac-v1"));
    }

    public string Encrypt(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[data.Length];
        using (var aes = new AesGcm(_encKey, 16)) aes.Encrypt(nonce, data, cipher, tag);
        var output = new byte[12 + 16 + cipher.Length];
        nonce.CopyTo(output, 0); tag.CopyTo(output, 12); cipher.CopyTo(output, 28);
        return "v1:" + Convert.ToBase64String(output);
    }

    public string Decrypt(string ciphertext)
    {
        if (!ciphertext.StartsWith("v1:")) throw new CryptographicException("Unknown ciphertext format.");
        var bytes = Convert.FromBase64String(ciphertext[3..]);
        var nonce = bytes.AsSpan(0, 12);
        var tag = bytes.AsSpan(12, 16);
        var cipher = bytes.AsSpan(28);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_encKey, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public string Hash(string value) =>
        Convert.ToHexString(HMACSHA256.HashData(_macKey, Encoding.UTF8.GetBytes(value)));
}

/// <summary>RFC 6238 TOTP (SHA-1, 6 digits, 30 s step) compatible with Google/Microsoft Authenticator.</summary>
public sealed class TotpService : ITotpService
{
    public string GenerateSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public string BuildOtpAuthUri(string secret, string accountName, string issuer) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";

    public string QrCodeDataUri(string content)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    public long? Verify(string secret, string code, long lastUsedStep)
    {
        code = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (code.Length != 6) return null;
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        if (!totp.VerifyTotp(code, out var step, new VerificationWindow(previous: 1, future: 1))) return null;
        return step <= lastUsedStep ? null : step; // reject replay of an already-used code
    }
}
