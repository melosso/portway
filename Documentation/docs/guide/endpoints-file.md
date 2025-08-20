# File Endpoints

File endpoints let you store, retrieve, and manage files through simple API calls. Perfect for document management, image galleries, data imports, and file sharing in your applications.

## What You Can Do

- **Upload files** - Store documents, images, data files securely
- **Download files** - Retrieve files with a simple web request
- **Organize files** - Group files by type, environment, or purpose
- **Control access** - Restrict file types and environments
- **List files** - Browse available files with search capabilities

## Quick Start

### 1. Set Up a File Endpoint

Create a configuration file for your endpoint (e.g., `Documents`):

```json
{
  "StorageType": "Local",
  "BaseDirectory": "documents",
  "AllowedExtensions": [".pdf", ".docx", ".xlsx", ".txt"],
  "IsPrivate": false,
  "AllowedEnvironments": ["600", "700"]
}
```

### 2. Upload a File

```bash
curl -X POST "https://your-api.com/api/600/files/Documents" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -F "file=@report.pdf"
```

### 3. List Your Files

```bash
curl -X GET "https://your-api.com/api/600/files/Documents/list" \
  -H "Authorization: Bearer YOUR_TOKEN"
```

Response shows available files with download links:
```json
{
  "success": true,
  "files": [
    {
      "fileName": "report.pdf",
      "contentType": "application/pdf", 
      "size": 125679,
      "url": "/api/600/files/Documents/abc123fileId"
    }
  ]
}
```

### 4. Download a File

Use the `url` from the list response:

```bash
curl -X GET "https://your-api.com/api/600/files/Documents/abc123fileId" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -o "downloaded-report.pdf"
```

## Common Use Cases

### Document Repository
Store business documents like contracts, reports, and manuals.

**Configuration:**
```json
{
  "StorageType": "Local",
  "BaseDirectory": "documents", 
  "AllowedExtensions": [".pdf", ".docx", ".xlsx", ".txt"],
  "AllowedEnvironments": ["600", "700"]
}
```

**Example:** Upload a contract, share download link with team members.

### Image Gallery
Store and serve product images, logos, and marketing materials.

**Configuration:**
```json
{
  "StorageType": "Local",
  "BaseDirectory": "images",
  "AllowedExtensions": [".jpg", ".png", ".gif", ".svg"],
  "AllowedEnvironments": ["600", "700"]  
}
```

**Example:** Upload product photos that display directly in your website.

### Data Exchange
Import/export data files for business processes.

**Configuration:**
```json
{
  "StorageType": "Local",
  "BaseDirectory": "data-import",
  "AllowedExtensions": [".csv", ".xlsx", ".json"],
  "AllowedEnvironments": ["600", "700"]
}
```

**Example:** Upload customer CSV files for import processing.

### Secure Reports
Store sensitive internal reports with restricted access.

**Configuration:**
```json
{
  "StorageType": "Local", 
  "BaseDirectory": "reports",
  "AllowedExtensions": [".pdf", ".xlsx"],
  "IsPrivate": true,
  "AllowedEnvironments": ["600"]
}
```

**Example:** Quarterly financial reports available only in production environment.

## Configuration Options

| Setting | Purpose | Example |
|---------|---------|---------|
| `BaseDirectory` | Organize files in folders | `"documents"`, `"images/{env}"` |
| `AllowedExtensions` | Security - control file types | `[".pdf", ".jpg", ".csv"]` |
| `AllowedEnvironments` | Control which environments can access | `["600"]` for production only |
| `IsPrivate` | Hide from public API documentation | `true` for internal endpoints |

### Smart Folder Organization

Use placeholders for automatic organization:

```json
{
  "BaseDirectory": "backups/{env}/{year}/{month}"
}
```

Files automatically organized like:
- `backups/600/2025/01/database-backup.sql`
- `backups/700/2025/01/test-backup.sql`

Available placeholders:
- `{env}` - Environment (600, 700, etc.)
- `{year}` - Current year (2025)
- `{month}` - Current month (01, 02, etc.)
- `{date}` - Current date (2025-01-15)

## Supported File Types

Common file types work automatically with proper browser handling:

| File Type | Extensions | Browser Behavior |
|-----------|------------|------------------|
| **Documents** | `.pdf`, `.docx`, `.xlsx` | PDFs display inline, others download |
| **Images** | `.jpg`, `.png`, `.gif`, `.svg` | Show directly in browser |
| **Data Files** | `.csv`, `.json`, `.xml` | Download for processing |
| **Text Files** | `.txt`, `.log` | Display inline as text |

## Web Integration

