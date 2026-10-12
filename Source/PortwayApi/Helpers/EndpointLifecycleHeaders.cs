namespace PortwayApi.Helpers;

using System.Globalization;
using PortwayApi.Classes;
using PortwayApi.Classes.OpenApi;

/// <summary>
/// Deprecation (RFC 9745), Sunset (RFC 8594) and successor-version Link headers for an endpoint response
/// </summary>
internal static class EndpointLifecycleHeaders
{
    public static void Apply(HttpResponse response, EndpointDefinition endpoint, string env)
    {
        if (endpoint.Deprecated && endpoint.DeprecatedSince is { } since)
        {
            response.Headers["Deprecation"] = "@" + since.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        if (endpoint.Sunset is { } sunset)
        {
            response.Headers["Sunset"] = sunset.UtcDateTime.ToString("R", CultureInfo.InvariantCulture);
        }

        if (endpoint.Deprecated && Successor(endpoint) is { } successor)
        {
            var path = successor.Type == EndpointType.Files
                ? OpenApiEndpointCatalog.FileBasePath(successor.Identity)
                : OpenApiEndpointCatalog.BasePath(successor);
            response.Headers.Append("Link", $"<{response.HttpContext.Request.PathBase}{path.Replace("{env}", env)}>; rel=\"successor-version\"");
        }
    }

    internal static EndpointDefinition? Successor(EndpointDefinition endpoint) =>
        OpenApiEndpointCatalog.All()
            .Select(e => e.Definition)
            .Where(d => d.Enabled
                && string.Equals(d.FullPath, endpoint.FullPath, StringComparison.OrdinalIgnoreCase)
                && EndpointVersion.Number(d.Version) > EndpointVersion.Number(endpoint.Version))
            .MaxBy(d => EndpointVersion.Number(d.Version));
}
