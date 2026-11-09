"""
Stress scenarios for the test instance from start.sh, driven by StressAnAPI (pip install stressanapi==1.0.3).

> Usage:
stress.py [SECONDS] [THREADS] [BURST] [FILTER]
"""

import fcntl
import json
import os
import pty
import re
import shutil
import socket
import struct
import subprocess
import sys
import termios
import threading
import time

BASE = "http://127.0.0.1:8081"
WORK = os.environ.get("PORTWAY_VERIFY_DIR", "/tmp/portway-verify")
TOKENS = dict(line.strip().split("=", 1) for line in open(f"{WORK}/tokens.env") if "=" in line)
BINARY = os.environ.get("STRESSANAPI") or shutil.which("stressanapi")
SECONDS = float(sys.argv[1]) if len(sys.argv) > 1 else 5
THREADS = int(sys.argv[2]) if len(sys.argv) > 2 else 8
BURST = int(sys.argv[3]) if len(sys.argv) > 3 else 5
FILTER = sys.argv[4] if len(sys.argv) > 4 else ""
JSON = "application/json"
DIVISION = {"code": "BR-VIE", "name": "Branch Vienna", "city": "Wien", "country": "AT", "isHeadOffice": "false"}
ORDER = {"orderNumber": "PO-2026-0422", "sku": "BS-3050", "quantity": "1500", "plant": "Eindhoven"}
ODATA = "$select=id,name,city&$filter=country%20eq%20%27NL%27&$orderby=name%20desc&$count=true&$skip=1&$top="

