#!/usr/bin/env bash
set -euo pipefail

# ── Health checks ─────────────────────────────────────────────────────────────
 if ! command -v dotnet &>/dev/null; then
    echo "ERROR: dotnet SDK not found. Install it from https://dot.net and re-run."
    exit 1
fi

# ── Paths ─────────────────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../../.." && pwd)"
TOOLS_DIR="$SCRIPT_DIR"
DEPLOYMENT_DIR="$ROOT_DIR/Deployment/PortwayApi/tools/TokenGenerator"
FINAL_DESTINATION="$(dirname "$(dirname "$DEPLOYMENT_DIR")")"   # Deployment/PortwayApi

# ── Deployment directory ───────────────────────────────────────────────────────
if [ -d "$DEPLOYMENT_DIR" ]; then
    echo "Clearing deployment directory $DEPLOYMENT_DIR..."
    find "$DEPLOYMENT_DIR" -mindepth 1 -delete 2>/dev/null || true
    echo "Deployment directory cleared."
else
    mkdir -p "$DEPLOYMENT_DIR"
    echo "Created deployment directory $DEPLOYMENT_DIR"
fi

# ── Clean ─────────────────────────────────────────────────────────────────────
dotnet clean "$TOOLS_DIR" -c Release

rm -rf "$TOOLS_DIR/obj" "$TOOLS_DIR/bin"

# ── TrimmerRoots.xml ──────────────────────────────────────────────────────────
TRIMMER_ROOTS_PATH="$TOOLS_DIR/TrimmerRoots.xml"
cat > "$TRIMMER_ROOTS_PATH" <<'XML'
<linker>
  <assembly fullname="System.Text.Json" preserve="all" />
  <assembly fullname="System.Text.Json.Serialization" preserve="all" />
  <assembly fullname="Microsoft.Extensions.Configuration.Json" preserve="all" />
  <assembly fullname="Microsoft.EntityFrameworkCore.Sqlite" preserve="all" />
</linker>
XML
echo "Created TrimmerRoots.xml to preserve JSON functionality"

# ── Publish ───────────────────────────────────────────────────────────────────
echo "Publishing optimized framework-dependent version..."
dotnet publish "$TOOLS_DIR" \
    -c Release \
    -r win-x64 \
    --self-contained false \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:TrimmerRootDescriptor="$TRIMMER_ROOTS_PATH" \
    -o "$DEPLOYMENT_DIR"

rm -f "$TRIMMER_ROOTS_PATH"

# ── Remove unnecessary files ──────────────────────────────────────────────────
echo "Removing unnecessary files..."
find "$DEPLOYMENT_DIR" \( \
    -name "*.pdb" \
    -o -name "*.xml" \
    -o -name "*.deps.json" \
    -o -name "*.dev.json" \
\) -delete 2>/dev/null || true

# ── Report size ───────────────────────────────────────────────────────────────
BINARY="$DEPLOYMENT_DIR/TokenGenerator.exe"
if [ -f "$BINARY" ]; then
    SIZE_BYTES="$(stat -c%s "$BINARY")"
    SIZE_MB="$(awk "BEGIN {printf \"%.2f\", $SIZE_BYTES/1048576}")"
    echo "Success: TokenGenerator published successfully to $DEPLOYMENT_DIR"
    echo "   - Size: ${SIZE_MB} MB"

    # ── Move and rename to PortwayMgt.exe ─────────────────────────────────────
    FINAL_BINARY="$FINAL_DESTINATION/PortwayMgt.exe"
    mv -f "$BINARY" "$FINAL_BINARY"
    echo "Moved and renamed binary to $FINAL_BINARY"
else
    echo "Warning: Published binary not found at $BINARY"
fi

# ── Cleanup temp dir ──────────────────────────────────────────────────────────
if [ -d "$DEPLOYMENT_DIR" ]; then
    rm -rf "$DEPLOYMENT_DIR"
    echo "Removed temporary deployment directory $DEPLOYMENT_DIR"
fi
