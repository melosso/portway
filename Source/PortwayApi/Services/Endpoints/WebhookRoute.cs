namespace PortwayApi.Services;

using PortwayApi.Classes;

/// <summary>
/// A webhook delivery route, <c>{env}/{endpoint}/{id}</c>. The one parser for verification and storage.
/// </summary>
public readonly record struct WebhookRoute(string Env, string EndpointKey, string Id)
{
    /// <summary>
    /// Parses the segments after the environment. Refuses extra segments, keyed names and quoted or parenthesised ids.
    /// </summary>
    public static bool TryParse(string env, string[] segments, HttpContext? context, out WebhookRoute route)
    {
        route = default;
        var identity = context is null
            ? Api.EndpointController.ResolveEndpointIdentity(segments)
            : Api.EndpointController.ResolveEndpointIdentity(context, segments);
        if (identity is not { Type: EndpointType.Webhook } resolved || segments.Length != resolved.NameIndex + 2)
            return false;

        var id = segments[^1];
        if (segments[resolved.NameIndex].Contains('(') || id.AsSpan().IndexOfAny("()'\"") >= 0)
            return false;

        route = new WebhookRoute(env, resolved.Namespace.Length > 0 ? $"{resolved.Namespace}/{resolved.Name}" : resolved.Name, id);
        return true;
    }
}
