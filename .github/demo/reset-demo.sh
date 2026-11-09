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
    mkdir -p "$(dirname "$file")"
    fetch "$file"
done

echo "Starting Portway Demo..."
docker compose up -d

echo "Sending sample traffic for the health page..."
for _ in $(seq 1 30); do
    curl -fs -o /dev/null http://localhost:8080/health/live && break
    sleep 2
done
TOKEN="$(sed -n 's/.*PORTWAY_SEED_TOKEN=//p' docker-compose.yml)"
echo "Granting X-Country tenants to the seeded token..."
docker exec demo-portway sqlite3 /app/auth.db "UPDATE Tokens SET AllowedTenants='{\"X-Country\":[\"NL\",\"DE\",\"US\"]}';"
for path in CRM/Accounts CRM/Contacts CRM/Opportunities CRM/Opportunities?\$expand=Account,Contact,Stage CRM/Divisions v2/CRM/Divisions Warehouse/Products v2/Warehouse/Products \
    Production/Orders v2/Production/Orders Warehouse/Warehouses v2/Warehouse/Warehouses v2/Warehouse/Warehouses?\$top=1; do
    for _ in 1 2 3; do
        curl -s -o /dev/null -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/WMS/$path"
    done
done
curl -s -o /dev/null -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"code":"BR-VIE","name":"Branch Vienna","country":"AT"}' http://localhost:8080/api/WMS/v2/CRM/Divisions
curl -s -o /dev/null -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"code":"HQ-AMS","name":"Head office Amsterdam"}' http://localhost:8080/api/WMS/v2/CRM/Divisions/01JCZ92WX0CH9MDSEQSXFB9NNB
curl -s -o /dev/null http://localhost:8080/api/WMS/v2/CRM/Divisions
curl -s -o /dev/null -H "Authorization: Bearer $TOKEN" -H "X-Country: NL" http://localhost:8080/api/WMS/Warehouse/CountryWarehouses
curl -s -o /dev/null -X QUERY -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "{\"filter\":\"country eq 'NL'\",\"top\":5}" http://localhost:8080/api/WMS/v2/Warehouse/Warehouses

echo "Demo reset successfully!"
