#!/bin/bash

# Resets demo data and config, cron: 0 */12 * * * /opt/portway-demo/reset-demo.sh > /opt/portway-demo/reset.log 2>&1

TARGET_DIR="portway-demo"
REPO_RAW_URL="https://raw.githubusercontent.com/melosso/portway/main/.github/demo"

fetch() {
    curl -fsSL "$REPO_RAW_URL/$1" -o "$1.tmp" && mv "$1.tmp" "$1" || { rm -f "$1.tmp"; echo "Failed to download $1"; return 1; }
}

# Determine directory
if [ -f "docker-compose.yml" ] && [ -d "data" ]; then
    BASE_DIR="."
elif [ -d "$TARGET_DIR" ]; then
    BASE_DIR="$TARGET_DIR"
else
    echo "Error: Could not find Portway demo installation."
    exit 1
fi

cd "$BASE_DIR" || exit

SELF="$(basename "$0")"
if [ -z "$PORTWAY_DEMO_SELF_UPDATED" ] && fetch "$SELF"; then
    chmod +x "$SELF"
    PORTWAY_DEMO_SELF_UPDATED=1 exec "./$SELF" "$@"
fi

echo "Stopping Portway Demo..."
docker compose down

echo "Cleaning up generated logs and tokens..."
rm -f log/* tokens/*

echo "Refreshing docker-compose.yml and nginx.conf, keeping the encryption key..."
KEY_LINE="$(grep -o 'PORTWAY_ENCRYPTION_KEY=.*' docker-compose.yml)"
fetch "docker-compose.yml" && [ -n "$KEY_LINE" ] && sed -i "s|PORTWAY_ENCRYPTION_KEY=.*|$KEY_LINE|" docker-compose.yml
fetch "nginx.conf"

echo "Resetting configuration and database from main repository..."
rm -rf config/endpoints
files=(
    "config/environments/settings.json"
    "config/environments/WMS/settings.json"
    "config/environments/network-access-policy.json"
    "config/endpoints/Proxy/CRM/Accounts/v1/entity.json"
    "config/endpoints/Proxy/CRM/Accounts/v2/entity.json"
    "config/endpoints/Proxy/Company/Companies/v1/entity.json"
    "config/endpoints/Proxy/Company/Companies/v2/entity.json"
    "config/endpoints/Proxy/Inventory/Products/v1/entity.json"
    "config/endpoints/Proxy/Inventory/Products/v2/entity.json"
    "config/endpoints/Proxy/Production/Orders/v1/entity.json"
    "config/endpoints/Proxy/Production/Orders/v2/entity.json"
    "config/endpoints/SQL/WMS/Warehouses/v1/entity.json"
    "config/endpoints/SQL/WMS/Warehouses/v2/entity.json"
    "config/endpoints/SQL/WMS/CountryWarehouses/entity.json"
    "data/demo.db"
    "data/auth.db"
)

for file in "${files[@]}"; do
    echo "Downloading $file..."
    mkdir -p "$(dirname "$file")"
    fetch "$file"
done

echo "Starting Portway Demo..."
docker compose up -d

echo "Sending sample traffic for the health page..."
for _ in $(seq 1 30); do
    curl -fs -o /dev/null http://localhost:8080/health/live && ls tokens/*.txt >/dev/null 2>&1 && break
    sleep 2
done
TOKEN="$(sed -n 's/.*"Token": *"\([^"]*\)".*/\1/p' tokens/*.txt | head -n 1)"
for path in CRM/Accounts v2/CRM/Accounts Company/Companies v2/Company/Companies Inventory/Products v2/Inventory/Products \
    Production/Orders v2/Production/Orders WMS/Warehouses v2/WMS/Warehouses v2/WMS/Warehouses?\$top=1; do
    for _ in 1 2 3; do
        curl -s -o /dev/null -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/WMS/$path"
    done
done
curl -s -o /dev/null -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"name":"New Account"}' http://localhost:8080/api/WMS/v2/CRM/Accounts
curl -s -o /dev/null -X PATCH -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"name":"Global Solutions"}' http://localhost:8080/api/WMS/v2/CRM/Accounts/01JNH6R020RJKTTWA0V4PC549M
curl -s -o /dev/null http://localhost:8080/api/WMS/v2/CRM/Accounts

echo "Demo reset successfully!"
