namespace PortwayApi.Interfaces;

public interface IEnvironmentSettingsProvider
{
    Task<(string ConnectionString, string ServerName, Dictionary<string, string> Headers)> LoadEnvironmentOrThrowAsync(string env);
}