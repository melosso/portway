#!/usr/bin/env bash
# usage: start.sh [--build] [--image IMG] [--no-limit] [--traffic]
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/../../.." && pwd)
WORK=${PORTWAY_VERIFY_DIR:-/tmp/portway-verify}
IMAGE=localhost/portway:verify
BUILD=0; LIMIT=true; TRAFFIC=false
while [ $# -gt 0 ]; do
  case $1 in
    --build) BUILD=1 ;;
    --image) IMAGE=$2; shift ;;
    --no-limit) LIMIT=false ;;
    --traffic) TRAFFIC=true ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
  shift
done
DOCKER=$(command -v podman || command -v docker)
TOKEN=pwdemo_4819cfc3d3dff6f0f63e7160a6422abee1ae07dd

[ "$BUILD" = 1 ] && $DOCKER build -t "$IMAGE" "$ROOT"

$DOCKER rm -f test-portway test-nginx >/dev/null 2>&1 || true
rm -rf "$WORK"; mkdir -p "$WORK/log" "$WORK/keys"
cp -r "$ROOT/.github/demo/config" "$ROOT/.github/demo/data" "$ROOT/.github/demo/nginx.conf" "$WORK/"

# mcp writes and QUERY on two endpoints
python3 -I - "$WORK/config/endpoints" <<'EOF'
import json, sys
base = sys.argv[1]
for rel, methods in [("Proxy/CRM/Divisions/v2/entity.json", ["GET", "POST", "PUT", "DELETE"]),
                     ("SQL/Warehouse/Warehouses/v2/entity.json", ["GET", "QUERY"])]:
    path = f"{base}/{rel}"
    d = json.load(open(path))
    d.setdefault("Mcp", {}).update({"Exposed": True, "AllowedMethods": methods})
    json.dump(d, open(path, "w"), indent=4)
EOF

# expand fixtures and gated targets
python3 -I - "$WORK" <<'EOF'
import json, os, sqlite3, sys
work = sys.argv[1]
db = sqlite3.connect(f"{work}/data/demo.db")
db.executescript("""
CREATE TABLE IF NOT EXISTS Countries (Code TEXT PRIMARY KEY, Name TEXT NOT NULL, Continent TEXT NOT NULL, Secret TEXT NOT NULL);
DELETE FROM Countries;
INSERT INTO Countries VALUES ('NL','Netherlands','Europe','secret-nl'),('DE','Germany','Europe','secret-de'),('BE','Belgium','Europe','secret-be'),
  ('FR','France','Europe','secret-fr'),('GB','United Kingdom','Europe','secret-gb'),('US','United States','North America','secret-us'),('AU','Australia','Oceania','secret-au');
CREATE TABLE IF NOT EXISTS StockLevels (Id INTEGER PRIMARY KEY, WarehouseId INTEGER NOT NULL, ItemCode TEXT NOT NULL, Qty INTEGER NOT NULL);
DELETE FROM StockLevels;
INSERT INTO StockLevels VALUES (1,1,'CHAIR',40),(2,1,'DESK',12),(3,2,'CHAIR',7),(4,3,'LAMP',90),(5,999,'ORPHAN',1);
""")
db.commit()

def endpoint(rel, body):
    path = f"{work}/config/endpoints/SQL/{rel}/entity.json"
    os.makedirs(os.path.dirname(path), exist_ok=True)
    json.dump(body, open(path, "w"), indent=4)

countries = {"DatabaseObjectName": "Countries", "Namespace": "Reference", "AllowedEnvironments": ["WMS"], "AllowedMethods": ["GET"],
             "AllowedColumns": ["Code;code", "Name;name", "Continent;continent"], "Mcp": {"Exposed": True}}
