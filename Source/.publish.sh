#!/usr/bin/env bash
set -euo pipefail

SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Run PortwayApi first, then Tools (which fans out to its own sub-scripts)
SCRIPTS=(
    "$SOURCE_DIR/PortwayApi/.publish.sh"
    "$SOURCE_DIR/Tools/.publish.sh"
)

for script in "${SCRIPTS[@]}"; do
    if [ -f "$script" ]; then
        name="$(basename "$(dirname "$script")")"
        echo ""
        echo "════════════════════════════════════════"
        echo " Publishing $name"
        echo "════════════════════════════════════════"
        bash "$script"
    else
        echo "Warning: publish script not found at $script"
    fi
done

echo ""
echo "All publish scripts completed."
