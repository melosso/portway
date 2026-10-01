---
title: Telemetry
description: "Publish Portway's traces and metrics to an OTLP collector, or let Prometheus scrape the gateway directly"
---

# Telemetry

Portway exports request traces and metrics through one provider, selected with `Telemetry:Provider`. The built-in dashboard is independent of the provider.

## Choosing a provider

One provider is active at a time:

| Provider | Style | Output |
|---|---|---|
| `None` | (default) | No export |
| `Otlp` | Push | Traces and metrics to an OTLP-compatible collector over gRPC |
| `Prometheus` | Pull | Metrics on a scrape endpoint |

Observability platforms (Grafana Alloy, Jaeger, Datadog) use `Otlp`; a standalone Prometheus server uses `Prometheus`. An OTLP collector can re-expose metrics to Prometheus, as in the [collector example](#docker-compose).

## Using the OTLP provider

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

| Key | Default | Description |
|---|---|---|
| `ServiceName` | `Portway.Api` | Service name on every span and metric |
| `ResourceAttributes` | none | Comma-separated `key=value` pairs added to every span and metric |
| `Otlp:Endpoint` | `http://localhost:4317` | Collector gRPC address |

Each key is also read from environment variables with the .NET double-underscore convention:

```bash
Telemetry__Provider=Otlp
Telemetry__Otlp__Endpoint=http://otel-collector:4317
Telemetry__ServiceName=portway-prod
```

## Using the Prometheus provider

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

The scrape path defaults to `/metrics`. Matching scrape configuration:

```yaml
scrape_configs:
  - job_name: portway
    scrape_interval: 15s
    static_configs:
      - targets: ["portway.internal:5000"]
```

::: Note
The scrape endpoint is mapped only with the `Prometheus` provider. It is unauthenticated and rate-limit exempt, like `/health`, and exposes aggregate counters and histograms only. Restrict the path at the firewall or reverse proxy when the gateway is reachable from untrusted networks.
:::

The Prometheus provider exports no traces. Traces with Prometheus metrics require the OTLP provider and a collector.

## What Portway exports

### Traces (OTLP provider)

| Span | Source | Notes |
|---|---|---|
| HTTP request | ASP.NET Core | Root span per inbound request, with method, route and status code |
| SQL query | SqlClient | Child span per database round trip, with statement text when available |
| Outbound HTTP | HttpClient | Child span per proxy call, with target URL and status code |

Errors handled by Portway's exception handler are recorded on the active span with `exception.type`, `exception.message` and `exception.stacktrace`.

### Metrics (both providers)

| Metric | Type | Unit | Dimensions |
|---|---|---|---|
| `portway.request.duration` | Histogram | `s` | `http.method`, `http.response.status_code`, `portway.request_source`, `portway.endpoint` |
| `portway.cache.hit.count` | Counter | `{hit}` | None |
| `portway.cache.miss.count` | Counter | `{miss}` | None |

The `portway.request_source` dimension is `api` for endpoint calls, `ui` for dashboard calls and `other` for the rest. The `portway.endpoint` dimension contains the configured endpoint name for API calls (e.g. `Products`, `composite/SalesOrder`) and is empty for other requests.

## Docker Compose

A gateway pushing to a collector that re-exposes metrics to Prometheus and forwards traces to Jaeger:

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

With `Telemetry__Provider: Prometheus` the collector service is not needed; Prometheus scrapes the `portway` container.

:::tip
For Grafana Alloy or the Grafana Agent, set `Telemetry__Otlp__Endpoint` to its OTLP receiver. Traces (Tempo) and metrics (Mimir/Prometheus) are sent through one pipeline.
:::

## Windows Server and IIS

The `Telemetry` section is read from `appsettings.json`. Collector addresses belong in an environment-specific override file:

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

Under IIS, `<environmentVariables>` in `web.config` override `appsettings.json`:

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
IIS worker processes do not inherit system environment variables. Use `appsettings.json` or `web.config` `<environmentVariables>`; system environment variables and application pool settings are unreliable across IIS resets.
:::

## Upgrading from earlier versions

Earlier releases used a flat `Enabled` switch and an `OtlpEndpoint` key. Both remain supported: `"Enabled": true` selects the OTLP provider, and `OtlpEndpoint` is used when `Otlp:Endpoint` is not set.
