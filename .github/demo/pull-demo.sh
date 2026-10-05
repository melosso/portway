#!/bin/bash

# Pulls the demo files into the current directory

REPO_RAW_URL="https://raw.githubusercontent.com/melosso/portway/main/.github/demo"

echo "Creating directory structure in $(pwd)..."
mkdir -p "config/environments/WMS"
mkdir -p "config/endpoints/Proxy/CRM/Accounts"
mkdir -p "config/endpoints/Proxy/Company/Companies"
mkdir -p "config/endpoints/Proxy/Inventory/Products/v2"
mkdir -p "config/endpoints/Proxy/Production/Orders"
mkdir -p "config/endpoints/SQL/WMS/Warehouses"
mkdir -p "config/endpoints/SQL/WMS/CountryWarehouses"
mkdir -p "tokens"
mkdir -p "log"
mkdir -p "data"
mkdir -p "keys"

# List of files to download
files=(
    "docker-compose.yml"
    "nginx.conf"
    "config/environments/settings.json"
    "config/environments/WMS/settings.json"
    "config/environments/network-access-policy.json"
    "config/endpoints/Proxy/CRM/Accounts/entity.json"
    "config/endpoints/Proxy/Company/Companies/entity.json"
    "config/endpoints/Proxy/Inventory/Products/entity.json"
    "config/endpoints/Proxy/Inventory/Products/v2/entity.json"
    "config/endpoints/Proxy/Production/Orders/entity.json"
    "config/endpoints/SQL/WMS/Warehouses/entity.json"
    "config/endpoints/SQL/WMS/CountryWarehouses/entity.json"
    "data/demo.db"
    "data/auth.db"
)

for file in "${files[@]}"; do
    echo "Downloading $file..."
    curl -fsSL "$REPO_RAW_URL/$file" -o "$file" || echo "Failed to download $file"
done

# Randomize the encryption key
echo "Randomizing Portway encryption key..."
if command -v openssl >/dev/null 2>&1; then
    RANDOM_KEY=$(openssl rand -hex 32)
else
    RANDOM_KEY=$(tr -dc 'a-f0-9' < /dev/urandom | fold -w 64 | head -n 1)
fi

# Substitute the key into docker-compose.yml
sed -i "s|PORTWAY_ENCRYPTION_KEY=.*|PORTWAY_ENCRYPTION_KEY=$RANDOM_KEY|" docker-compose.yml

# Optional: Prompt for domain or leave as default
echo "Portway is configured for: https://portway-demo.melosso.com"
echo "If you use a different domain, edit PORTWAY_PUBLIC_ORIGINS in docker-compose.yml"

echo ""
echo "Pull complete."
echo "Run 'docker compose up -d' to start the demo."
