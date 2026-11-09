# Verify

Scripts for an isolated Portway test instance from the demo stack. Allows us to run functional and load checks.

The test instance will attempt to spawn on `127.0.0.1:8081`. Builds on containers `test-portway` and `test-nginx` and the network `portway-verify` (port 8080 not used).

## Requirements

- `podman` or `docker`
- `python3` and `curl`

## Scripts

| Script | Description |
| --- | --- |
| `start.sh [--build] [--image IMG] [--no-limit] [--traffic]` | Starts the test instance. `--build` builds `localhost/portway:verify` from the working tree. `--no-limit` disables rate limiting. `--traffic` enables traffic logging to SQLite. |
| `verify.py` | Runs the functional checks. Exit code `1` if a check fails. |
| `load.py limit` | Sends a burst of 300 requests to the rate limiter. |
| `load.py SCENARIO REQUESTS THREADS` | Runs a load scenario: `sql`, `sqlvary`, `proxy`, `proxyvary`, `mcp` or `bad`. |
| `console.sh METHOD PATH [JSON]` | Calls the console API from inside `test-portway`. |

`start.sh` copies `.github/demo` to `$PORTWAY_VERIFY_DIR` (default `/tmp/portway-verify`) and adds `$expand` fixtures to the copy. It creates the console account `tester` and the tokens `TOKEN`, `SCOPED` (`CRM/Accounts`), `TENANT` (`X-Country: NL`) and `STOCK` (`Inventory/StockLevels`) in `tokens.env`.

## Usage

Functional checks require traffic logging:

```bash
./start.sh --build --traffic
python3 -I verify.py
```

Rate limiter, with the limiter on:

```bash
./start.sh --image localhost/portway:verify
python3 -I load.py limit
```

Load, with the limiter off:

```bash
./start.sh --image localhost/portway:verify --no-limit
python3 -I load.py sql 5000 50
python3 -I load.py sqlvary 3000 50
python3 -I load.py proxy 5000 50
python3 -I load.py proxyvary 3000 50
python3 -I load.py mcp 2000 20
python3 -I load.py bad 1000 20
```

## Expected results

- `verify.py`: every check passes.
- `load.py limit`: 100 × `200` and 200 × `429`.
- Load scenarios: `200` for every request. `bad` returns `400` for every request.

## Cleanup

```bash
podman rm -f test-portway test-nginx && podman network rm portway-verify
```
