namespace PortwayApi.Classes.OpenApi;

using System;
using System.Collections.Generic;
using PortwayApi.Classes;

/// <summary>Maps every configured endpoint to the OpenAPI path template its operations live under</summary>
internal static class OpenApiEndpointCatalog
{
    /// <summary>Tag shared by file endpoints that declare no namespace</summary>
    public const string FilesFallbackTag = "Files";

    public static string BasePath(EndpointDefinition definition) => $"/api/{{env}}/{definition.FullPath}";

    /// <summary>File operations are served under a fixed /files segment keyed by the endpoint key</summary>
    public static string FileBasePath(string endpointKey) => $"/api/{{env}}/files/{endpointKey}";

    public static IEnumerable<(string BasePath, EndpointDefinition Definition)> All()
    {
        foreach (var kv in EndpointHandler.GetSqlEndpoints())
            yield return (BasePath(kv.Value), kv.Value);

        foreach (var kv in EndpointHandler.GetProxyEndpoints())
            yield return (BasePath(kv.Value), kv.Value);

        foreach (var kv in EndpointHandler.GetStaticEndpoints())
            yield return (BasePath(kv.Value), kv.Value);

        foreach (var kv in EndpointHandler.GetSqlWebhookEndpoints())
            yield return (BasePath(kv.Value), kv.Value);

        foreach (var kv in EndpointHandler.GetFileEndpoints())
            yield return (FileBasePath(kv.Key), kv.Value);
    }

    /// <summary>Whether an endpoint belongs in the document; disabled ones stay in, marked by EndpointStateDocumentFilter</summary>
    public static bool IsDocumented(EndpointDefinition definition) => !definition.Hidden;

    /// <summary>Matches a base and its id and sub-path variants, without matching a longer sibling name</summary>
    public static bool Covers(string basePath, string pathKey) =>
        pathKey.Equals(basePath, StringComparison.OrdinalIgnoreCase) ||
        pathKey.StartsWith(basePath + "(", StringComparison.OrdinalIgnoreCase) ||
        pathKey.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase);
}
