---
title: Telemetry
description: "Publish Portway's traces and metrics to an OTLP collector, or let Prometheus scrape the gateway directly"
---

# Telemetry

Portway can publish its request traces and metrics to the monitoring stack you already run. You pick one provider through a single configuration key, and the gateway takes care of the rest: no code changes, no plugins, just configuration. The built-in dashboard keeps working either way, so enabling telemetry is purely additive.

## Choosing a provider

The `Telemetry:Provider` key selects how telemetry leaves the gateway. Since most teams standardize on either a push or a pull pipeline, only one provider is active at a time:

| Provider | Style | What you get |
|---|---|---|
| `None` | (default) | Telemetry export stays off; the built-in dashboard is unaffected |
| `Otlp` | Push | Traces and metrics sent to any OTLP-compatible collector over gRPC |
| `Prometheus` | Pull | Metrics served on a scrape endpoint, ready for your Prometheus server |

If you are unsure which fits your setup: teams running an observability platform (Grafana Alloy, Jaeger, Datadog, and similar) generally want `Otlp`, while teams with a plain Prometheus server pointed at their infrastructure will feel right at home with `Prometheus`. Should you ever need both, the OTLP collector can re-expose metrics to Prometheus on your behalf, as shown in the [collector example](#docker-compose) below.

## Using the OTLP provider

Point the gateway at your collector's gRPC address and set the provider:

```json
{
  "Telemetry": {
    "Provider": "Otlp",
    "ServiceName": "portway-prod",
    "ResourceAttributes": "deployment.environment=production,host.name=gw01",
    "Otlp": {
      "Endpoint": "http://otel-collector.internal:4317"
    }
  }
}
```

A few helpful defaults to be aware of:

* `ServiceName` falls back to `Portway.Api` when omitted. Overriding it is a convenient way to tell environments apart in your tracing backend.
* `ResourceAttributes` accepts a comma-separated `key=value` string and attaches the values to every exported span and metric.
* `Otlp:Endpoint` defaults to `http://localhost:4317`, the standard OTLP gRPC port.

Every value can also be supplied as an environment variable using the .NET double-underscore convention, which is handy in containerized deployments:

```bash
Telemetry__Provider=Otlp
Telemetry__Otlp__Endpoint=http://otel-collector:4317
Telemetry__ServiceName=portway-prod
```

## Using the Prometheus provider

If your monitoring is built around Prometheus, you can skip the collector entirely and let Prometheus pull metrics straight from the gateway:

```json
{
  "Telemetry": {
    "Provider": "Prometheus",
    "Prometheus": {
      "Path": "/metrics"
    }
  }
}
```

`Path` defaults to `/metrics` and can be changed if that route conflicts with something else in your setup. A matching scrape configuration looks like this:

```yaml
scrape_configs:
  - job_name: portway
    scrape_interval: 15s
    static_configs:
      - targets: ["portway.internal:5000"]
```

::: Note
The scrape endpoint only exists when the `Prometheus` provider is selected; with any other provider the route is not mapped. When enabled it is served without authentication and exempt from rate limiting, following the same convention as `/health`, and it only exposes aggregate counters and histograms, never request payloads. If the gateway is reachable from untrusted networks, it is recommended to restrict access to the metrics path at your firewall or reverse proxy.
:::

Since Prometheus is a metrics-only system, traces are not collected with this provider. If you want distributed tracing alongside Prometheus-style metrics, the OTLP provider combined with a collector gives you both.

## What Portway exports

### Traces (OTLP provider)

Three span types cover the full request path through the gateway:

| Span | Source | Notes |
|---|---|---|
| HTTP request | ASP.NET Core | One root span per inbound request. Includes method, route, and status code |
| SQL query | SqlClient | One child span per database round-trip. Includes statement text when available |
| Outbound HTTP | HttpClient | One child span per proxy call. Includes target URL and status code |

Proxy endpoint calls produce a child HttpClient span nested under the inbound request span, giving you an end-to-end latency breakdown per forwarded call. SQL endpoint calls produce SqlClient child spans automatically without any additional configuration.

Errors caught by Portway's exception handler are recorded on the active span with `exception.type`, `exception.message`, and `exception.stacktrace` attributes.

### Metrics (both providers)

Portway publishes its own meters alongside the standard ASP.NET Core instrumentation:

| Metric | Type | Unit | Dimensions |
|---|---|---|---|
| `portway.request.duration` | Histogram | `s` | `http.method`, `http.response.status_code`, `portway.request_source`, `portway.endpoint` |
| `portway.cache.hit.count` | Counter | `{hit}` | None |
| `portway.cache.miss.count` | Counter | `{miss}` | None |

`portway.request_source` distinguishes traffic by origin: `api` for endpoint calls, `ui` for dashboard calls, `other` for everything else. `portway.endpoint` carries the configured endpoint name for API calls (for example `Products` or `composite/SalesOrder`), so you can break down latency and error rates per endpoint. It stays empty for requests that do not target a configured endpoint.

ASP.NET Core also emits its own `http.server.request.duration` histogram, which overlaps with `portway.request.duration`. Both are exported, so you can drop one at the collector if you would rather avoid the duplication.

## Docker Compose

A gateway pushing to a collector, with the collector re-exposing metrics to Prometheus and forwarding traces to Jaeger:

```yaml
services:
  portway:
    image: melosso/portway:latest
    environment:
      Telemetry__Provider: Otlp
      Telemetry__Otlp__Endpoint: http://otel-collector:4317
      Telemetry__ServiceName: portway-prod
    ports:
      - "5000:5000"

  otel-collector:
    image: otel/opentelemetry-collector-contrib:latest
    volumes:
      - ./otel-collector.yaml:/etc/otelcol-contrib/config.yaml
    ports:
      - "4317:4317"
```

```yaml
# otel-collector.yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317

exporters:
  prometheus:
    endpoint: 0.0.0.0:8889
  otlp/jaeger:
    endpoint: jaeger:4317
    tls:
      insecure: true

service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [otlp/jaeger]
    metrics:
      receivers: [otlp]
      exporters: [prometheus]
```

If you prefer the pull model instead, set `Telemetry__Provider: Prometheus` on the gateway and drop the collector service entirely. Prometheus then scrapes the `portway` container directly.

:::tip
If you use Grafana Alloy or the Grafana Agent, point `Telemetry__Otlp__Endpoint` at its OTLP receiver and route from there. Both traces (Tempo) and metrics (Mimir/Prometheus) land in a single pipeline.
:::

## Windows Server and IIS

On Windows, the `Telemetry` section lives in `appsettings.json` next to the rest of your gateway configuration. An environment-specific override file is a good place for collector addresses, keeping them out of source control:

```json [appsettings.Production.json]
{
  "Telemetry": {
    "Provider": "Otlp",
    "Otlp": {
      "Endpoint": "http://otel-collector.internal:4317"
    }
  }
}
```

For IIS hosting, `web.config` `<environmentVariables>` can override individual values and take precedence over `appsettings.json`:

```xml
<configuration>
  <system.webServer>
    <aspNetCore processPath="dotnet" arguments=".\PortwayApi.dll" stdoutLogEnabled="false">
      <environmentVariables>
        <environmentVariable name="Telemetry__Provider" value="Otlp" />
        <environmentVariable name="Telemetry__Otlp__Endpoint" value="http://otel-collector.internal:4317" />
      </environmentVariables>
    </aspNetCore>
  </system.webServer>
</configuration>
```

:::info
IIS worker processes do not inherit system environment variables. Use `appsettings.json` or `web.config` `<environmentVariables>`, not the Windows system environment or application pool advanced settings, which are unreliable across IIS resets.
:::

## Upgrading from earlier versions

Earlier releases configured telemetry with a flat `Enabled` switch and `OtlpEndpoint` key. Both keep working: `"Enabled": true` selects the OTLP provider automatically, and a flat `OtlpEndpoint` is used whenever `Otlp:Endpoint` is not set. You can migrate to the `Provider` key at your own pace.
