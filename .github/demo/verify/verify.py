"""Checks the test instance from start.sh. Exits with code 1 on a failed check."""

import hashlib
import hmac
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = "http://127.0.0.1:" + os.environ.get("PORTWAY_VERIFY_PORT", "8081")
WORK = os.environ.get("PORTWAY_VERIFY_DIR", "/tmp/portway-verify")
TOKENS = dict(line.strip().split("=", 1) for line in open(f"{WORK}/tokens.env") if "=" in line)
DOCKER = shutil.which("podman") or shutil.which("docker")
MCP_HEADERS = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}

WAREHOUSES = "/api/WMS/v2/Warehouse/Warehouses"
TENANT_WAREHOUSES = "/api/WMS/Warehouse/CountryWarehouses"
STOCK = "/api/WMS/Inventory/StockLevels"
WEBHOOK = "/api/WMS/Events/Inbound"
WEBHOOK_SECRET = "demo-github-webhook-secret"

results = []


def http(path, token="TOKEN", method="GET", body=None, headers=None):
    request = urllib.request.Request(BASE + path, method=method, data=body.encode() if body else None)
    if token:
        request.add_header("Authorization", "Bearer " + TOKENS.get(token, token))
    for name, value in (headers or {}).items():
        request.add_header(name, value)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.status, response.read().decode(), dict(response.headers)
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode(), dict(error.headers)


def odata(filter=None, token="TOKEN", headers=None, path=WAREHOUSES, **options):
    params = {"$count": "true", "$top": "0", **({"$filter": filter} if filter else {}), **options}
    status, body, _ = http(path + "?" + urllib.parse.urlencode(params, quote_via=urllib.parse.quote), token, headers=headers)
    try:
        return status, json.loads(body)
    except ValueError:
        return status, {}


def mcp(tool, arguments, token="TOKEN"):
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"name": tool, "arguments": arguments}})
    _, text, _ = http("/mcp", token, "POST", body, MCP_HEADERS)
    for line in text.splitlines():
        if line.startswith("data: "):
            result = json.loads(line[6:]).get("result", {})
            return result.get("content", [{}])[0].get("text", ""), result.get("isError", False)
    return text, True


def traffic_count(where):
    query = f"SELECT count(*) FROM TrafficLogs WHERE {where}"
    out = subprocess.run([DOCKER, "exec", "test-portway", "sqlite3", "/app/log/traffic_logs.db", query],
                         capture_output=True, text=True)
    return out.stdout.strip() if out.returncode == 0 else None


def nginx_hits(since, needle):
    out = subprocess.run([DOCKER, "logs", "--since", since, "test-nginx"], capture_output=True, text=True)
    return sum(needle in line for line in (out.stdout + out.stderr).splitlines())


def console(method, path, body=None):
    script = os.path.join(os.path.dirname(os.path.abspath(__file__)), "console.sh")
    out = subprocess.run([script, method, path] + ([body] if body else []), capture_output=True, text=True)
    try:
        return json.loads(out.stdout)
    except ValueError:
        return {"raw": out.stdout}


def github(body, secret=WEBHOOK_SECRET, delivery=None):
    signature = "sha256=" + hmac.new(secret.encode(), body.encode(), hashlib.sha256).hexdigest()
    headers = {"Content-Type": "application/json", "X-Hub-Signature-256": signature,
               "X-GitHub-Delivery": delivery or f"verify-{time.time_ns()}"}
    return http(f"{WEBHOOK}/github", None, "POST", body, headers)


def check(area, name, ok, actual=""):
    results.append((area, name, bool(ok), actual))


def countries(data):
    return {row.get("Country") for row in data.get("value", [])}


# odata filters apply or return 400
for expression, expected in [("'NL' eq country", 2), ("null eq country", 0), ("-capacityM2 lt 0", 13), ("code eq code", 13)]:
    s, d = odata(expression)
    check("odata", f"$filter={expression} returns {expected} rows or 400",
          s == 400 or (s == 200 and d.get("totalCount") == expected), f"{s} totalCount={d.get('totalCount')}")
for expression in ["1 eq 1", "code eq 'x' or 1 eq 1", "not (1 eq 2)", "1 add 1 eq 2", "capacityM2 add 1 gt 100"]:
    s, _ = odata(expression)
    check("odata", f"$filter={expression} returns 200 or 400", s in (200, 400), s)
s, _ = odata(None, **{"$orderby": "1"})
check("odata", "$orderby=1 returns 200 or 400", s in (200, 400), s)
s, d = odata("Foo eq")
check("odata", "$filter=Foo eq returns a syntax error", s == 400 and "Columns not allowed" not in d.get("error", ""),
      f"{s} {d.get('error')}")
