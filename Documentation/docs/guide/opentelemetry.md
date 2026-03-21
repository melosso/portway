# OpenTelemetry

> Connect Portway to any OTLP-compatible collector for traces, metrics, and distributed request visibility.

Portway exports telemetry over OTLP (gRPC) by default. Set an endpoint and your collector receives data immediately, no code changes or restarts required beyond configuration. The service name reported to your backend is `Portway.Api`.

## Connect a collector

Set `OTEL_EXPORTER_OTLP_ENDPOINT` to your collector's gRPC address:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
```

All telemetry, traces and metrics, is sent to this endpoint. If your collector uses a non-standard port or requires a separate endpoint per signal, use the signal-specific variables:

```bash
OTEL_EXPORTER_OTLP_TRACES_ENDPOINT=http://localhost:4317
OTEL_EXPORTER_OTLP_METRICS_ENDPOINT=http://localhost:4317
```

## Configure resource attributes

Override the default service name or attach additional resource metadata:

```bash
OTEL_SERVICE_NAME=portway-prod
OTEL_RESOURCE_ATTRIBUTES=deployment.environment=production,host.name=gw01
```

`OTEL_SERVICE_NAME` takes precedence over the built-in `Portway.Api` default.

## What Portway exports

### Traces

| Span | Source | Notes |
|---|---|---|
| HTTP request | ASP.NET Core | One root span per inbound request. Includes method, route, and status code |
| SQL query | SqlClient | One child span per database round-trip. Includes statement text when available |
| Outbound HTTP | HttpClient | One child span per proxy call. Includes target URL and status code |

Proxy endpoint calls produce a child HttpClient span nested under the inbound request span, giving you end-to-end latency breakdown per forwarded call. SQL endpoint calls produce SqlClient child spans automatically without any additional configuration.

Errors caught by Portway's exception handler are recorded on the active span with `exception.type`, `exception.message`, and `exception.stacktrace` attributes.

### Metrics

| Metric | Type | Unit | Dimensions |
|---|---|---|---|
| `portway.request.duration` | Histogram | `s` | `http.method`, `http.response.status_code`, `portway.request_source` |
| `portway.cache.hit.count` | Counter | `{hit}` | — |
| `portway.cache.miss.count` | Counter | `{miss}` | — |

`portway.request_source` distinguishes traffic by origin: `api` for endpoint calls, `ui` for dashboard calls, `other` for everything else.

ASP.NET Core also emits its own `http.server.request.duration` histogram, which overlaps with `portway.request.duration`. Both are exported. Drop one at the collector if you want to avoid duplication.

## Docker Compose example

```yaml
services:
  portway:
    image: melosso/portway:latest
    environment:
      OTEL_EXPORTER_OTLP_ENDPOINT: http://otel-collector:4317
      OTEL_SERVICE_NAME: portway-prod
      OTEL_RESOURCE_ATTRIBUTES: deployment.environment=production
    ports:
      - "5000:5000"

  otel-collector:
    image: otel/opentelemetry-collector-contrib:latest
    volumes:
      - ./otel-collector.yaml:/etc/otelcol-contrib/config.yaml
    ports:
      - "4317:4317"
```

A minimal collector config forwarding to Prometheus and Jaeger:

```yaml
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

:::tip
If you use Grafana Alloy or the Grafana Agent, point `OTEL_EXPORTER_OTLP_ENDPOINT` at its OTLP receiver and route from there. Both traces (Tempo) and metrics (Mimir/Prometheus) land in a single pipeline.
:::

## Windows Server and IIS deployment

Configure telemetry through the `Telemetry` section in `appsettings.json`. Set `Enabled` to `true` and point `OtlpEndpoint` at your collector:

```json
{
  "Telemetry": {
    "Enabled": true,
    "OtlpEndpoint": "http://otel-collector.internal:4317",
    "ServiceName": "portway-prod",
    "ResourceAttributes": "deployment.environment=production,host.name=gw01"
  }
}
```

`ResourceAttributes` accepts a comma-separated `key=value` string. `ServiceName` defaults to `Portway.Api` when omitted.

Use an environment-specific override file to avoid committing collector addresses to source control:

```json
// appsettings.Production.json
{
  "Telemetry": {
    "Enabled": true,
    "OtlpEndpoint": "http://otel-collector.internal:4317"
  }
}
```

For IIS hosting, `web.config` `<environmentVariables>` can override individual values and take precedence over `appsettings.json`:

```xml
<configuration>
  <system.webServer>
    <aspNetCore processPath="dotnet" arguments=".\PortwayApi.dll" stdoutLogEnabled="false">
      <environmentVariables>
        <environmentVariable name="OTEL_EXPORTER_OTLP_ENDPOINT" value="http://otel-collector.internal:4317" />
      </environmentVariables>
    </aspNetCore>
  </system.webServer>
</configuration>
```

:::info
IIS worker processes do not inherit system environment variables. Use `appsettings.json` or `web.config` `<environmentVariables>` — not the Windows system environment or application pool advanced settings, which are unreliable across IIS resets.
:::

## Next steps

- [Monitoring](./monitoring)
- [Logging](/reference/logging)
- [Health Checks](/reference/health-checks)
