using System.Reflection;
using System.Text.RegularExpressions;
using PortwayApi.Classes;
using Xunit;

namespace PortwayApi.Tests.Services;

/// <summary>
/// The console endpoint form contains every endpoint config key as a form field or a Raw JSON key
/// </summary>
public class EndpointFormInventoryTests
{
    private static readonly Type[] ConfigTypes =
    [
        typeof(EndpointEntity), typeof(ExtendedEndpointEntity), typeof(FileEndpointEntity),
        typeof(StaticEndpointEntity), typeof(McpSettings), typeof(ProxyResponseTransforms)
    ];

    private static string EndpointsView() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "_", "views", "endpoints.html"));

    [Fact]
    public void EndpointForm_ContainsEveryConfigKey()
    {
        var html = EndpointsView();

        var missing = ConfigTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.CanRead && p.CanWrite)
            .Select(p => p.Name)
            .Distinct()
            .Where(name => !Regex.IsMatch(html, $@"\b{name}\b"))
            .Order()
            .ToList();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("WriteMode")]
    [InlineData("ForwardAuthorization")]
    [InlineData("Mask")]
    public void EndpointForm_HasFieldsForSecurityKeys(string key)
    {
        var html = EndpointsView();
        var rawOnly = Regex.Match(html, @"const RAW_ONLY_KEYS = \[(?<keys>[^\]]*)\]").Groups["keys"].Value;

        Assert.Matches($@"\b{key}\b", html);
        Assert.DoesNotMatch($@"'[A-Za-z.]*\b{key}'", rawOnly);
    }
}