s, _ = odata(None, **{"$top": "-1"})
check("odata", "$top=-1 returns 400", s == 400, s)
s, _ = odata(None, **{"$skip": "-1"})
check("odata", "$skip=-1 returns 400", s == 400, s)
s, d = odata("country eq 'NL'")
check("odata", "country eq 'NL' returns 2 rows", s == 200 and d.get("totalCount") == 2, f"{s} {d.get('totalCount')}")
s, _ = odata("country in ('NL','DE')")
check("odata", "in operator returns 200", s == 200, s)

# traffic log needs start.sh --traffic
probe = "querysecret" + str(int(time.time()))
http(f"{WAREHOUSES}?$top=1&apikey={probe}")
http("/api/WMS/v2/CRM/Divisions?$top=1")
time.sleep(6)
leaked = traffic_count(f"QueryString LIKE '%{probe}%' OR TargetUrl LIKE '%{probe}%'")
if leaked is None:
    check("traffic", "traffic log is enabled (start.sh --traffic)", False)
else:
    check("traffic", "traffic entries contain no query values", leaked == "0", f"{leaked} rows contain the value")
    targets = traffic_count("Path LIKE '%/CRM/Divisions%' AND TargetUrl LIKE 'http%'")
    check("traffic", "traffic entries include the proxy target url", targets not in ("", "0"), f"{targets} rows")

# proxy cache
stamp = str(int(time.time()))
for key in ("CACHEA" + stamp, "CACHEB" + stamp):
    http(f"/api/WMS/v2/CRM/Divisions('{key}')?run={stamp}")
time.sleep(1)
hits = nginx_hits("30s", stamp)
check("cache", "two record keys reach the upstream twice", hits == 2, f"{hits} upstream hits")

# expand
s, d = odata(None, "STOCK", path=STOCK, **{"$expand": "Warehouse", "$top": "10"})
check("expand", "out of scope target returns 400",
      s in (400, 403) or not any("Warehouse" in row for row in d.get("value", [])), s)
for navigation in ("HiddenCountry", "OtherEnvCountry"):
    s, _ = odata(None, **{"$expand": navigation, "$top": "1"})
    check("expand", f"$expand={navigation} returns 400", s == 400, s)
s, d = odata(None, **{"$expand": "CountryInfo", "$top": "50"})
check("expand", "warehouses keep 13 rows", s == 200 and d.get("count") == 13, f"{s} count={d.get('count')}")
s, d = odata(None, path=STOCK, **{"$expand": "Warehouse", "$top": "10"})
check("expand", "stock keeps the unmatched row", s == 200 and d.get("count") == 5, f"{s} count={d.get('count')}")
s, d = odata(None, **{"$expand": "CountryInfo", "$top": "5"})
check("expand", "nextLink includes $expand", "expand" in (d.get("nextLink") or ""), d.get("nextLink"))
s, _ = odata(None, **{"$expand": "CountryInfo,CountryInfo", "$top": "1"})
check("expand", "duplicate navigation returns 200 or 400", s in (200, 400), s)
s, b, _ = http(WAREHOUSES, method="QUERY", body=json.dumps({"$expand": "CountryInfo", "$top": 1}),
               headers={"Content-Type": "application/json"})
check("expand", "QUERY body $expand applies or returns 400", s == 400 or (s == 200 and "CountryInfo" in b), s)
s, d = odata(None, **{"$expand": "CountryInfo", "$select": "code", "$top": "2"})
first = (d.get("value") or [{}])[0]
check("expand", "navigation uses target aliases", s == 200 and (first.get("CountryInfo") or {}).get("name"), first)
s, b, _ = http(f"{WAREHOUSES}?$expand=CountryInfo&$top=50")
check("expand", "target columns outside AllowedColumns are absent", "secret-" not in b)
s, _ = odata(None, path=STOCK, **{"$expand": "TenantWarehouse", "$top": "1"})
check("expand", "tenant target returns 400", s == 400, s)
s, _ = odata(None, **{"$expand": "CountryInfo($select=name)", "$top": "1"})
check("expand", "nested options return 400", s == 400, s)
s, b, _ = http(f"{WAREHOUSES}('1')?$expand=CountryInfo")
check("expand", "record key with $expand returns the record", s == 200 and "Netherlands" in b, s)
s, _ = odata(None, **{"$expand": "Nope"})
check("expand", "unknown navigation returns 400", s == 400, s)

# crm navigations
OPPORTUNITIES = "/api/WMS/CRM/Opportunities"
s, d = odata(None, path=OPPORTUNITIES, **{"$expand": "Account,Contact,Stage", "$top": "50"})
rows = d.get("value", [])
check("crm", "opportunities expand account, contact and stage",
      s == 200 and rows and all(r.get("Account", {}).get("name") and r.get("Stage", {}).get("name") for r in rows), s)
