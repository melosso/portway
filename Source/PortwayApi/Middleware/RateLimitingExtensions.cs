namespace PortwayApi.Middleware;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Text.Json;
using Serilog;
using PortwayApi.Auth;

/// <summary>Extension methods for Rate Limiting</summary>
public static class RateLimitingExtensions
{
    /// <summary>Adds rate limiting configuration to the service collection</summary>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure RateLimitSettings from configuration
        var rateLimitSettings = new RateLimitSettings();
        configuration.GetSection("RateLimiting").Bind(rateLimitSettings);
        
        // Add settings as singleton for potential future DI
        services.AddSingleton(rateLimitSettings);
        
        return services;
    }

    /// <summary>Adds rate limiting middleware to the application pipeline</summary>
    public static IApplicationBuilder UseRateLimiter(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimiter>();
    }
}
