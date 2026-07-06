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

public class RateLimitSettings
{
    public bool Enabled { get; set; } = true;
    public int IpLimit { get; set; } = 100;
    public int IpWindow { get; set; } = 60; // seconds
    public int TokenLimit { get; set; } = 1000;
    public int TokenWindow { get; set; } = 60; // seconds
}