check("crm", "opportunity contact belongs to the account",
      bool(rows) and all(r["Contact"] is None or r["Contact"]["accountId"] == r["accountId"] for r in rows), len(rows))
s, d = odata("status eq 'Open'", path=OPPORTUNITIES, **{"$top": "50"})
check("crm", "status eq 'Open' returns open rows", s == 200 and all(r["status"] == "Open" for r in d.get("value", [])), s)
s, d = odata(None, path="/api/WMS/CRM/Contacts", **{"$expand": "Account,Role", "$top": "1"})
first = (d.get("value") or [{}])[0]
check("crm", "contacts expand account and role", s == 200 and first.get("Account") and first.get("Role"), s)
s, d = odata(None, path="/api/WMS/CRM/Accounts", **{"$expand": "AccountType,Industry", "$top": "1"})
first = (d.get("value") or [{}])[0]
check("crm", "accounts expand account type and industry", s == 200 and first.get("AccountType") and first.get("Industry"), s)

# token scope
for path in ["/api/WMS/CRM/Contacts?$top=1", "/api/WMS/v2/Nope/Nothing?$top=1"]:
    s, b, _ = http(path, "SCOPED")
    check("scope", f"scoped token {path} returns 403", s == 403 and "Access denied to endpoint" in b, s)
s, _, _ = http("/api/WMS/CRM/Accounts?$top=1", "SCOPED")
check("scope", "scoped token own endpoint returns 200", s == 200, s)

# tenancy
s, d = odata(None, "TENANT", path=TENANT_WAREHOUSES, **{"$top": "50"})
check("tenant", "tenant token returns NL rows", s == 200 and countries(d) == {"NL"}, f"{s} {countries(d)}")
for expression in ["'DE' eq Country or Country eq 'DE'", "not (Country eq Country)", "not (length('abc') eq 3)"]:
    s, d = odata(expression, "TENANT", path=TENANT_WAREHOUSES, **{"$top": "50"})
    other = countries(d) - {"NL"}
    check("tenant", f"$filter={expression} returns NL rows or 400",
          s in (200, 400) and not other and d.get("totalCount", 0) <= 2,
          f"{s} other={sorted(other)} totalCount={d.get('totalCount')}")
s, _, _ = http(f"{TENANT_WAREHOUSES}('3')", "TENANT")
check("tenant", "DE record key returns 404", s == 404, s)
s, _ = odata(None, "TENANT", {"X-Country": "DE"}, path=TENANT_WAREHOUSES)
check("tenant", "header outside the grant returns 403", s == 403, s)

# mcp
check("mcp", "request without token returns 401", http("/mcp", None, "POST", "{}", MCP_HEADERS)[0] == 401)
check("mcp", "request with bad token returns 401", http("/mcp", "bad-token", "POST", "{}", MCP_HEADERS)[0] == 401)
info, _ = mcp("get_endpoint_info", {"endpointName": "warehouse_warehouses"})
check("mcp", "get_endpoint_info returns deprecated", '"deprecated"' in info.replace("\\u0022", '"'), info[:100])
listing, _ = mcp("list_endpoints", {})
check("mcp", "list_endpoints marks deprecated endpoints", "deprecated" in listing.lower(), listing[:80])
check("mcp", "list_endpoints shows OTHER-only endpoint to an all-environment token", "otherenvcountries" in listing.lower(), listing[:80])
listing, _ = mcp("list_endpoints", {}, "ENVWMS")
check("mcp", "list_endpoints omits OTHER-only endpoint for a WMS token", "otherenvcountries" not in listing.lower() and "countries" in listing.lower(), listing[:80])
info, _ = mcp("get_endpoint_info", {"endpointName": "reference_otherenvcountries"}, "ENVWMS")
check("mcp", "get_endpoint_info hides OTHER-only endpoint for a WMS token", "not found" in info and "OTHER" not in info, info[:80])
text, error = mcp("call_endpoint", {"endpointName": "reference_otherenvcountries", "environment": "OTHER"}, "ENVWMS")
check("mcp", "call_endpoint answers like an unknown tool for a WMS token", error and "Unknown tool" in text and "OTHER" not in text, text[:80])
text, error = mcp("call_endpoint", {"endpointName": "crm_divisions_v2", "environment": "WMS", "method": "DELETE",
                                    "key": "01JCZ92WX0CH9MDSEQSXFB9NNB"})
check("mcp", "DELETE with 204 returns a confirmation", not error and text.strip() != "", repr(text[:60]))
text, error = mcp("call_endpoint", {"endpointName": "crm_divisions_v2", "environment": "WMS", "key": "../../health/details"})
check("mcp", "path escape returns an error", error and "not valid" in text, text[:60])
query = json.dumps({"$filter": "country eq 'NL'", "$top": 5})
text, error = mcp("call_endpoint", {"endpointName": "warehouse_warehouses_v2", "environment": "WMS", "method": "QUERY",
                                    "body": query})
