#!/usr/bin/env bash
set -euo pipefail

TOOLS_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

find "$TOOLS_ROOT" -mindepth 2 -maxdepth 2 -name ".publish.sh" | sort | while read -r script; do
    tool_name="$(basename "$(dirname "$script")")"
    echo ""
    echo "=== Publishing $tool_name ==="
    bash "$script"
done

echo ""
echo "All tool publish scripts completed."
