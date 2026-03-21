using System.Diagnostics;

namespace PortwayApi.Services.Telemetry;

public static class PortwayTelemetry
{
    public const string ServiceName = "Portway.Api";
    public const string MeterName   = "Portway.Api";

    // Versioned ActivitySource — pre-1.0 until telemetry schema is proven stable
    public static readonly ActivitySource Source = new(ServiceName, "0.1.0");

    // Span operation name constants (kept under test to prevent silent renames becoming breaking changes)
    public static class Operations
    {
        public const string SqlExecute   = "portway.sql.execute";
        public const string ProxyForward = "portway.proxy.forward";
        public const string CacheGet     = "portway.cache.get";
    }
}
