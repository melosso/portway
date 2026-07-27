---
title: File Endpoints
description: "Upload, download, and list files through authenticated API calls"
---

# File Endpoints

File endpoints expose a storage directory as a REST API, so your callers can upload files via multipart form POST, retrieve them by file ID, and list what's available per endpoint. File type restrictions, environment scoping, and the base directory all live in the endpoint configuration.

## Configuration

You define one with `endpoints/File/{EndpointName}/entity.json`:

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


Every property this endpoint type accepts, with its type and default, is listed in [Entity configuration](/reference/entity-config#endpoint-files).

### Base directory placeholders

`BaseDirectory` supports dynamic path segments:

| Placeholder | Resolves to |
|---|---|
| `{env}` | Environment name |
| `{year}` | Current year (`2025`) |
| `{month}` | Current month (`01` to `12`) |
| `{date}` | Current date (`2025-01-15`) |

```json
{ "BaseDirectory": "backups/{env}/{year}/{month}" }
```

Files land at paths like `files/prod/2025/01/database-backup.sql`.

## Namespaces

File endpoints support namespaces. Place the endpoint under `endpoints/Files/{Namespace}/{Name}/entity.json` (or set `Namespace` in `entity.json`) and it is served at `/api/{env}/files/{Namespace}/{Name}/...`. All operations (upload, download, delete, list) resolve the namespace, and returned download URLs include it so they round-trip. Non-namespaced endpoints keep their existing `/api/{env}/files/{Name}` URLs. A few names are reserved and cannot be used, which [Namespaces](/reference/namespaces) covers along with the rest of the naming rules.

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

Use the `fileId` from the list response:

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

Specify which extensions callers can upload via `AllowedExtensions`. Any extension not listed is rejected.

Regardless of `AllowedExtensions`, the following types are always blocked:
`.exe`, `.dll`, `.bat`, `.sh`, `.cmd`, `.msi`, `.vbs`

The default maximum file size is **50MB**. This is configurable in system settings.

## JavaScript integration

```javascript
// List files
const listResponse = await fetch('/api/500/files/Documents/list', {
  headers: { 'Authorization': 'Bearer ' + token }
});
const data = await listResponse.json();

// Download a file by ID
const fileId = data.files[0].fileId;
const fileResponse = await fetch(`/api/500/files/Documents/${fileId}`, {
  headers: { 'Authorization': 'Bearer ' + token }
});
const blob = await fileResponse.blob();

// Trigger browser download
const url = window.URL.createObjectURL(blob);
const a = document.createElement('a');
a.href = url;
a.download = data.files[0].fileName;
a.click();
window.URL.revokeObjectURL(url);
```

:::info
File endpoints require the `Authorization` header. Direct `<img src>` or `<embed src>` tags in HTML will not work unless authentication is handled via JavaScript.
:::

## Troubleshooting

**File list returns empty**: If `BaseDirectory` uses `{env}`, verify Portway created the correct path. A literal folder named `{env}` indicates the placeholder was not resolved. Move files to the correct path under the actual environment name.

**"File size exceeds maximum"**: The file exceeds the 50MB default. Either compress the file or increase the limit in system settings.

**"Extension not allowed"**: Add the extension to `AllowedExtensions` in the endpoint config, or convert the file to an allowed format.

**"File not found" on download**: Confirm you are using the `fileId` from a list response (not the filename), that you are requesting from the correct environment, and that the file has not been deleted.

Files are stored at predictable paths: `files/{environment}/{baseDirectory}/{filename}`. Application logs at `log/portwayapi-[date].log` record upload and download events.

## Next steps

- [Environments](/guide/environments)
- [Security](/guide/security)
