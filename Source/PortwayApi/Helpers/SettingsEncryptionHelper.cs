// Helper for encryption detection and decryption
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PortwayApi.Helpers
{
    public static class SettingsEncryptionHelper
    {
        private const string EncryptedHeader = "PWENC:";
        private static string? _currentPublicKeyPem = null;

        public static bool IsEncrypted(string content)
        {
            return content.StartsWith(EncryptedHeader);
        }

        // Get the current public key (from file, private key, or fallback to hardcoded)
        private static string GetCurrentPublicKey()
        {
            if (_currentPublicKeyPem != null)
                return _currentPublicKeyPem;

            // Try to read public key directly from file first
            var certsPath = Path.Combine(Directory.GetCurrentDirectory(), "certs");
            var publicKeyPath = Path.Combine(certsPath, "key_a.pem");
            
            if (File.Exists(publicKeyPath))
            {
                try
                {
                    _currentPublicKeyPem = File.ReadAllText(publicKeyPath);
                    return _currentPublicKeyPem;
                }
                catch
                {
                    // Continue to next option if error
                }
            }

            // Try to derive public key from private key in certs directory
            var privateKeyPath = Path.Combine(certsPath, "key_b.pem");
            
            if (File.Exists(privateKeyPath))
            {
                try
                {
                    var privateKeyPem = File.ReadAllText(privateKeyPath);
                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(privateKeyPem);
                    var publicKeyPem = ExportPublicKeyPem(rsa);
                    _currentPublicKeyPem = publicKeyPem;
                    
                    // Save the derived public key for next time
                    File.WriteAllText(publicKeyPath, publicKeyPem);
                    
                    return publicKeyPem;
                }
                catch
                {
                    // Fall back to hardcoded key if error
                }
            }
            
            // Fallback to hardcoded public key
            _currentPublicKeyPem = SettingsEncryptionKeys.PublicKeyPem;
            return _currentPublicKeyPem;
        }

        private static string ExportPublicKeyPem(RSA rsa)
        {
            var builder = new StringBuilder();
            builder.AppendLine("-----BEGIN PUBLIC KEY-----");
            builder.AppendLine(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo(), Base64FormattingOptions.InsertLineBreaks));
            builder.AppendLine("-----END PUBLIC KEY-----");
            return builder.ToString();
        }

        // Hybrid encryption: AES for content, RSA for AES key/IV
        public static string Encrypt(string plainText)
        {
            // Generate AES key/IV
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.GenerateKey();
            aes.GenerateIV();
            var keyIv = new byte[aes.Key.Length + aes.IV.Length];
            Buffer.BlockCopy(aes.Key, 0, keyIv, 0, aes.Key.Length);
            Buffer.BlockCopy(aes.IV, 0, keyIv, aes.Key.Length, aes.IV.Length);

            // Encrypt content with AES
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes;
            using (var ms = new MemoryStream())
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(plainBytes, 0, plainBytes.Length);
                cs.FlushFinalBlock();
                cipherBytes = ms.ToArray();
            }

            // Encrypt AES key/IV with RSA
            using var rsa = RSA.Create();
            rsa.ImportFromPem(GetCurrentPublicKey());
            var encryptedKeyIv = rsa.Encrypt(keyIv, RSAEncryptionPadding.OaepSHA256);

            // Format: PWENC:<base64(RSA_AES_KEY_IV)>::<base64(AES_CIPHERTEXT)>
            return EncryptedHeader + Convert.ToBase64String(encryptedKeyIv) + "::" + Convert.ToBase64String(cipherBytes);
        }

        public static string Decrypt(string encryptedContent, string privateKeyPem)
        {
            if (!IsEncrypted(encryptedContent))
                throw new InvalidOperationException("Content is not encrypted");
            var payload = encryptedContent.Substring(EncryptedHeader.Length);
            var parts = payload.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length != 2)
                throw new FormatException("Invalid encrypted format");
            var encryptedKeyIv = Convert.FromBase64String(parts[0]);
            var cipherBytes = Convert.FromBase64String(parts[1]);

            // Decrypt AES key/IV with RSA
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            var keyIv = rsa.Decrypt(encryptedKeyIv, RSAEncryptionPadding.OaepSHA256);
            var key = new byte[32];
            var iv = new byte[16];
            Buffer.BlockCopy(keyIv, 0, key, 0, 32);
            Buffer.BlockCopy(keyIv, 32, iv, 0, 16);

            // Decrypt content with AES
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            using var ms = new MemoryStream(cipherBytes);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            return sr.ReadToEnd();
        }
    }
}
