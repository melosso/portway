#!/bin/bash

# Portway Demo Pull Script
# Run this on your remote server to download the demo configuration.

REPO_RAW_URL="https://raw.githubusercontent.com/melosso/portway/main/.github/demo"
TARGET_DIR="portway-demo"

echo "Creating directory structure in $TARGET_DIR..."
mkdir -p "$TARGET_DIR/config/environments/Demo"
mkdir -p "$TARGET_DIR/config/endpoints/Proxy/Accounts"
mkdir -p "$TARGET_DIR/config/endpoints/Proxy/Company"
mkdir -p "$TARGET_DIR/config/endpoints/Proxy/Products"
mkdir -p "$TARGET_DIR/config/endpoints/Proxy/Production"
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
    "config/environments/Demo/settings.json"
    "config/endpoints/Proxy/Accounts/entity.json"
    "config/endpoints/Proxy/Company/entity.json"
    "config/endpoints/Proxy/Products/entity.json"
    "config/endpoints/Proxy/Production/entity.json"
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
    RANDOM_KEY=$(cat /dev/urandom | tr -dc 'a-f0-8' | fold -w 64 | head -n 1)
fi

sed -i "s/demo-encryption-key-12345/$RANDOM_KEY/g" docker-compose.yml

# Optional: Prompt for domain or leave as default
echo "Portway is configured for: https://portway-demo.melosso.com"
echo "If you use a different domain, edit WebUi__PublicOrigins in docker-compose.yml"

echo ""
echo "Pull complete."
echo "Run 'cd $TARGET_DIR && docker compose up -d' to start the demo."
