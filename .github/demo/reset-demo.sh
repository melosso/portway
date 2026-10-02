#!/bin/bash

# Resets demo data and config, cron: 0 */12 * * * /opt/portway-demo/reset-demo.sh > /opt/portway-demo/reset.log 2>&1

TARGET_DIR="portway-demo"
REPO_RAW_URL="https://raw.githubusercontent.com/melosso/portway/main/.github/demo"

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

echo "Stopping Portway Demo..."
docker compose down

echo "Cleaning up generated logs..."
rm -f log/*

echo "Resetting configuration and database from main repository..."
files=(
    "config/environments/settings.json"
    "config/environments/WMS/settings.json"
    "config/environments/network-access-policy.json"
    "config/endpoints/Proxy/Accounts/entity.json"
    "config/endpoints/Proxy/Company/entity.json"
    "config/endpoints/Proxy/Products/entity.json"
    "config/endpoints/Proxy/Production/entity.json"
    "config/endpoints/SQL/WMS/Warehouses/entity.json"
    "config/endpoints/SQL/WMS/CountryWarehouses/entity.json"
    "data/demo.db"
    "data/auth.db"
)

for file in "${files[@]}"; do
    echo "Downloading $file..."
    mkdir -p "$(dirname "$file")"
    curl -sSL "$REPO_RAW_URL/$file" -o "$file"
done

echo "Starting Portway Demo..."
docker compose up -d

echo "Demo reset successfully!"