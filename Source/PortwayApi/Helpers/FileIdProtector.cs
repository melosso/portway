using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace PortwayApi.Helpers;

/// <summary>
/// Deterministic AES-GCM file ids over environment and path, keyed from PORTWAY_ENCRYPTION_KEY via HKDF
/// </summary>
internal static class FileIdProtector
{
    internal const string Prefix = "f1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly Lazy<(byte[] Encryption, byte[] Nonce)> Keys = new(() => DeriveKeys(SettingsEncryptionHelper.LoadEncryptionKey()));

    internal static bool IsProtected(string fileId) => fileId.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string environment, string path) => Protect(environment, path, Keys.Value);

    public static bool TryUnprotect(string fileId, out string environment, out string path) => TryUnprotect(fileId, Keys.Value, out environment, out path);

    internal static (byte[] Encryption, byte[] Nonce) DeriveKeys(string secret)
    {
        var ikm = Encoding.UTF8.GetBytes(secret);
        return (
            HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, info: "portway file id encryption"u8.ToArray()),
            HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, info: "portway file id nonce"u8.ToArray()));
    }

    internal static string Protect(string environment, string path, (byte[] Encryption, byte[] Nonce) keys)
    {
        var plain = Encoding.UTF8.GetBytes($"{environment}\n{path}");
        var payload = new byte[NonceSize + plain.Length + TagSize];
        var nonce = payload.AsSpan(0, NonceSize);
        HMACSHA256.HashData(keys.Nonce, plain).AsSpan(0, NonceSize).CopyTo(nonce);

        using var aes = new AesGcm(keys.Encryption, TagSize);
        aes.Encrypt(nonce, plain, payload.AsSpan(NonceSize, plain.Length), payload.AsSpan(NonceSize + plain.Length));
        return Prefix + Base64Url.EncodeToString(payload);
    }

    internal static bool TryUnprotect(string fileId, (byte[] Encryption, byte[] Nonce) keys, out string environment, out string path)
    {
        environment = string.Empty;
        path = string.Empty;
        if (!IsProtected(fileId))
            return false;

        byte[] payload;
        try
        {
            payload = Base64Url.DecodeFromChars(fileId.AsSpan(Prefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload.Length <= NonceSize + TagSize)
            return false;

        var plain = new byte[payload.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(keys.Encryption, TagSize);
            aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize, plain.Length), payload.AsSpan(NonceSize + plain.Length), plain);
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(plain);
        var separator = text.IndexOf('\n');
        if (separator <= 0)
            return false;

        environment = text[..separator];
        path = text[(separator + 1)..];
        return true;
    }
}
