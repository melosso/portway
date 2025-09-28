using System.IO;

// This file contains the public key for settings encryption. Replace the value with the generated key.
namespace PortwayApi.Helpers
{
    public static class SettingsEncryptionKeys
    {
        public static string PublicKeyPem
        {
            get
            {
                var rootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", ".."));
                var publicKeyPath = Path.Combine(rootPath, ".core", "key_a.pem");
                
                if (!File.Exists(publicKeyPath))
                {
                    throw new FileNotFoundException($"Public key not found at {publicKeyPath}. Run the encryption tool first to generate keys.");
                }
                
                return File.ReadAllText(publicKeyPath);
            }
        }
    }
}
