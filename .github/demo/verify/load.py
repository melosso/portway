"""
Load generator for the test instance from start.sh.

> Usage: 
load.py limit
load.py sql|sqlvary|proxy|proxyvary|mcp|bad REQUESTS THREADS
"""

import collections
import http.client
import json
import os
import sys
import threading
import time
import urllib.parse
from concurrent.futures import ThreadPoolExecutor

HOST, PORT = "127.0.0.1", 8081
WORK = os.environ.get("PORTWAY_VERIFY_DIR", "/tmp/portway-verify")
TOKEN = dict(line.strip().split("=", 1) for line in open(f"{WORK}/tokens.env") if "=" in line)["TOKEN"]
MCP_HEADERS = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
WAREHOUSES = "/api/WMS/v2/Warehouse/Warehouses"
DIVISIONS = "/api/WMS/v2/CRM/Divisions"
local = threading.local()


def connection():
    if not hasattr(local, "connection"):
        local.connection = http.client.HTTPConnection(HOST, PORT, timeout=30)
    return local.connection


def send(method, path, body=None, headers=None):
    headers = {"Authorization": f"Bearer {TOKEN}", **(headers or {})}
    started = time.perf_counter()
    for attempt in (0, 1):
        try:
            current = connection()
            current.request(method, path, body=body, headers=headers)
            response = current.getresponse()
            return response.status, (time.perf_counter() - started) * 1000, response.read()
        except Exception as error:
            local.__dict__.pop("connection", None)
            if attempt:
                return f"EXC {type(error).__name__}", (time.perf_counter() - started) * 1000, b""


def run(name, requests, threads, make):
    statuses, latencies, samples = collections.Counter(), [], {}
    started = time.perf_counter()
    with ThreadPoolExecutor(threads) as pool:
        for status, ms, body in pool.map(lambda i: send(*make(i)), range(requests)):
            statuses[status] += 1
            latencies.append(ms)
            samples.setdefault(status, body[:160].decode(errors="replace"))
    duration = time.perf_counter() - started
    latencies.sort()

    def percentile(p):
        return latencies[min(len(latencies) - 1, int(p * len(latencies)))]

    print(f"{name}: n={requests} threads={threads} {requests / duration:.0f} req/s p50={percentile(.5):.1f}ms "
          f"p95={percentile(.95):.1f}ms p99={percentile(.99):.1f}ms max={latencies[-1]:.0f}ms status={dict(statuses)}")
    for status, body in samples.items():
        if status != 200:
            print(f"   sample {status}: {body}")


def get(path):
    return "GET", path, None, None


def mcp_call(_):
    arguments = {"endpointName": "warehouse_warehouses_v2", "environment": "WMS", "query": "$top=5"}
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                       "params": {"name": "call_endpoint", "arguments": arguments}})
    return "POST", "/mcp", body, MCP_HEADERS


SCENARIOS = {
    "sql": ("sql get", lambda i: get(f"{WAREHOUSES}?$top=20&$orderby=code")),
    "sqlvary": ("sql get distinct filters",
                lambda i: get(f"{WAREHOUSES}?$top=5&$filter=" + urllib.parse.quote(f"capacityM2 gt {i}"))),
    "proxy": ("proxy get cached", lambda i: get(f"{DIVISIONS}?$top=5")),
    "proxyvary": ("proxy get distinct", lambda i: get(f"{DIVISIONS}?page={i}")),
    "mcp": ("mcp call_endpoint", mcp_call),
    "bad": ("malformed 1 eq 1", lambda i: get(f"{WAREHOUSES}?$filter=" + urllib.parse.quote("1 eq 1"))),
}

if __name__ == "__main__":
    scenario = sys.argv[1] if len(sys.argv) > 1 else ""
    if scenario == "limit":
        run("rate-limit burst", 300, 30, lambda i: get(f"{WAREHOUSES}?$top=20&$orderby=code"))
    elif scenario in SCENARIOS and len(sys.argv) == 4:
        name, make = SCENARIOS[scenario]
        run(name, int(sys.argv[2]), int(sys.argv[3]), make)
    else:
        sys.exit(__doc__)
