# Portway Documentation

This directory contains the Portway documentation, served by [Bark](https://github.com/hawkinslabdev/bark). Pages are plain markdown under `docs/`, with site configuration in `docs/config.json`.

## Running locally

```bash
docker compose up -d
```

The site becomes available at `http://localhost:5991`.

## Layout

| Path | Purpose |
|---|---|
| `docs/config.json` | Site configuration: navigation, sidebar, branding, edit links |
| `docs/guide/` | Task-oriented guides (getting started, endpoints, security, operations) |
| `docs/reference/` | Configuration and API reference pages |
| `docs/assets/` | Static assets referenced by pages |
| `wwwroot/` | Bark theme, custom CSS, and shared icons |
