using PortwayApi.Classes;

namespace PortwayApi.Interfaces;

public interface IEnvironmentSettingsProvider
{
    Task<(string ConnectionString, string ServerName, Dictionary<string, string> Headers)> LoadEnvironmentOrThrowAsync(string env);
    Task<EnvironmentConfig?> GetEnvironmentConfigAsync(string env);
    Task EncryptEnvironmentIfNeededAsync(string envName);

    Task<string?> GetSecretAsync(string env, string name, CancellationToken cancellationToken = default);
}