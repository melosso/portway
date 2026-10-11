using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

/// <summary>
/// PORTWAY_ environment variables land under their configuration keys
/// </summary>
public sealed class EnvAliasesTests
{
    private static Dictionary<string, string?> Resolve(params (string Name, string Value)[] env) =>
        EnvAliases.Resolve(name => env.FirstOrDefault(e => e.Name == name).Value);

    [Fact]
    public void ScalarAliasMapsToConfigKey()
    {
        var config = Resolve(("PORTWAY_TELEMETRY_PROVIDER", "Otlp"));

        Assert.Equal("Otlp", config["Telemetry:Provider"]);
    }

    [Fact]
    public void ListAliasSplitsIntoIndexedKeys()
    {
        var config = Resolve(("PORTWAY_PUBLIC_ORIGINS", "https://a.example.com, https://b.example.com,"));

        Assert.Equal("https://a.example.com", config["Console:PublicOrigins:0"]);
        Assert.Equal("https://b.example.com", config["Console:PublicOrigins:1"]);
        Assert.Equal(2, config.Count);
    }

    [Fact]
    public void DirectReadAliasIsNotAConfigKey()
    {
        var config = Resolve(("PORTWAY_USE_HTTPS", "true"));

        Assert.Empty(config);
    }

    // every alias, the upgrade contract for operators; renaming one needs a deprecation path
    public static TheoryData<string, string, string?> All => new()
    {
        { "PORTWAY_ADMIN_KEY", "WebUi__AdminApiKey", "Console:AdminApiKey" },
        { "PORTWAY_WEBUI_ENABLED", "WebUi__Enabled", "Console:Enabled" },
        { "PORTWAY_ALLOWED_HOSTS", "AllowedHosts", "AllowedHosts" },
        { "PORTWAY_PATH_BASE", "PathBase", "PathBase" },
        { "PORTWAY_SECURE_COOKIES", "WebUi__SecureCookies", "Console:SecureCookies" },
        { "PORTWAY_OPENAPI_MARKDOWN", "OpenApi__MarkdownEnabled", "OpenApi:MarkdownEnabled" },
        { "PORTWAY_PUBLIC_ORIGINS", "WebUi__PublicOrigins__0", "Console:PublicOrigins[]" },
        { "PORTWAY_SEED_PASSWORD", "WebUi__SeedPassword", "Console:SeedPassword" },
        { "PORTWAY_PROMO_TEXT", "WebUi__Customization__PromoText", "Console:Customization:PromoText" },
        { "PORTWAY_LOGIN_FOOTER", "WebUi__Customization__LoginFooter", "Console:Customization:LoginFooter" },
        { "PORTWAY_KNOWN_PROXIES", "ForwardedHeaders__KnownProxies__0", "ForwardedHeaders:KnownProxies[]" },
        { "PORTWAY_KNOWN_NETWORKS", "ForwardedHeaders__KnownNetworks__0", "ForwardedHeaders:KnownNetworks[]" },
        { "PORTWAY_OIDC_ENABLED", "Oidc__Enabled", "Oidc:Enabled" },
        { "PORTWAY_MCP_ENABLED", "Mcp__Enabled", "Mcp:Enabled" },
        { "PORTWAY_CHAT_ENABLED", "Mcp__ChatEnabled", "Mcp:ChatEnabled" },
        { "PORTWAY_TELEMETRY_PROVIDER", "Telemetry__Provider", "Telemetry:Provider" },
        { "PORTWAY_OTLP_ENDPOINT", "Telemetry__Otlp__Endpoint", "Telemetry:Otlp:Endpoint" },
        { "PORTWAY_SERVICE_NAME", "Telemetry__ServiceName", "Telemetry:ServiceName" },
        { "PORTWAY_USE_HTTPS", "Use_HTTPS", null },
        { "PORTWAY_PROXY_USERNAME", "PROXY_USERNAME", null },
        { "PORTWAY_PROXY_PASSWORD", "PROXY_PASSWORD", null },
        { "PORTWAY_PROXY_DOMAIN", "PROXY_DOMAIN", null },
        { "PORTWAY_KEYVAULT_URI", "KEYVAULT_URI", null },
    };

    [Fact]
    public void TableListsEveryAlias()
    {
        var expected = All.Select(row => row.Data).ToHashSet();

        Assert.Equal(expected, EnvAliases.Entries.ToHashSet());
    }

    [Theory]
    [MemberData(nameof(All))]
    public void PortwayNameLandsUnderItsConfigKey(string newName, string oldName, string? configKey)
    {
        _ = oldName;
        var config = Resolve((newName, "value"));

        if (configKey is null)
            Assert.Empty(config);
        else
            Assert.Equal("value", config[configKey.EndsWith("[]") ? configKey[..^2] + ":0" : configKey]);
    }

    [Theory]
    [MemberData(nameof(All))]
    public void BothNamesLockTheConsoleField(string newName, string oldName, string? configKey)
    {
        if (configKey is null) return;
        var key = configKey.TrimEnd('[', ']');

        Assert.Equal(newName, EnvNameFor(key, newName));
        Assert.Equal(oldName, EnvNameFor(key, oldName));
    }

    [Theory]
    [MemberData(nameof(All))]
    public void DirectReadPrefersThePortwayName(string newName, string oldName, string? configKey)
    {
        if (configKey is not null) return;

        Assert.Equal("new", EnvAliases.GetDirect(newName, n => n == newName ? "new" : n == oldName ? "old" : null));
        Assert.Equal("old", EnvAliases.GetDirect(newName, n => n == oldName ? "old" : null));
        Assert.Null(EnvAliases.GetDirect(newName, _ => null));
    }

    private static string? EnvNameFor(string key, params string[] envNames) => EnvAliases.EnvNameFor(key, envNames);

    [Fact]
    public void EnvNameForPrefersPortwayAlias()
    {
        Assert.Equal("PORTWAY_MCP_ENABLED", EnvNameFor("Mcp:Enabled", "Mcp__Enabled", "PORTWAY_MCP_ENABLED"));
    }

    [Fact]
    public void EnvNameForFindsNativeNameInAnyCase()
    {
        Assert.Equal("Caching__Enabled", EnvNameFor("Caching:Enabled", "Caching__Enabled"));
        Assert.Equal("CACHING__ENABLED", EnvNameFor("Caching:Enabled", "CACHING__ENABLED"));
    }

    [Fact]
    public void EnvNameForFindsListEntries()
    {
        Assert.Equal("ForwardedHeaders__KnownProxies__2", EnvNameFor("ForwardedHeaders:KnownProxies", "ForwardedHeaders__KnownProxies__2"));
        Assert.Equal("PORTWAY_KNOWN_PROXIES", EnvNameFor("ForwardedHeaders:KnownProxies", "PORTWAY_KNOWN_PROXIES"));
    }

    [Fact]
    public void EnvNameForIgnoresSiblingAndPrefixedNames()
    {
        Assert.Null(EnvNameFor("Mcp:Enabled", "Mcp__EnabledX", "Mcp__ChatEnabled", "DOTNET_Mcp__Enabled", "PATH"));
        Assert.Null(EnvNameFor("Caching:Enabled", "Caching"));
    }

    [Fact]
    public void EnvNameForIsNullWhenNothingIsSet()
    {
        Assert.Null(EnvNameFor("Mcp:Enabled"));
    }
}