# name, method, path, expected statuses, expected 2xx body text, token, extra headers, post_data
SCENARIOS = [
    ("health", "GET", "/health", [200], "", None, {}, {}),
    ("health live", "GET", "/health/live", [200], "", None, {}, {}),
    ("accounts odata", "GET", f"/api/WMS/CRM/Accounts?$expand=AccountType,Industry&{ODATA}%%randomint:1:10%%", [200], '"value":[', "TOKEN", {}, {}),
    ("accounts by key", "GET", "/api/WMS/CRM/Accounts(%%randomint:1:16%%)", [404], "", "TOKEN", {}, {}),
    ("account types by key", "GET", "/api/WMS/CRM/AccountTypes('CUSTOMER')", [200], '"code":"CUSTOMER"', "TOKEN", {}, {}),
    ("account types", "GET", "/api/WMS/CRM/AccountTypes?$select=code,name&$orderby=sortOrder&$count=true&$top=%%randomint:1:4%%", [200], '"value":[', "TOKEN", {}, {}),
    ("contact roles", "GET", "/api/WMS/CRM/ContactRoles?$filter=code%20ne%20%27X%27&$count=true&$skip=%%randomint:0:4%%", [200], '"value":[', "TOKEN", {}, {}),
    ("contacts expand", "GET", "/api/WMS/CRM/Contacts?$expand=Account,Role&$select=id,firstName,lastName,accountId,roleCode&$filter=isPrimary%20eq%201&$count=true&$top=%%randomint:1:38%%", [200], '"value":[', "TOKEN", {}, {}),
    ("industries", "GET", "/api/WMS/CRM/Industries?$orderby=name&$top=%%randomint:1:8%%", [200], '"value":[', "TOKEN", {}, {}),
    ("opportunities expand", "GET", "/api/WMS/CRM/Opportunities?$expand=Account,Contact,Stage&$filter=amount%20gt%20%%randomint:0:100000%%&$orderby=amount%20desc&$count=true&$top=10", [200], '"value":[', "TOKEN", {}, {}),
    ("opportunity stages", "GET", "/api/WMS/CRM/OpportunityStages?$filter=isClosed%20eq%200&$select=code,name,probability&$top=%%randomint:1:7%%", [200], '"value":[', "TOKEN", {}, {}),
    ("stock expand", "GET", "/api/WMS/Inventory/StockLevels?$expand=Warehouse&$filter=qty%20ge%20%%randomint:0:50%%&$count=true", [200], '"value":[', "TOKEN", {}, {}),
    ("countries", "GET", "/api/WMS/Reference/Countries?$filter=continent%20eq%20%27Europe%27&$select=code,name&$orderby=code&$count=true&$top=%%randomint:1:7%%", [200], '"value":[', "TOKEN", {}, {}),
    ("countries by key", "GET", "/api/WMS/Reference/Countries('NL')", [400], "", "TOKEN", {}, {}),
    ("warehouses v1", "GET", "/api/WMS/Warehouse/Warehouses?$select=Id,Code,City&$filter=IsActive%20eq%201&$orderby=Code&$count=true&$top=%%randomint:1:13%%", [200], '"value":[', "TOKEN", {}, {}),
    ("warehouses v2 expand", "GET", "/api/WMS/v2/Warehouse/Warehouses?$expand=CountryInfo&$select=id,code,country&$count=true&$skip=%%randomint:0:12%%", [200], '"value":[', "TOKEN", {}, {}),
    ("warehouses v2 by key", "GET", "/api/WMS/v2/Warehouse/Warehouses('%%randomint:1:13%%')?$expand=CountryInfo", [200], '"code":', "TOKEN", {}, {}),
    ("tenant warehouses", "GET", "/api/WMS/Warehouse/CountryWarehouses?$count=true&$top=%%randomint:1:5%%", [200], '"NL"', "TENANT", {"X-Country": "NL"}, {}),
    ("tenant outside grant", "GET", "/api/WMS/Warehouse/CountryWarehouses?$top=%%randomint:1:5%%", [403], "", "TENANT", {"X-Country": "DE"}, {}),
    ("tenant missing grant", "GET", "/api/WMS/Warehouse/CountryWarehouses?$top=%%randomint:1:5%%", [403], "", "TOKEN", {}, {}),
    ("divisions v1 get", "GET", "/api/WMS/CRM/Divisions", [200], "", "TOKEN", {}, {}),
    ("divisions v1 post", "POST", "/api/WMS/CRM/Divisions", [201], "Division created", "TOKEN", {}, DIVISION),
    ("divisions v1 put", "PUT", "/api/WMS/CRM/Divisions", [200], "Division updated", "TOKEN", {}, DIVISION),
    ("divisions v1 delete", "DELETE", "/api/WMS/CRM/Divisions/%%randomint:1:9999%%", [200], "Division deleted", "TOKEN", {}, {}),
    ("divisions v2 get", "GET", "/api/WMS/v2/CRM/Divisions", [200], "", "TOKEN", {}, {}),
    ("divisions v2 post", "POST", "/api/WMS/v2/CRM/Divisions", [201], '"code": "BR-VIE"', "TOKEN", {}, DIVISION),
    ("divisions v2 put", "PUT", "/api/WMS/v2/CRM/Divisions", [200], '"code": "HQ-AMS"', "TOKEN", {}, DIVISION),
    ("divisions v2 delete", "DELETE", "/api/WMS/v2/CRM/Divisions/%%randomint:1:9999%%", [204], "", "TOKEN", {}, {}),
    ("orders v1 post", "POST", "/api/WMS/Production/Orders", [201], "Production order created", "TOKEN", {}, ORDER),
    ("orders v2 put", "PUT", "/api/WMS/v2/Production/Orders", [200], '"orderNumber"', "TOKEN", {}, ORDER),
    ("orders v2 delete", "DELETE", "/api/WMS/v2/Production/Orders/%%randomint:1:9999%%", [204], "", "TOKEN", {}, {}),
    ("products v1 get", "GET", "/api/WMS/Warehouse/Products", [200], "", "TOKEN", {}, {}),
    ("products v2 transform", "GET", "/api/WMS/v2/Warehouse/Products", [200], '"itemCode":"EP-1208"', "TOKEN", {}, {}),
    ("products v2 patch", "PATCH", "/api/WMS/v2/Warehouse/Products", [405], "", "TOKEN", {}, ORDER),
    ("sql post", "POST", "/api/WMS/CRM/Accounts", [405], "", "TOKEN", {}, {"name": "x"}),
    ("sql delete", "DELETE", "/api/WMS/CRM/Accounts(%%randomint:1:16%%)", [405], "", "TOKEN", {}, {}),
    ("disabled endpoint", "GET", "/api/WMS/Reference/HiddenCountries?$top=%%randomint:1:5%%", [503], "", "TOKEN", {}, {}),
    ("endpoint env denied", "GET", "/api/WMS/Reference/OtherEnvCountries?$top=%%randomint:1:5%%", [400], "", "TOKEN", {}, {}),
    ("unknown env", "GET", "/api/PROD/CRM/Accounts?$top=%%randomint:1:5%%", [400], "", "TOKEN", {}, {}),
    ("unknown endpoint", "GET", "/api/WMS/CRM/Nope%%randomint:1:9999%%", [404], "", "TOKEN", {}, {}),
    ("no token", "GET", "/api/WMS/CRM/Accounts?$top=%%randomint:1:5%%", [401], "", None, {}, {}),
    ("bad token", "GET", "/api/WMS/CRM/Accounts?$top=%%randomint:1:5%%", [401], "", "pwdemo_invalid", {}, {}),
    ("scoped own", "GET", "/api/WMS/CRM/Accounts?$top=%%randomint:1:5%%", [200], '"value":[', "SCOPED", {}, {}),
    ("scoped other", "GET", "/api/WMS/CRM/Contacts?$top=%%randomint:1:5%%", [403], "", "SCOPED", {}, {}),
    ("bad column", "GET", "/api/WMS/CRM/Accounts?$select=secret&$top=%%randomint:1:5%%", [400], "", "TOKEN", {}, {}),
    ("bad filter", "GET", "/api/WMS/CRM/Accounts?$filter=name%20eq&$top=%%randomint:1:5%%", [400], "", "TOKEN", {}, {}),
    ("negative top", "GET", "/api/WMS/CRM/Accounts?$top=-%%randomint:1:99%%", [400], "", "TOKEN", {}, {}),
    ("bad expand", "GET", "/api/WMS/v2/Warehouse/Warehouses?$expand=HiddenCountry&$top=%%randomint:1:5%%", [400], "", "TOKEN", {}, {}),
    ("injection", "GET", "/api/WMS/CRM/Accounts?$filter=name%20eq%20%27x%27%27;DROP%20TABLE%20Accounts;--%27&$top=%%randomint:1:5%%", [200, 400], "", "TOKEN", {}, {}),
    ("filter random ipv4", "GET", "/api/WMS/CRM/Accounts?$count=true&$filter=website%20eq%20%27%%randomipv4%%'", [200], '"count":0', "TOKEN", {}, {}),
    ("path random ipv6", "GET", "/api/WMS/CRM/%%randomipv6%%", [400, 404], "", "TOKEN", {}, {}),
    ("path random private ipv4", "GET", "/api/WMS/%%randomprivateipv4%%/Accounts", [400, 404], "", "TOKEN", {}, {}),
]

