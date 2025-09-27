using EncryptTool;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using Serilog;

namespace EncryptTool;
class Program
{
    private const string EncryptedHeader = "PWENC:";

    // Dynamic public key - will be loaded from private key or generated
    private static string _currentPublicKeyPem = PublicKeyPem;
    
    // Fixed public key (fallback)
    private const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAyz9H4bRMXhB5x4ure6kt
nkSspl8i/ub3TRoaFh83CW5LiFSdq/F5UodlEvloaP5jIXjNA+sKqRSgeytWy2pn
t/ACYrckVLEx6t2qVqE5X48E3KJDoIFy3RUy5fUt3aQP2xHLX/GQ/i/AtZ9Z3IUI
7tBm4qHUDiwBHbVU62u6issYR1smp9m0xGVj/uQSu+X8bUtUTRQu3NNjsCTVNIS+
FfTu11I+dyL7LxcwASihI2F4l8HbMfXgAEv0ap7AdalwLHwOuSA8HaZ9T2BG28Y/
0xnSxRzGHMZIE5UcWmJxEN4VoSzR11UgjP0z+tjKg98YMfXJEAYLnC1PyNpaUmPR
3a0X0mVEYZR26SLN6kzDXnUIb6cJY+maz/djadpiPVqywX9GSJswLXGHxisER1e7
R6W/tfbcnx5irmQ/gbxGdY8z65hCXWiFJ93gvc107hidk4kDdRw2iqFa5RZE8PJC
8/2oeAg8kPsNZFOYbkOiIQOjQ+KnDUvywk0GENyrJbsiSsgi8yv2cDr20vrqq+ta
DRwIdm+l6WAP1AUQtcLJcE66kJrJCRQTdDT4M4F129V1rjbV5Lj3Umtwg5ca89Y5
H/l/Zr708b8Rcs3jGgzYhEwoDR7m+tO9aTtR0m3Kb2p+J5ncFBPprJEbVHP9YVex
b4f8Vz/IrxYrXxKY9IaofXECAwEAAQ==
-----END PUBLIC KEY-----";

    private const string PrivateKeyFileName = "portway_private_key.pem";

    static void Main(string[] args)
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("log/encrypt-.log", rollingInterval: Serilog.RollingInterval.Day)
            .MinimumLevel.Information()
            .CreateLogger();

