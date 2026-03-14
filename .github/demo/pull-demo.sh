#!/bin/bash

# Portway Demo Pull Script
# Run this on your remote server to download the demo configuration.

REPO_RAW_URL="https://raw.githubusercontent.com/melosso/portway/main/.github/demo"
TARGET_DIR="portway-demo"

echo "Creating directory structure in $TARGET_DIR..."
mkdir -p "$TARGET_DIR/config/environments/WMS"
mkdir -p "$TARGET_DIR/config/endpoints/Proxy/Accounts"
mkdir -p "$TARGET_DIR/config/endpoints/Proxy/Products"
mkdir -p "$TARGET_DIR/config/endpoints/Proxy/Production"
mkdir -p "$TARGET_DIR/config/endpoints/SQL/WMS/Warehouses"
mkdir -p "$TARGET_DIR/tokens"
mkdir -p "$TARGET_DIR/log"
mkdir -p "$TARGET_DIR/data"
mkdir -p "$TARGET_DIR/keys"

cd "$TARGET_DIR" || exit

# List of files to download
files=(
    "docker-compose.yml"
    "nginx.conf"
    "config/environments/settings.json"
    "config/environments/WMS/settings.json"
    "config/endpoints/Proxy/Accounts/entity.json"
    "config/endpoints/Proxy/Products/entity.json"
    "config/endpoints/Proxy/Production/entity.json"
    "config/endpoints/SQL/WMS/Warehouses/entity.json"
    "data/demo.db"
)

for file in "${files[@]}"; do
    echo "Downloading $file..."
    curl -sSL "$REPO_RAW_URL/$file" -o "$file"
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
echo "If you use a different domain, edit WebUi__PublicOrigins in docker-compose.yml"

echo ""
echo "Pull complete."
echo "Run 'docker compose up -d' to start the demo."
