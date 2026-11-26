using System.Text.Json;
using PortwayApi.Classes;
using PortwayApi.Tests.Base;
using Xunit;

namespace PortwayApi.Tests.Endpoints;

/// <summary>
/// A name used by two endpoint types loads once, in the type routing reaches first
/// </summary>
public sealed class EndpointNameClashTests : ApiTestBase, IDisposable
{
    private const string Env = "500";
    private static readonly string SqlDir = Path.Combine("endpoints", "SQL", "NameClash");
    private static readonly string ProxyDir = Path.Combine("endpoints", "Proxy", "NameClash");

    private static readonly object Proxy = new { Url = "http://127.0.0.1:1/item", Methods = new[] { "GET" }, AllowedEnvironments = new[] { Env } };

    public EndpointNameClashTests()
    {
        SetAllowedEnvironments(Env);
        Write(Path.Combine(SqlDir, "Item"), new { DatabaseObjectName = "Items", AllowedColumns = new[] { "Id" }, AllowedEnvironments = new[] { Env } });
        Write(Path.Combine(ProxyDir, "Item"), Proxy);
        Write(Path.Combine(ProxyDir, "Item", "v2"), Proxy);
        Write(Path.Combine(ProxyDir, "Other"), Proxy);
        EndpointHandler.ReloadAllEndpoints();
    }

    public new void Dispose()
    {
        Directory.Delete(SqlDir, recursive: true);
        Directory.Delete(ProxyDir, recursive: true);
        EndpointHandler.ReloadAllEndpoints();
        base.Dispose();
    }

    private static void Write(string dir, object content)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "entity.json"), JsonSerializer.Serialize(content));
    }

    [Fact]
    public void ProxyWithASqlName_IsNotLoaded()
    {
        Assert.True(EndpointHandler.GetSqlEndpoints().ContainsKey("NameClash/Item"));
        Assert.False(EndpointHandler.GetProxyEndpoints().ContainsKey("NameClash/Item"));
        Assert.True(EndpointHandler.GetProxyEndpoints().ContainsKey("NameClash/Item@v2"));
        Assert.True(EndpointHandler.GetProxyEndpoints().ContainsKey("NameClash/Other"));
        Assert.Equal(EndpointType.SQL, PortwayApi.Api.EndpointController.ResolveEndpointIdentity(["NameClash", "Item"])?.Type);
    }

    [Fact]
    public void ProxyLoadsAgain_WhenTheSqlEndpointIsRemoved()
    {
        Directory.Delete(Path.Combine(SqlDir, "Item"), recursive: true);
        EndpointHandler.ReloadEndpointType(EndpointType.SQL);

        Assert.True(EndpointHandler.GetProxyEndpoints().ContainsKey("NameClash/Item"));
        Assert.Equal(EndpointType.Proxy, PortwayApi.Api.EndpointController.ResolveEndpointIdentity(["NameClash", "Item"])?.Type);
    }

    [Fact]
    public void ProxyIsDropped_WhenASqlEndpointTakesItsNameOnReload()
    {
        Write(Path.Combine(SqlDir, "Other"), new { DatabaseObjectName = "Others", AllowedColumns = new[] { "Id" }, AllowedEnvironments = new[] { Env } });
        EndpointHandler.ReloadEndpointType(EndpointType.SQL);

        Assert.False(EndpointHandler.GetProxyEndpoints().ContainsKey("NameClash/Other"));
    }

    [Theory]
    [InlineData(EndpointType.Proxy, "Proxy", "SQL")]
    [InlineData(EndpointType.SQL, "SQL", "Proxy")]
    public void ConsoleSave_RefusesANameAnotherTypeUses(EndpointType type, string folder, string owner)
    {
        var json = type == EndpointType.SQL
            ? JsonSerializer.Serialize(new { DatabaseObjectName = "Others", AllowedColumns = new[] { "Id" } })
            : JsonSerializer.Serialize(Proxy);
        var name = type == EndpointType.SQL ? "Other" : "Item";
        var directory = Path.GetFullPath(Path.Combine("endpoints", folder));
        var file = Path.Combine(directory, "NameClash", name, "entity.json");

        var errors = EndpointHandler.ValidateConfig(type, json, file, directory);

        Assert.Contains(errors, e => e.Contains($"already used by the {owner} endpoint NameClash/{name}"));
    }

    [Fact]
    public void ConsoleSave_AllowsAnUnusedName()
    {
        var directory = Path.GetFullPath(Path.Combine("endpoints", "Proxy"));
        var file = Path.Combine(directory, "NameClash", "Fresh", "entity.json");

        Assert.Empty(EndpointHandler.ValidateConfig(EndpointType.Proxy, JsonSerializer.Serialize(Proxy), file, directory));
    }
}