endpoint("Reference/Countries", countries)
endpoint("Reference/HiddenCountries", {**countries, "Enabled": False})
endpoint("Reference/OtherEnvCountries", {**countries, "AllowedEnvironments": ["OTHER"]})
endpoint("Inventory/StockLevels", {
    "DatabaseObjectName": "StockLevels", "Namespace": "Inventory", "AllowedEnvironments": ["WMS"], "AllowedMethods": ["GET"],
    "AllowedColumns": ["Id;id", "WarehouseId;warehouseId", "ItemCode;itemCode", "Qty;qty"],
    "Relationships": [
        {"Name": "Warehouse", "Target": "Warehouse/Warehouses", "LocalColumn": "WarehouseId", "TargetColumn": "Id"},
        {"Name": "TenantWarehouse", "Target": "Warehouse/CountryWarehouses", "LocalColumn": "WarehouseId", "TargetColumn": "Id"}],
    "Mcp": {"Exposed": True}})

path = f"{work}/config/endpoints/SQL/Warehouse/Warehouses/v2/entity.json"
d = json.load(open(path))
d["Relationships"] = [
    {"Name": "CountryInfo", "Target": "Reference/Countries", "LocalColumn": "Country", "TargetColumn": "Code"},
    {"Name": "HiddenCountry", "Target": "Reference/HiddenCountries", "LocalColumn": "Country", "TargetColumn": "Code"},
    {"Name": "OtherEnvCountry", "Target": "Reference/OtherEnvCountries", "LocalColumn": "Country", "TargetColumn": "Code"}]
json.dump(d, open(path, "w"), indent=4)
EOF

$DOCKER network create portway-verify >/dev/null 2>&1 || true
$DOCKER run -d --name test-nginx --network portway-verify --network-alias nginx \
  -v "$WORK/nginx.conf:/etc/nginx/nginx.conf:ro,z" docker.io/library/nginx:alpine >/dev/null
$DOCKER run -d --name test-portway --network portway-verify -p 127.0.0.1:8081:8080 \
  -v "$WORK/config/environments:/app/environments:z" -v "$WORK/config/endpoints:/app/endpoints:z" \
  -v "$WORK/log:/app/log:z" -v "$WORK/data:/app/data:z" -v "$WORK/data/auth.db:/app/auth.db:z" -v "$WORK/keys:/app/.core:z" \
  -e PORTWAY_ENCRYPTION_KEY=demo-encryption-key-12345 -e "PORTWAY_ALLOWED_HOSTS=localhost;127.0.0.1" \
  -e PORTWAY_SEED_TOKEN=$TOKEN -e PORTWAY_MCP_ENABLED=true -e PORTWAY_PORT_CONSOLE=8080 \
  -e RateLimiting__Enabled=$LIMIT -e RequestTrafficLogging__Enabled=$TRAFFIC -e RequestTrafficLogging__Provider=SQLite \
  "$IMAGE" >/dev/null

for _ in $(seq 1 60); do curl -sf http://127.0.0.1:8081/health/live >/dev/null && break; sleep 1; done
curl -sf http://127.0.0.1:8081/health/live >/dev/null || { echo "instance did not start" >&2; $DOCKER logs test-portway | tail -20; exit 1; }

# console accepts local clients only
$DOCKER exec test-portway dotnet /app/PortwayApi.dll accounts create tester 'Tester-Pass-2026!' >/dev/null
console() { "$(dirname "$0")/console.sh" "$@"; }
scoped=$(console POST /_/api/tokens '{"username":"scoped-crm","allowed_scopes":"CRM/Accounts","allowed_environments":"*"}' | sed -E 's/.*"token":"([^"]+)".*/\1/')
tenant=$(console POST /_/api/tokens '{"username":"tenant-nl","allowed_scopes":"*","allowed_environments":"*","allowed_tenants":{"X-Country":["NL"]}}' | sed -E 's/.*"token":"([^"]+)".*/\1/')
stock=$(console POST /_/api/tokens '{"username":"scoped-stock","allowed_scopes":"Inventory/StockLevels","allowed_environments":"*"}' | sed -E 's/.*"token":"([^"]+)".*/\1/')
printf 'TOKEN=%s\nSCOPED=%s\nTENANT=%s\nSTOCK=%s\n' "$TOKEN" "$scoped" "$tenant" "$stock" > "$WORK/tokens.env"

echo "test instance on http://127.0.0.1:8081 (limit=$LIMIT traffic=$TRAFFIC, work dir $WORK)"
