namespace PortwayApi.Services.Mcp;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Base URL for tool calls back into this gateway. The caller token is sent here: request headers never select the target
/// </summary>
internal static class McpLoopback
{
    public static string BaseUrl(HttpContext context)
    {
        var addresses = context.RequestServices.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;
        return Resolve(addresses ?? [], context.Request.PathBase, context.Connection.LocalPort);
    }

    internal static string Resolve(IEnumerable<string> addresses, PathString pathBase, int localPort)
    {
        var bound = addresses.Select(BindingAddress.Parse).Where(a => !a.IsUnixPipe).ToList();
        var address = bound.FirstOrDefault(a => a.Scheme == "http") ?? bound.FirstOrDefault();

        var origin = address is null
            ? $"http://localhost:{localPort}"
            : $"{address.Scheme}://{(address.Host is "+" or "*" or "0.0.0.0" or "[::]" ? "localhost" : address.Host)}:{address.Port}";

        return origin + pathBase.ToUriComponent();
    }
}