### Display Images Directly
```html
<!-- Note: This requires authentication headers to work -->
<img src="https://your-api.com/api/600/files/Images/imageFileId" 
     alt="Product Photo" />
<!-- For web apps, you'll need to handle authentication via JavaScript -->
```

### Embed PDF Documents
```html
<!-- Note: This requires authentication headers to work -->
<embed src="https://your-api.com/api/600/files/Documents/pdfFileId"
       type="application/pdf" 
       width="800" height="600" />
<!-- For web apps, you'll need to handle authentication via JavaScript -->
```

### Download Files with JavaScript
```javascript
// Get file list
const response = await fetch('/api/600/files/Documents/list', {
  headers: { 'Authorization': 'Bearer ' + token }
});
const data = await response.json();

// Download first file using the fileId from the response
const fileId = data.files[0].fileId;
const downloadUrl = `/api/600/files/Documents/${fileId}`;

// Download file
const fileResponse = await fetch(downloadUrl, {
  headers: { 'Authorization': 'Bearer ' + token }
});
const blob = await fileResponse.blob();

// Trigger download
const url = window.URL.createObjectURL(blob);
const a = document.createElement('a');
a.href = url;
a.download = data.files[0].fileName;
a.click();
window.URL.revokeObjectURL(url);
```

## Security & Access Control

### File Type Restrictions
Always specify allowed file types for security:

```json
"AllowedExtensions": [".pdf", ".jpg", ".png"]  // Only these types allowed
```

### Environment Control
Separate files by environment:
- **Development/Testing:** Environment `700`
- **Production:** Environment `600`
- **Sensitive Data:** Restrict to `["600"]` only

### Size Limits
- Default maximum file size: **50MB**
- Configurable in system settings
- Large files automatically rejected

### Blocked File Types
These dangerous file types are always blocked:
`.exe`, `.dll`, `.bat`, `.sh`, `.cmd`, `.msi`, `.vbs`

## Best Practices

### 1. Name Endpoints Clearly
```
✓ ProductImages, CustomerReports, InvoiceDocuments
✗ Files, Data, Stuff
```

### 2. Organize with Folders
```json
"BaseDirectory": "invoices/2025"     // ✓ Organized by purpose and year
"BaseDirectory": ""                  // ✗ Everything mixed together
```

### 3. Restrict File Types
```json
"AllowedExtensions": [".pdf", ".jpg"]  // ✓ Only what you need
"AllowedExtensions": []                // ✗ Allows everything (risky)
```

### 4. Use Environment Restrictions
```json
"AllowedEnvironments": ["600"]         // ✓ Production-only for sensitive files
"AllowedEnvironments": ["600", "700"]  // ✓ Both environments for general files
```

## Troubleshooting

### Files Not Showing Up
**Problem:** File list returns empty but files exist.

**Solution:** Check if your `BaseDirectory` uses placeholders like `{env}`. The system might have created literal placeholder folders instead of processing them.

**Fix:** If you see a folder literally named `{env}`, move files from `files/customer-data/{env}/` to `files/600/customer-data/600/`

### Upload Fails - File Too Large
**Problem:** "File size exceeds maximum allowed size"

**Solution:** File is larger than 50MB limit. Contact administrator to increase limit or compress the file.

### Upload Fails - File Type Not Allowed  
**Problem:** "Files with extension .exe are not allowed"

**Solution:** Check the `AllowedExtensions` list in your endpoint configuration. Add the extension if safe, or convert to an allowed format.

### Can't Download File
**Problem:** File not found or access denied.

**Solutions:**
- Verify you're using the correct `fileId` from the list response
- Check you're accessing the right environment (600 vs 700)
- Ensure your token has proper permissions
- Confirm file hasn't been deleted

## Getting Help

### Check File Locations
Files are stored in predictable locations:
```
files/{environment}/{baseDirectory}/{filename}
```

### View System Logs
Application logs show detailed information about file operations:
```
log/portwayapi-[date].log
```

### Test Basic Operations
Use simple commands to verify functionality:
```bash
# Test upload
curl -X POST https://your-api/api/600/files/Documents \
  -H "Authorization: Bearer TOKEN" -F "file=@test.txt"

# Test listing  
curl -X GET https://your-api/api/600/files/Documents/list \
  -H "Authorization: Bearer TOKEN"
```

## Next Steps

1. **Plan your file organization** - Decide on endpoint names and folder structure
2. **Set up configurations** - Create endpoint definitions for each file type
3. **Test with sample files** - Upload and download test files to verify setup
4. **Integrate with applications** - Add file operations to your business processes
5. **Monitor usage** - Track file storage and access patterns