STATUS = re.compile(r" - (\d{3})\b(.*) \[?(\d+\.\d+)")


def listen(sock, lines, stop):
    while not stop.is_set():
        try:
            lines.append(sock.recv(65535).decode(errors="replace"))
        except socket.timeout:
            pass


def run(name, method, path, expected, body, token, headers, post_data):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("127.0.0.1", 0))
    sock.settimeout(0.2)
    lines, stop = [], threading.Event()
    listener = threading.Thread(target=listen, args=(sock, lines, stop))
    listener.start()
    all_headers = {"Content-Type": JSON, "Accept": JSON, "X-Forwarded-For": "203.0.113.7", "X-Real-IP": "203.0.113.7", **headers}
    if token:
        all_headers["Authorization"] = "Bearer " + TOKENS.get(token, token)
    config = {
        "url": BASE + path, "method": method, "post_data": post_data, "headers": all_headers, "timeout": 10,
        "success_status_codes": expected, "user_agent": "Portway stress", "start_interval": 0.01,
        "start_burst": BURST, "start_threads": THREADS, "cpu_affinity": [-1],
        "syslog_server_url": f"udp://127.0.0.1:{sock.getsockname()[1]}/local7",
    }
    conf = f"{WORK}/stress-{re.sub(r'\W+', '-', name)}.json"
    json.dump(config, open(conf, "w"), indent=4, ensure_ascii=False)
    master, slave = pty.openpty()
    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 50, 200, 0, 0))
    process = subprocess.Popen([BINARY, "--conf", conf, "--nodate"], stdin=slave, stdout=slave, stderr=slave, close_fds=True)
    os.close(slave)
    drain = threading.Thread(target=lambda: _drain(master), daemon=True)
    drain.start()
    time.sleep(SECONDS)
    os.write(master, b"f")
    try:
        process.wait(15)
    except subprocess.TimeoutExpired:
        process.kill()
    time.sleep(0.3)
    stop.set()
    listener.join()
    sock.close()

    statuses, latencies, wrong = {}, [], []
    for line in lines:
        match = STATUS.search(line)
        if not match:
            continue
        status, text = int(match.group(1)), match.group(2)
        statuses[status] = statuses.get(status, 0) + 1
        latencies.append(float(match.group(3)))
        if status not in expected or (200 <= status < 300 and status != 204 and body and body not in text):
            wrong.append(f"{status} {text.strip()[:140]}")
    total = sum(statuses.values())
    latencies.sort()
    p = lambda q: latencies[min(len(latencies) - 1, int(q * len(latencies)))] * 1000 if latencies else 0
    ok = total > 0 and not wrong
    print(f"{'PASS' if ok else 'FAIL'}  {name:28} {method:6} n={total:<6} {total / SECONDS:7.0f} rps  "
          f"p50={p(.5):6.1f}ms p95={p(.95):6.1f}ms p99={p(.99):7.1f}ms  {statuses}")
    for sample in sorted(set(wrong))[:3]:
        print(f"      unexpected: {sample}")
    return ok


def _drain(fd):
    try:
        while os.read(fd, 65536):
            pass
    except OSError:
        pass


if __name__ == "__main__":
    if not BINARY:
        sys.exit("stressanapi not found: pip install stressanapi==1.0.3 or set STRESSANAPI")
    print(f"{SECONDS:g}s per scenario, {THREADS} threads, burst {BURST}")
    results = [run(*scenario) for scenario in SCENARIOS if FILTER in scenario[0]]
    print(f"{results.count(True)}/{len(results)} scenarios passed")
    sys.exit(0 if all(results) else 1)
