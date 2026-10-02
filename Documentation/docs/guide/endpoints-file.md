---
title: File Endpoints
description: "Upload, download, and list files through authenticated API calls"
---

# File Endpoints

File endpoints expose a storage directory as a REST API: multipart upload, download by file id, delete and list. Allowed extensions, environments and the base directory are set per endpoint.

## Configuration

Each endpoint is defined in `endpoints/Files/{EndpointName}/entity.json`:

```json
{
  "StorageType": "Local",
  "BaseDirectory": "documents",
  "AllowedExtensions": [".pdf", ".docx", ".xlsx", ".txt"],
  "Hidden": false,
  "AllowedEnvironments": ["prod", "test"]
}
```

### Configuration properties

All properties, types and defaults: [Entity configuration](/reference/entity-config#file).

### Base directory placeholders

| Placeholder | Value |
|---|---|
| `{env}` | Environment name |
| `{year}` | Current year (`2025`) |
| `{month}` | Current month (`01` to `12`) |
| `{date}` | Current date (`2025-01-15`) |
| `{Header}` | Request [tenant header](/guide/tenant-headers) value |

```json
{ "BaseDirectory": "backups/{env}/{year}/{month}" }
```

With the default `FileStorage:StorageDirectory` (`storage/files`), this saves files to `storage/files/prod/backups/prod/2025/01/`. An absolute `BaseDirectory` (e.g. `/srv/exports/{env}`) saves files outside the storage directory.

Downloads, deletes and listings are restricted to the `BaseDirectory` segments before the first date placeholder. Tenant placeholders precede date placeholders:

```json
{ "BaseDirectory": "invoices/{X-Company-Id}/{year}", "Tenancy": { "X-Company-Id": "" } }
```

In this example, requests for tenant `ACME` are restricted to `invoices/ACME`.

### Storage behavior

| Aspect | Behavior |
|---|---|
| Upload | Saved to disk before the response is sent |
| Existing name | `409` unless `overwrite=true` |
| Memory cache | `FileStorage:UseMemoryCache` caches downloaded files; `isInMemoryOnly` in listings is always `false` |
| File id | Encrypted with `PORTWAY_ENCRYPTION_KEY`; rotating the key invalidates issued ids |
| Modified id | `400` |
| Id outside the endpoint's directory | `404` |

## Namespaces

An endpoint at `endpoints/Files/{Namespace}/{Name}/entity.json` (or with `Namespace` in `entity.json`) is served at `/api/{env}/files/{Namespace}/{Name}/...`. Upload, download, delete and list resolve the namespace, and returned URLs include it. Endpoints without a namespace use `/api/{env}/files/{Name}`. Reserved names and naming rules: [Namespaces](/reference/namespaces).

## API operations

### Upload a file

```http
POST /api/{env}/files/{EndpointName}
Authorization: Bearer YOUR_TOKEN
Content-Type: multipart/form-data

file=@report.pdf
```

```bash
curl -X POST "https://your-api/api/500/files/Documents" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -F "file=@report.pdf"
```

### List files

```http
GET /api/{env}/files/{EndpointName}/list
Authorization: Bearer YOUR_TOKEN
```

```json
{
  "success": true,
  "count": 1,
  "value": [
    {
      "fileId": "abc123fileId",
      "fileName": "report.pdf",
      "contentType": "application/pdf",
      "size": 125679,
      "lastModified": "2025-03-21T10:00:00Z",
      "url": "/api/500/files/Documents/abc123fileId",
      "isInMemoryOnly": false
    }
  ],
  "nextLink": null
}
```

### Download a file

The `fileId` comes from the upload or list response:

```http
GET /api/{env}/files/{EndpointName}/{fileId}
Authorization: Bearer YOUR_TOKEN
```

```bash
curl -X GET "https://your-api/api/500/files/Documents/abc123fileId" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -o "downloaded-report.pdf"
```

## File type restrictions

Only extensions in `AllowedExtensions` are accepted. The following extensions are always blocked: `.exe`, `.dll`, `.bat`, `.sh`, `.cmd`, `.msi`, `.vbs`.

The default maximum file size is 50MB (`FileStorage:MaxFileSizeBytes`).

## JavaScript integration

```javascript
// List files
const listResponse = await fetch('/api/500/files/Documents/list', {
  headers: { 'Authorization': 'Bearer ' + token }
});
const data = await listResponse.json();

// Download a file by ID
const fileId = data.value[0].fileId;
const fileResponse = await fetch(`/api/500/files/Documents/${fileId}`, {
  headers: { 'Authorization': 'Bearer ' + token }
});
const blob = await fileResponse.blob();

// Trigger browser download
const url = window.URL.createObjectURL(blob);
const a = document.createElement('a');
a.href = url;
a.download = data.value[0].fileName;
a.click();
window.URL.revokeObjectURL(url);
```

:::info
File endpoints require the `Authorization` header, so `<img src>` and `<embed src>` cannot load files directly.
:::

## Troubleshooting

| Symptom | Resolution |
|---|---|
| Empty file list | A folder named `{env}` means the placeholder was not resolved; move the files to the folder named after the environment. |
| "File size exceeds maximum" | Raise `FileStorage:MaxFileSizeBytes` or reduce the file size. |
| "Extension not allowed" | Add the extension to `AllowedExtensions`. |
| "File not found" on download | Use the `fileId` from a list response, the same environment, and a file that still exists. |

Upload and download events are logged in `log/portwayapi-[date].log`.

## Next steps

- [Environments](/guide/environments)
- [Security](/guide/security)
