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

// Extension methods for adding RateLimiter middleware
public static class RateLimiterExtensions
{
    public static IApplicationBuilder UseRateLimiter(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimiter>();
    }
}
