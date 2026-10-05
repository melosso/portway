---
title: Telemetry
description: "Publish Portway's traces and metrics to an OTLP collector, or let Prometheus scrape the gateway directly"
---

# Telemetry

Portway exports request traces and metrics through one provider, selected with `Telemetry:Provider`. The built-in dashboard is independent of the provider.

## Choosing a provider

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
| `Provider` (`PORTWAY_TELEMETRY_PROVIDER`) | `None` | `None`, `Otlp` or `Prometheus` |
| `ServiceName` (`PORTWAY_SERVICE_NAME`) | `Portway.Api` | Service name on every span and metric |
| `ResourceAttributes` | none | Comma-separated `key=value` pairs added to every span and metric |
| `Otlp:Endpoint` (`PORTWAY_OTLP_ENDPOINT`) | `http://localhost:4317` | Collector gRPC address |

Environment variables:

::: code-group

```bash [Linux]
export PORTWAY_TELEMETRY_PROVIDER=Otlp
export PORTWAY_OTLP_ENDPOINT=http://otel-collector:4317
export PORTWAY_SERVICE_NAME=portway-prod
```

```powershell [Windows]
$env:PORTWAY_TELEMETRY_PROVIDER = "Otlp"
$env:PORTWAY_OTLP_ENDPOINT = "http://otel-collector:4317"
$env:PORTWAY_SERVICE_NAME = "portway-prod"
```

:::

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
      - targets: ["portway.internal:8080"]
```

::: info
The scrape endpoint is mapped only with the `Prometheus` provider. It is unauthenticated and rate-limit exempt, like `/health`, and exposes aggregate counters and histograms only. Restrict the path at the firewall or reverse proxy when the gateway is reachable from untrusted networks.
:::

The Prometheus provider exports no traces.

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

The `portway.request_source` dimension is `api` for `/api` calls, `ui` for console calls and `other` for the rest. The `portway.endpoint` dimension contains the configured endpoint name with namespace and version for API calls (e.g. `Products`, `CRM/Accounts`, `Inventory/Products@v2`, `composite/SalesOrder`) and is empty for other requests and for paths that match no configured endpoint. `http.method` is `OTHER` for methods outside `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `MERGE`, `QUERY`, `HEAD` and `OPTIONS`.

## Docker Compose

A gateway pushing to a collector that re-exposes metrics to Prometheus and forwards traces to Jaeger:

```yaml
services:
  portway:
    image: ghcr.io/melosso/portway:latest
    environment:
      PORTWAY_TELEMETRY_PROVIDER: Otlp
      PORTWAY_OTLP_ENDPOINT: http://otel-collector:4317
      PORTWAY_SERVICE_NAME: portway-prod
    ports:
      - "8080:8080"

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

With `PORTWAY_TELEMETRY_PROVIDER: Prometheus` the collector service is not needed; Prometheus scrapes the `portway` container.

:::tip
For Grafana Alloy or the Grafana Agent, set `PORTWAY_OTLP_ENDPOINT` to its OTLP receiver. Traces (Tempo) and metrics (Mimir/Prometheus) are sent through one pipeline.
:::

## Windows Server and IIS

`appsettings.Production.json` overrides `appsettings.json`:

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
        <environmentVariable name="PORTWAY_TELEMETRY_PROVIDER" value="Otlp" />
        <environmentVariable name="PORTWAY_OTLP_ENDPOINT" value="http://otel-collector.internal:4317" />
      </environmentVariables>
    </aspNetCore>
  </system.webServer>
</configuration>
```

## Upgrading from earlier versions

Earlier releases used a flat `Enabled` switch and an `OtlpEndpoint` key. Both remain supported: `"Enabled": true` selects the OTLP provider, and `OtlpEndpoint` is used when `Otlp:Endpoint` is not set.