check("mcp", "QUERY returns 2 rows", not error and '"count":2' in text.replace("\\u0022", '"').replace(" ", ""), text[:60])
text, error = mcp("call_endpoint", {"endpointName": "crm_divisions_v2", "environment": "WMS", "method": "POST",
                                    "body": '{"name":"Branch Vienna"}'})
check("mcp", "POST succeeds", not error, text[:60])

# webhooks
payload = json.dumps({"action": "opened", "run": time.time_ns()})
JSON = {"Content-Type": "application/json"}
s, b, _ = github(payload)
check("webhook", "signed github delivery returns 201", s == 201 and "Webhook stored." in b, f"{s} {b[:60]}")
delivery = f"verify-dup-{time.time_ns()}"
github(payload, delivery=delivery)
s, b, _ = github(payload, delivery=delivery)
check("webhook", "repeated delivery id returns 200 duplicate", s == 200 and "Duplicate" in b, f"{s} {b[:60]}")
s, _, _ = github(payload, secret="wrong-secret")
check("webhook", "wrong signature returns 401", s == 401, s)
s, _, _ = http(f"{WEBHOOK}/github", None, "POST", payload, JSON)
check("webhook", "unsigned github delivery returns 401", s == 401, s)
s, _, _ = http(f"{WEBHOOK}/github", "TOKEN", "POST", payload, JSON)
check("webhook", "bearer token on a verified id returns 401", s == 401, s)
s, _, _ = http(f"{WEBHOOK}/partner", "TOKEN", "POST", payload, JSON)
check("webhook", "partner delivery with token returns 201", s == 201, s)
s, _, _ = http(f"{WEBHOOK}/partner", None, "POST", payload, JSON)
check("webhook", "partner delivery without token returns 401", s == 401, s)
s, _, _ = http(f"{WEBHOOK}/partner", "TOKEN", "POST", "{not json", JSON)
check("webhook", "malformed json returns 400", s == 400, s)
for path in (f"{WEBHOOK}/unknown", f"{WEBHOOK}/partner/extra"):
    s, _, _ = http(path, "TOKEN", "POST", payload, JSON)
    check("webhook", f"{path.removeprefix(WEBHOOK)} returns 404", s == 404, s)
deliveries = console("GET", "/_/api/webhooks/Events/Inbound/deliveries?env=WMS")
check("webhook", "console lists stored deliveries", '"github"' in json.dumps(deliveries), str(deliveries)[:80])

# secrets
environment = json.dumps(console("GET", "/_/api/environments/WMS"))
check("secrets", "environment read lists the secret name", "GITHUB-WEBHOOK" in environment)
check("secrets", "environment read contains no secret value", WEBHOOK_SECRET not in environment)
overview = console("GET", "/_/api/secrets")
row = next((r for r in overview.get("secrets", []) if r.get("name") == "GITHUB-WEBHOOK"), {})
check("secrets", "secrets page lists the reference and value", row.get("has_value") and row.get("used_by"), row)
on_disk = open(f"{WORK}/config/environments/WMS/settings.json").read()
check("secrets", "settings.json stores the secret encrypted", WEBHOOK_SECRET not in on_disk and "PWENC:" in on_disk)

# unreachable upstream
s, b, _ = http("/api/WMS/Outage/Status")
check("outage", "unreachable upstream returns 503", s == 503 and "traceId" in b, f"{s} {b[:60]}")
check("outage", "503 body names no host or port", "nginx" not in b and ":1" not in b, b[:80])

# console save checks
saved = console("PUT", "/_/api/endpoints/proxy/Outage/Broken?raw=true", json.dumps({"content": '{"Methods":["GET"]}'}))
check("console", "save the gateway cannot load returns errors", bool(saved.get("errors")), str(saved)[:80])
check("console", "refused save writes no file", not os.path.exists(f"{WORK}/config/endpoints/Proxy/Outage/Broken/entity.json"))

# application log
out = subprocess.run([DOCKER, "logs", "test-portway"], capture_output=True, text=True)
occurrences = sum((out.stdout + out.stderr).count(token) for token in TOKENS.values())
check("log", "application log contains no token", occurrences == 0, f"{occurrences} occurrences")
check("log", "application log contains no secret value", WEBHOOK_SECRET not in out.stdout + out.stderr)

area_width = max(len(area) for area, *_ in results)
name_width = max(len(name) for _, name, _, _ in results)
for area, name, ok, actual in results:
    detail = " ".join(str(actual).split())[:100]
    print(f"{'PASS' if ok else 'FAIL'}  {area:<{area_width}}  {name:<{name_width}}  {detail}")
failed = sum(not ok for _, _, ok, _ in results)
print(f"\n{len(results) - failed} passed, {failed} failed")
sys.exit(1 if failed else 0)
