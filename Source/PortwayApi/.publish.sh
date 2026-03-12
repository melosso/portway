#!/usr/bin/env bash
set -euo pipefail

# ── Health checks ─────────────────────────────────────────────────────────────
if ! command -v dotnet &>/dev/null; then
    echo "ERROR: dotnet SDK not found. Install it from https://dot.net and re-run."
    exit 1
fi

# ── Paths ─────────────────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
DEPLOYMENT_PATH="$ROOT_DIR/Deployment/PortwayApi"
SOURCE_PROJECT_PATH="$SCRIPT_DIR"

# ── Deployment directory ───────────────────────────────────────────────────────
if [ ! -d "$DEPLOYMENT_PATH" ]; then
    echo "Creating deployment folder at $DEPLOYMENT_PATH..."
    mkdir -p "$DEPLOYMENT_PATH"
else
    echo "Removing existing deployment folder contents..."
    find "$DEPLOYMENT_PATH" -mindepth 1 -delete 2>/dev/null || true
fi

# ── Publish ───────────────────────────────────────────────────────────────────
echo "Publishing application..."
dotnet publish "$SOURCE_PROJECT_PATH" -c Release -r win-x64 --self-contained false -o "$DEPLOYMENT_PATH"

# ── Remove dev files ──────────────────────────────────────────────────────────
echo "Removing development files..."
find "$DEPLOYMENT_PATH" \( \
    -name "*.pdb" \
    -o -name "appsettings.Development.json" \
    -o -name "*.publish.ps1" \
    -o -name "*.publish.sh" \
    -o -name "*.db" \
    -o -name ".gitattributes" \
    -o -name ".gitignore" \
\) -delete 2>/dev/null || true

# Remove .git folder
if [ -d "$DEPLOYMENT_PATH/.git" ]; then
    echo "Removing .git folder..."
    rm -rf "$DEPLOYMENT_PATH/.git"
fi

# Remove tokens folder(s)
find "$DEPLOYMENT_PATH" -type d -name "tokens" -exec rm -rf {} + 2>/dev/null || true

# ── Remove XML docs (keep Endpoints content) ──────────────────────────────────
find "$DEPLOYMENT_PATH" -name "*.xml" \
    ! -path "*/Endpoints/*" \
    ! -path "*/endpoints/*" \
    -delete 2>/dev/null || true

# ── Remove localised SqlClient resource folders (keep en / nl) ────────────────
find "$DEPLOYMENT_PATH" -mindepth 1 -maxdepth 1 -type d | while read -r dir; do
    name="$(basename "$dir")"
    if [ "$name" != "en" ] && [ "$name" != "nl" ]; then
        if [ -f "$dir/Microsoft.Data.SqlClient.resources.dll" ]; then
            rm -rf "$dir"
        fi
    fi
done

# ── Process Proxy endpoints ───────────────────────────────────────────────────
ENDPOINTS_PATH="$DEPLOYMENT_PATH/Endpoints"
PROXY_PATH="$ENDPOINTS_PATH/Proxy"

if [ -d "$PROXY_PATH" ]; then
    # Copy POST.txt → entity.example inside every examples/example folder under Proxy
    find "$PROXY_PATH" -type d \( -name "examples" -o -name "example" \) | while read -r ex_dir; do
        post_file="$ex_dir/POST.txt"
        if [ -f "$post_file" ]; then
            parent_dir="$(dirname "$ex_dir")"
            cp -f "$post_file" "$parent_dir/entity.example"
        fi
    done

    # Remove all examples folders under Endpoints
    find "$ENDPOINTS_PATH" -type d -name "examples" -exec rm -rf {} + 2>/dev/null || true
    # Remove all definitions folders under Endpoints
    find "$ENDPOINTS_PATH" -type d -name "definitions" -exec rm -rf {} + 2>/dev/null || true
fi

# ── Generate web.config ───────────────────────────────────────────────────────
WEB_CONFIG_PATH="$DEPLOYMENT_PATH/web.config"
cat > "$WEB_CONFIG_PATH" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\PortwayApi.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
    </system.webServer>
  </location>
  <system.webServer>
    <defaultDocument>
      <files>
        <clear />
        <add value="index.html" />
      </files>
    </defaultDocument>
    <httpProtocol>
      <customHeaders>
        <!-- Stop IIS from adding these headers, since Portway already adds them. So removing them here prevents duplicates. -->
        <remove name="X-Powered-By" />
        <remove name="X-Content-Type-Options" />
        <remove name="X-Frame-Options" />
        <remove name="Strict-Transport-Security" />
        <remove name="Referrer-Policy" />
        <remove name="Permissions-Policy" />
        <remove name="Content-Security-Policy" />
      </customHeaders>
    </httpProtocol>
  </system.webServer>
</configuration>
XML

# ── LICENSE ───────────────────────────────────────────────────────────────────
LICENSE_DIR="$DEPLOYMENT_PATH/license"
mkdir -p "$LICENSE_DIR"

LICENSE_SRC="$ROOT_DIR/LICENSE"
if [ -f "$LICENSE_SRC" ]; then
    echo "Copying LICENSE file..."
    cp -f "$LICENSE_SRC" "$LICENSE_DIR/license.txt"
fi

echo "https://github.com/melosso/portway" > "$LICENSE_DIR/source.txt"

# ── .gitignore auto-heal ──────────────────────────────────────────────────────
GITIGNORE_PATH="$ROOT_DIR/.gitignore"
touch "$GITIGNORE_PATH"

add_if_missing() {
    grep -qxF "$1" "$GITIGNORE_PATH" || echo "$1" >> "$GITIGNORE_PATH"
}

add_if_missing "# Ignore log files"
add_if_missing "*.log"
add_if_missing "/logs/"

echo ".gitignore updated to exclude log files and /logs/ directory"
echo "Deployment complete. Published to $DEPLOYMENT_PATH"
echo "web.config generated at $WEB_CONFIG_PATH"
