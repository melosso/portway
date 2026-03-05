#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../../.." && pwd)"
TOOLS_DIR="$SCRIPT_DIR"
DEPLOYMENT_DIR="$ROOT_DIR/Deployment/PortwayApi/tools/Scalar"

if [ -d "$DEPLOYMENT_DIR" ]; then
    echo "Clearing deployment directory $DEPLOYMENT_DIR..."
    find "$DEPLOYMENT_DIR" -mindepth 1 -delete 2>/dev/null || true
    echo "Deployment directory cleared."
else
    mkdir -p "$DEPLOYMENT_DIR"
    echo "Created deployment directory $DEPLOYMENT_DIR"
fi

echo "Copying Scalar configuration files..."

FILES_TO_COPY=("Configure.bat" "Configure.ps1" "Configure.sh" "README.md" "ReadMe.md")

for file in "${FILES_TO_COPY[@]}"; do
    src="$TOOLS_DIR/$file"
    if [ -f "$src" ]; then
        cp -f "$src" "$DEPLOYMENT_DIR/"
        echo "  Copied $file"
    fi
done

echo "Scalar tool published successfully to $DEPLOYMENT_DIR"
