using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Serilog;

namespace PortwayApi.Services.Configuration;

public static class ConfigurationServiceExtensions
{
    public static IServiceCollection AddPortwayConfigurationReload(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ReloadTracker>();
        services.AddSingleton<ConfigAuditService>();
        services.AddSingleton<SettingsWriteService>();
        services.AddHostedService<ConfigurationReloadService>();
        services.AddHostedService<EnvironmentFileWatcher>();
        services.AddHostedService<EndpointFileWatcher>();
        services.Configure<EndpointReloadingOptions>(configuration.GetSection("EndpointReloading"));
        return services;
    }

    /// <summary>
    /// Adds appsettings.overrides.json with lower precedence than unprefixed environment variables
    /// </summary>
    public static IConfigurationBuilder AddConsoleOverrides(this IConfigurationBuilder config)
    {
        var source = new ConsoleOverridesConfigurationSource
        {
            Path = SettingsWriteService.OverridesFileName,
            Optional = true,
            ReloadOnChange = true
        };

        var envIndex = -1;
        for (var i = 0; i < config.Sources.Count; i++)
        {
            if (config.Sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
                envIndex = i;
        }

        if (envIndex < 0) config.Sources.Add(source);
        else config.Sources.Insert(envIndex, source);
        return config;
    }

    /// <summary>
    /// Validates writable settings from every source; deployment keys and cross-setting rules fail the start.
    /// </summary>
    public static void ValidateConsoleSettings(this IConfiguration configuration)
    {
        var values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in SettingsWriteService.Schema)
        {
            var section = configuration.GetSection(spec.Key);
            var children = section.GetChildren().ToList();
            if (section.Value is null && children.Count == 0) continue;

            // empty hosts allow every host
            if (spec.Kind == SettingKind.HostList && section.Value is "") continue;

            // operator files may use absolute paths
            if (spec.Kind is SettingKind.RelPath or SettingKind.DbFile)
            {
                values[spec.Key] = section.Value;
                continue;
            }
            if (spec.Kind is SettingKind.Secret or SettingKind.Clear) continue;

            // unparsable values cannot bind
            if (ToJson(spec.Kind, section.Value, children) is not { } raw)
                throw new InvalidOperationException($"Configuration key {spec.Key} is invalid: '{section.Value}' is not a valid {spec.Kind.ToString().ToLowerInvariant()}");

            var (node, error) = SettingsWriteService.Coerce(spec.Key, raw);

            if (error is null)
            {
                values[spec.Key] = node;
                continue;
            }

            if (spec.Kind is SettingKind.Route or SettingKind.PathBase or SettingKind.HostList or SettingKind.OriginList or SettingKind.IpList or SettingKind.CidrList)
                throw new InvalidOperationException($"Configuration key {spec.Key} is invalid: {error}");

            Log.Warning("Configuration key {Key} is outside the console range: {Reason}", spec.Key, error);
        }

        if (SettingsWriteService.CrossCheck(values, configuration) is { } cross)
            throw new InvalidOperationException($"Configuration key {cross.Field} is invalid: {cross.Message}");
    }

    private static JsonElement? ToJson(SettingKind kind, string? value, List<IConfigurationSection> children)
    {
        JsonNode? node = kind switch
        {
            SettingKind.IntMap =>
                new JsonObject(children.Select(c => KeyValuePair.Create(c.Key, (JsonNode?)(long.TryParse(c.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? JsonValue.Create(n) : JsonValue.Create(c.Value))))),
            SettingKind.LevelMap => new JsonObject(children.Select(c => KeyValuePair.Create(c.Key, (JsonNode?)JsonValue.Create(c.Value)))),
            _ when SettingsWriteService.IsCollection(kind) => new JsonArray([.. children.Select(c => (JsonNode?)JsonValue.Create(c.Value))]),
            SettingKind.HostList => new JsonArray([.. (value ?? "").Split(';').Select(h => (JsonNode?)JsonValue.Create(h))]),
            SettingKind.Bool => bool.TryParse(value, out var b) ? JsonValue.Create(b) : null,
            SettingKind.Int => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? JsonValue.Create(i) : null,
            SettingKind.Long => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? JsonValue.Create(l) : null,
            SettingKind.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? JsonValue.Create(d) : null,
            _ => JsonValue.Create(value),
        };
        return node is null ? null : JsonSerializer.SerializeToElement(node);
    }
}
