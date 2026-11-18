namespace PortwayApi.Endpoints;

using System.Text.Json;
using System.Text.Json.Nodes;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using PortwayApi.Interfaces;

public static partial class WebUiEndpointExtensions
{
    internal sealed record SecretUse(string Environment, string Name, bool HasValue, bool InFile, bool Failed, IReadOnlyList<string> UsedBy);

    private static void MapSecretRoutes(WebApplication app)
    {
        app.MapGet("/_/api/secrets", async (HttpContext context) =>
        {
            var environments = app.Services.GetRequiredService<EnvironmentSettings>().GetAllowedEnvironments();
            var rows = await SecretsOverviewAsync(app.Services, environments, context.RequestAborted);
            return Results.Json(new
            {
                environments,
                secrets = rows.Select(r => new
                {
                    environment = r.Environment,
                    name = r.Name,
                    has_value = r.HasValue,
                    in_file = r.InFile,
                    failed = r.Failed,
                    used_by = r.UsedBy
                })
            });
        }).ExcludeFromDescription();
    }

    internal static async Task<List<SecretUse>> SecretsOverviewAsync(IServiceProvider services, IReadOnlyList<string> environments, CancellationToken cancellationToken)
    {
        var root = Directory.GetCurrentDirectory();
        var endpointsDir = Path.Combine(root, "endpoints");
        var endpointRefs = new List<(string Endpoint, IReadOnlyCollection<string> Names, List<string> Environments)>();
        if (Directory.Exists(endpointsDir))
        {
            foreach (var file in Directory.EnumerateFiles(endpointsDir, "entity.json", SearchOption.AllDirectories))
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var names = SecretReference.Names(json);
                if (names.Count > 0)
                    endpointRefs.Add((Path.GetRelativePath(endpointsDir, Path.GetDirectoryName(file)!).Replace('\\', '/'), names, AllowedEnvironmentsOf(json)));
            }
        }

        var provider = services.GetRequiredService<IEnvironmentSettingsProvider>();
        var rows = new List<SecretUse>();
        foreach (var env in environments)
        {
            var usedBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            void Use(string name, string where)
            {
                if (!usedBy.TryGetValue(name, out var list))
                    usedBy[name] = list = [];
                list.Add(where);
            }

            var stored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var envPath = Path.Combine(root, "environments", env, "settings.json");
            if (File.Exists(envPath) && TryParseObject(await File.ReadAllTextAsync(envPath, cancellationToken)) is { } settings)
            {
                if (settings["Secrets"] is JsonObject secrets)
                    foreach (var (name, value) in secrets)
                        if (value is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                            stored.Add(name);
                settings.Remove("Secrets");
                foreach (var name in SecretReference.Names(settings.ToJsonString()))
                    Use(name, "environment settings");
            }

            foreach (var (endpoint, names, allowed) in endpointRefs)
                if (allowed.Count == 0 || allowed.Contains(env, StringComparer.OrdinalIgnoreCase))
                    foreach (var name in names)
                        Use(name, endpoint);

            foreach (var name in stored.Union(usedBy.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            {
                bool hasValue, failed = false;
                try
                {
                    hasValue = await provider.GetSecretAsync(env, name, cancellationToken) is not null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    hasValue = false;
                    failed = true;
                }
                rows.Add(new SecretUse(env, name, hasValue, stored.Contains(name), failed, usedBy.GetValueOrDefault(name) ?? []));
            }
        }
        return rows;
    }

    private static List<string> AllowedEnvironmentsOf(string json)
    {
        if (TryParseObject(json) is not { } entity)
            return [];
        var allowed = entity.FirstOrDefault(p => p.Key.Equals("AllowedEnvironments", StringComparison.OrdinalIgnoreCase)).Value;
        return allowed is JsonArray array
            ? [.. array.Select(e => e is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>()]
            : [];
    }
}