        try
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintHelp();
                return;
            }

            string mode = string.Empty;
            string? envRoot = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--encrypt" || args[i] == "-e")
                    mode = "encrypt";
                else if (args[i] == "--decrypt" || args[i] == "-d")
                    mode = "decrypt";
                else if (args[i] == "--verify" || args[i] == "-v")
                    mode = "verify";
                else if ((args[i] == "--envdir" || args[i] == "-p") && i + 1 < args.Length)
                    envRoot = args[++i];
            }
            if (string.IsNullOrEmpty(mode))
            {
                PrintHelp();
                return;
            }

            // Locate Environments folder
            if (string.IsNullOrEmpty(envRoot))
            {
                var tryEnvDirInTwoUp = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Environments"));
                var tryFirst = Path.Combine("..", "PortwayApi", "Environments");
                var trySecond = Path.Combine("..", "..", "PortwayApi", "Environments");
                if (Directory.Exists(tryEnvDirInTwoUp))
                    envRoot = tryEnvDirInTwoUp;
                else if (Directory.Exists(tryFirst))
                    envRoot = Path.GetFullPath(tryFirst);
                else if (Directory.Exists(trySecond))
                    envRoot = Path.GetFullPath(trySecond);
                else
                {
                    Log.Error("Environments directory not found.");
                    return;
                }
            }
            else
            {
                envRoot = !Path.IsPathRooted(envRoot) ? Path.GetFullPath(envRoot) : envRoot;
                if (!Directory.Exists(envRoot))
                {
                    Log.Error("Environments directory not found: {EnvRoot}", envRoot);
                    return;
                }
            }

            // Certs path
            var rootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", ".."));
            var certsPath = Path.Combine(rootPath, "certs");
            var privateKeyPath = Path.Combine(certsPath, PrivateKeyFileName);

            // Initialize keypair - ensure we have matching public/private keys
            InitializeKeyPair(certsPath, privateKeyPath);

            var files = FileLookupHandler.FindFiles(envRoot, "settings.json");

            if (mode == "encrypt")
            {
                Log.Information("Encrypting files with current public key");
                foreach (var file in files)
                {
                    if (FileLookupHandler.IsRootFile(envRoot, file))
                        continue;
                    var content = File.ReadAllText(file);
                    if (content.StartsWith(EncryptedHeader))
                    {
                        Log.Information("Already encrypted: {File}", file);
                        continue;
                    }
                    var encrypted = Encrypt(content, _currentPublicKeyPem);
                    File.WriteAllText(file, encrypted);
                    Log.Information("Encrypted: {File}", file);
                }
            }
            else if (mode == "decrypt")
            {
                if (!File.Exists(privateKeyPath))
                {
                    Log.Error("❌ Private key not found at {Path}. Required for decryption.", privateKeyPath);
                    return;
                }

                var privateKey = File.ReadAllText(privateKeyPath);
                Log.Information("🔑 Using private key from {PrivateKeyPath}", privateKeyPath);

                foreach (var file in files)
                {
                    if (FileLookupHandler.IsRootFile(envRoot, file))
                        continue;
                    var content = File.ReadAllText(file);
                    if (!content.StartsWith(EncryptedHeader))
                    {
                        Log.Information("Not encrypted: {File}", file);
                        continue;
                    }
                    try
                    {
                        var decrypted = Decrypt(content, privateKey);
                        File.WriteAllText(file, decrypted);
                        Log.Information("Decrypted: {File}", file);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "❌ Failed to decrypt {File}", file);
                    }
                }
            }
            else if (mode == "verify")
            {
                Log.Information("File status:");
                foreach (var file in files)
                {
                    if (FileLookupHandler.IsRootFile(envRoot, file))
                        continue;
                    var content = File.ReadAllText(file);
                    string envName = GetEnvironmentName(envRoot, file);
                    string status = content.StartsWith(EncryptedHeader) ? "ENCRYPTED" : "PLAIN TEXT";
                    Log.Information("[{Status}] {Env,-10} {File}", status, envName, file);
                }
            }

            static string GetEnvironmentName(string envRoot, string file)
            {
                var dir = Path.GetDirectoryName(file);
                if (dir == null || Path.GetFullPath(dir).Equals(Path.GetFullPath(envRoot), StringComparison.OrdinalIgnoreCase))
                    return "ROOT";
                return new DirectoryInfo(dir).Name;
            }
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    static void InitializeKeyPair(string certsPath, string privateKeyPath)
    {
        if (!File.Exists(privateKeyPath))
        {
            Log.Information("Private key not found. Generating new keypair...");
            
            // Create certs directory
            Directory.CreateDirectory(certsPath);
            
            // Generate new keypair
            using var rsa = RSA.Create(2048);
            var privateKeyPem = ExportPrivateKeyPem(rsa);
            var publicKeyPem = ExportPublicKeyPem(rsa);
            
            // Save private key
            File.WriteAllText(privateKeyPath, privateKeyPem);
            Log.Information("Private key saved to: {PrivateKeyPath}", privateKeyPath);
            
            // Save public key to certs directory
            var publicKeyPath = Path.Combine(certsPath, "portway_public_key.pem");
            File.WriteAllText(publicKeyPath, publicKeyPem);
            Log.Information("Public key saved to: {PublicKeyPath}", publicKeyPath);
            
            // Update current public key
            _currentPublicKeyPem = publicKeyPem;
            Log.Information("Generated new keypair for this session.");
        }
        else
        {
            // Private key exists, derive public key from it
            try
            {
                var privateKeyPem = File.ReadAllText(privateKeyPath);
                using var rsa = RSA.Create();
                rsa.ImportFromPem(privateKeyPem);
                var derivedPublicKeyPem = ExportPublicKeyPem(rsa);
                _currentPublicKeyPem = derivedPublicKeyPem;
                
                // Also save/update public key file
                var publicKeyPath = Path.Combine(certsPath, "portway_public_key.pem");
                File.WriteAllText(publicKeyPath, derivedPublicKeyPem);
                
                Log.Information("Loaded existing private key and derived public key.");
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load private key: {Error}", ex.Message);
                throw;
            }
        }
    }

    // AES + RSA hybrid encryption
    static string Encrypt(string plainText, string publicKeyPem)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes;
        using (var ms = new MemoryStream())
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(plainBytes, 0, plainBytes.Length);
            cs.FlushFinalBlock();
            cipherBytes = ms.ToArray();
        }

        var keyIv = new byte[aes.Key.Length + aes.IV.Length];
        Buffer.BlockCopy(aes.Key, 0, keyIv, 0, aes.Key.Length);
        Buffer.BlockCopy(aes.IV, 0, keyIv, aes.Key.Length, aes.IV.Length);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var encryptedKeyIv = rsa.Encrypt(keyIv, RSAEncryptionPadding.OaepSHA256);

        return EncryptedHeader + Convert.ToBase64String(encryptedKeyIv) + "::" + Convert.ToBase64String(cipherBytes);
    }

    static string Decrypt(string encryptedContent, string privateKeyPem)
    {
        if (!encryptedContent.StartsWith(EncryptedHeader))
            throw new InvalidOperationException("Content is not encrypted");
        var payload = encryptedContent.Substring(EncryptedHeader.Length);
        var parts = payload.Split(new[] { "::" }, StringSplitOptions.None);
        if (parts.Length != 2)
            throw new FormatException("Invalid encrypted format");

        var encryptedKeyIv = Convert.FromBase64String(parts[0]);
        var cipherBytes = Convert.FromBase64String(parts[1]);

        var sanitizedPem = string.Join("\n",
            privateKeyPem.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(sanitizedPem);
        var keyIv = rsa.Decrypt(encryptedKeyIv, RSAEncryptionPadding.OaepSHA256);

        var key = new byte[32];
        var iv = new byte[16];
        Buffer.BlockCopy(keyIv, 0, key, 0, 32);
        Buffer.BlockCopy(keyIv, 32, iv, 0, 16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var ms = new MemoryStream(cipherBytes);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  --encrypt | -e   Encrypt all settings.json files");
        Console.WriteLine("  --decrypt | -d   Decrypt all settings.json files (requires private key in certs/)");
        Console.WriteLine("  --verify  | -v   Verify encryption status of files");
        Console.WriteLine("  --envdir  | -p   Specify environment directory root");
    }

    static string ExportPrivateKeyPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PRIVATE KEY-----");
        builder.AppendLine(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PRIVATE KEY-----");
        return builder.ToString();
    }

    static string ExportPublicKeyPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PUBLIC KEY-----");
        builder.AppendLine(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PUBLIC KEY-----");
        return builder.ToString();
    }
}
