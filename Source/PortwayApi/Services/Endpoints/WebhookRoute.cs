namespace PortwayApi.Services;

using PortwayApi.Classes;

public readonly record struct WebhookRoute(string Env, string EndpointKey, string Id)
{
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
