using PortwayApi.Classes;

namespace PortwayApi.Interfaces;

public interface IEnvironmentSettingsProvider
{
    Task<(string ConnectionString, string ServerName, Dictionary<string, string> Headers)> LoadEnvironmentOrThrowAsync(string env);
    Task<EnvironmentConfig?> GetEnvironmentConfigAsync(string env);
    void EncryptEnvironmentIfNeeded(string envName);

    /// <summary>
    /// Returns the value of secret <paramref name="name"/> in <paramref name="env"/>, or null when it is missing, empty or disabled.
    /// </summary>
    Task<string?> GetSecretAsync(string env, string name, CancellationToken cancellationToken = default);
}