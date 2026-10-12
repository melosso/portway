namespace PortwayApi.Classes;

using Microsoft.AspNetCore.Routing;

/// <summary>
/// Route constraint apiversion: matches a v{n} endpoint version segment
/// </summary>
public sealed class ApiVersionRouteConstraint : IRouteConstraint
{
    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection) =>
        values.TryGetValue(routeKey, out var value) && EndpointVersion.TryParse(value?.ToString(), out _);
}

public static class EndpointVersionRoutingExtensions
{
    public static IServiceCollection AddEndpointVersioning(this IServiceCollection services) =>
        services.Configure<RouteOptions>(options => options.ConstraintMap["apiversion"] = typeof(ApiVersionRouteConstraint));
}
