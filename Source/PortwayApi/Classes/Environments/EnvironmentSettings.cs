namespace PortwayApi.Classes;

using System.Text.Json;
using Serilog;

public class EnvironmentSettings
{
    private readonly List<string> _allowedEnvironments = new List<string>();
    private readonly string _settingsPath;
    private string _serverName = ".";

    public List<string> AllowedEnvironments => _allowedEnvironments.ToList();
    public string ServerName => _serverName;

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
                
                if (settings?.Environment?.AllowedEnvironments != null)
                {
                    _allowedEnvironments.Clear();
                    _allowedEnvironments.AddRange(settings.Environment.AllowedEnvironments);
                }

                if (settings?.Environment?.ServerName != null)
                {
                    _serverName = settings.Environment.ServerName;
                }

                Log.Information("✅ Loaded environments: {AllowedEnvironments}", string.Join(", ", _allowedEnvironments));
                Log.Information("✅ Using server: {ServerName}", _serverName);
            }
            else
            {
                // Create default settings file
                var defaultSettings = new SettingsModel
                {
                    Environment = new EnvironmentModel
                    {
                        ServerName = ".",
                        AllowedEnvironments = new List<string> { "600", "700" }
                    }
                };
                
                _allowedEnvironments.AddRange(defaultSettings.Environment.AllowedEnvironments);
                _serverName = defaultSettings.Environment.ServerName;
                
                var json = JsonSerializer.Serialize(defaultSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                
                Log.Warning("⚠️ settings.json not found. Created with defaults.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error loading environment settings: {ErrorMessage}", ex.Message);
        }
    }

    public bool IsEnvironmentAllowed(string environment)
    {
        return _allowedEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase);
    }

    public List<string> GetAllowedEnvironments()
    {
        return _allowedEnvironments.ToList();
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