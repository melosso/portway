namespace PortwayApi.Classes;

using System.Collections.Immutable;
using System.Text.Json;
using Serilog;

public class EnvironmentSettings
{
    // Reload swaps this in one assignment so readers never see a half-rebuilt allowlist
    private sealed record Snapshot(ImmutableArray<string> AllowedEnvironments, string ServerName);

    private volatile Snapshot _snapshot = new([], ".");
    private readonly string _settingsPath;

    public List<string> AllowedEnvironments => [.. _snapshot.AllowedEnvironments];
    public string ServerName => _snapshot.ServerName;

    public EnvironmentSettings()
    {
        _settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "environments", "settings.json");
        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            var directoryName = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }                
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<SettingsModel>(json);

                var loaded = new Snapshot(
                    [.. settings?.Environment?.AllowedEnvironments ?? []],
                    settings?.Environment?.ServerName ?? _snapshot.ServerName);

                _snapshot = loaded;

                Log.Information("Loaded environments: {AllowedEnvironments}", string.Join(", ", loaded.AllowedEnvironments));
                Log.Debug("Using server: {ServerName}", loaded.ServerName);
            }
            else
            {
                // Create default settings file
                var defaultSettings = new SettingsModel
                {
                    Environment = new EnvironmentModel
                    {
                        ServerName = ".",
                        AllowedEnvironments = new List<string> { "prod", "dev" }
                    }
                };

                _snapshot = new Snapshot(
                    [.. defaultSettings.Environment.AllowedEnvironments],
                    defaultSettings.Environment.ServerName);

                var json = JsonSerializer.Serialize(defaultSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);

                Log.Warning("settings.json not found. Created with defaults.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error loading environment settings: {ErrorMessage}", ex.Message);
        }
    }

    public virtual void Reload()
    {
        LoadSettings();
    }

    public virtual bool IsEnvironmentAllowed(string environment)
    {
        return _snapshot.AllowedEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase);
    }

    public virtual List<string> GetAllowedEnvironments()
    {
        return [.. _snapshot.AllowedEnvironments];
    }
    
    private class SettingsModel
    {
        public EnvironmentModel Environment { get; set; } = new EnvironmentModel();
    }
    
    private class EnvironmentModel
    {
        public string ServerName { get; set; } = ".";
        public List<string> AllowedEnvironments { get; set; } = new List<string>();
    }
}