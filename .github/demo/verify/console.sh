#!/usr/bin/env bash
# usage: console.sh METHOD PATH [JSON]
set -euo pipefail
DOCKER=$(command -v podman || command -v docker)
$DOCKER exec test-portway sh -c '
J=/tmp/verify-cookies; B=http://127.0.0.1:8080
if [ ! -s $J ]; then
  C=$(curl -s -c $J -b $J $B/_/api/auth/csrf | sed -E "s/.*\"csrf\":\"([^\"]+)\".*/\1/")
  curl -s -c $J -b $J -X POST $B/_/api/auth -H "Content-Type: application/json" \
    -d "{\"username\":\"tester\",\"password\":\"Tester-Pass-2026!\",\"csrf\":\"$C\"}" >/dev/null
fi
X=$(awk "/portway_csrf/{print \$7}" $J | sed -e "s/%2B/+/g" -e "s/%2F/\//g" -e "s/%3D/=/g")
if [ -n "${3:-}" ]; then
  curl -s -b $J -c $J -X "$1" "$B$2" -H "Content-Type: application/json" -H "X-CSRF-Token: $X" -d "$3"
else
  curl -s -b $J -c $J -X "$1" "$B$2" -H "X-CSRF-Token: $X"
fi
' _ "$1" "$2" "${3:-}"
