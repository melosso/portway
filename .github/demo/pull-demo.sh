#!/bin/bash

# Pulls the demo files into the current directory

REPO_RAW_URL="https://raw.githubusercontent.com/melosso/portway/main/.github/demo"

echo "Creating directory structure in $(pwd)..."
mkdir -p "config/environments/WMS"
mkdir -p "config/endpoints/Proxy/CRM/Divisions/v1" "config/endpoints/Proxy/CRM/Divisions/v2"
mkdir -p "config/endpoints/Proxy/Warehouse/Products/v1" "config/endpoints/Proxy/Warehouse/Products/v2"
mkdir -p "config/endpoints/Proxy/Production/Orders/v1" "config/endpoints/Proxy/Production/Orders/v2"
mkdir -p "config/endpoints/SQL/Warehouse/Warehouses/v1" "config/endpoints/SQL/Warehouse/Warehouses/v2"
mkdir -p "config/endpoints/SQL/Warehouse/CountryWarehouses"
mkdir -p "config/endpoints/SQL/CRM/Accounts" "config/endpoints/SQL/CRM/Contacts" "config/endpoints/SQL/CRM/Opportunities" "config/endpoints/SQL/CRM/AccountTypes" "config/endpoints/SQL/CRM/Industries" "config/endpoints/SQL/CRM/ContactRoles" "config/endpoints/SQL/CRM/OpportunityStages"
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
    "config/endpoints/Proxy/CRM/Divisions/v1/entity.json"
    "config/endpoints/Proxy/CRM/Divisions/v2/entity.json"
    "config/endpoints/Proxy/Warehouse/Products/v1/entity.json"
    "config/endpoints/Proxy/Warehouse/Products/v2/entity.json"
    "config/endpoints/Proxy/Production/Orders/v1/entity.json"
    "config/endpoints/Proxy/Production/Orders/v2/entity.json"
    "config/endpoints/SQL/Warehouse/Warehouses/v1/entity.json"
    "config/endpoints/SQL/Warehouse/Warehouses/v2/entity.json"
    "config/endpoints/SQL/Warehouse/CountryWarehouses/entity.json"
    "config/endpoints/SQL/CRM/Accounts/entity.json"
    "config/endpoints/SQL/CRM/Contacts/entity.json"
    "config/endpoints/SQL/CRM/Opportunities/entity.json"
    "config/endpoints/SQL/CRM/AccountTypes/entity.json"
    "config/endpoints/SQL/CRM/Industries/entity.json"
    "config/endpoints/SQL/CRM/ContactRoles/entity.json"
    "config/endpoints/SQL/CRM/OpportunityStages/entity.json"
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
echo "Run './reset-demo.sh' to start the demo with sample traffic."
