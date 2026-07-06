# Portway Documentation

This directory contains the Portway documentation, served by [Bark](https://github.com/melosso/bark). Pages are plain markdown under `docs/`, with site configuration in `docs/config.json`.

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
| `docs/public/` | Static assets referenced by pages |
| `wwwroot/` | Bark theme, custom CSS, and shared icons |

## Writing style

Documentation copy follows the Bark tone guidelines: suggestive and explanatory rather than commanding, no em or en dashes, and loud callouts reserved for genuine edge cases. A quick pre-publish check for aggressive phrasing:

```bash
grep -niE '\b(must|mandatory|strictly|forbidden)\b' docs/path/to/page.md
```

Hits in prose are worth rewording; factual constraints in tables, error messages, and code samples can stay as they are.
