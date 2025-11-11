using Microsoft.AspNetCore.Routing;

namespace PortwayApi.Api;

/// <summary>
/// Custom constraint for proxy endpoint routing
/// This allows the route to be skipped when SQL endpoints should handle the request
/// </summary>
public class ProxyConstraintAttribute : Attribute, IRouteConstraint
{
    public bool Match(
        HttpContext? httpContext, 
        IRouter? route, 
        string routeKey, 
        RouteValueDictionary values, 
        RouteDirection routeDirection)
    {
        // If httpContext is null, we can't make a decision
        if (httpContext == null)
        {
            return false;
        }

        // Extract the endpoint path from the route values
        if (!values.TryGetValue("catchall", out var catchallObj) || catchallObj == null)
        {
            return false;
        }

        string catchall = catchallObj.ToString() ?? "";
        
        // Extract the first segment as the endpoint name
        var segments = catchall.Split('/');
        if (segments.Length == 0)
        {
            return true; // Let the controller handle invalid formats
        }

        string endpointName = segments[0];
        
        // Check if this endpoint is defined as an SQL endpoint
        var sqlEndpoints = Classes.EndpointHandler.GetSqlEndpoints();
        bool isSqlEndpoint = sqlEndpoints.ContainsKey(endpointName);
        
        // Return true (match) only if it's NOT an SQL endpoint
        return !isSqlEndpoint;
    }
}