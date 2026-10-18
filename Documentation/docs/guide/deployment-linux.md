---
title: Deploying on Linux
description: "Deploy Portway on Linux from a .deb or .rpm package, or from a release archive, as a systemd service"
---

# Deploying on Linux

## Prerequisites

- x64 or arm64 Linux with systemd
- Debian or Ubuntu for `.deb` packages; RHEL, Fedora, Rocky Linux or AlmaLinux for `.rpm` packages
- Root or `sudo` access
- [ASP.NET Core Runtime 11](https://get.dot.net/11) for archive installation; packages include the runtime
- A reverse proxy for HTTPS, e.g. Nginx or Caddy ([Instances](/guide/instances#linux))

## Release files

The [Releases page](https://github.com/melosso/portway/releases/) contains these Linux files:

| File | Architecture | .NET runtime |
|---|---|---|
| `portway_<version>_amd64.deb` | x64 | Included |
| `portway_<version>_arm64.deb` | arm64 | Included |
| `portway-<version>-1.x86_64.rpm` | x64 | Included |
| `portway-<version>-1.aarch64.rpm` | arm64 | Included |
| `<tag>-Linux_x64.zip` | x64 | [ASP.NET Core Runtime 11](https://get.dot.net/11) required |
| `<tag>-Linux_arm64.zip` | arm64 | [ASP.NET Core Runtime 11](https://get.dot.net/11) required |

`SHA256SUMS` contains the SHA-256 checksum of each release file. Verification of the downloaded files:

```bash
sha256sum --check --ignore-missing SHA256SUMS
```

## Package installation

### 1. Install the package

::: code-group

```bash [Debian / Ubuntu]
sudo apt install ./portway_<version>_amd64.deb
```

```bash [RHEL / Fedora]
sudo dnf install ./portway-<version>-1.x86_64.rpm
```

:::

The package installs:

| Path | Contents |
|---|---|
| `/opt/portway/` | Application files, `appsettings.json`, `endpoints/`, `environments/` |
| `/usr/lib/systemd/system/portway.service` | systemd unit |
| `portway` | System user that runs the service and owns `/opt/portway/` |

### 2. Set the encryption key

Portway reads `PORTWAY_ENCRYPTION_KEY` from `/opt/portway/.env`. Set the key before the first start:

```bash
echo "PORTWAY_ENCRYPTION_KEY=$(openssl rand -base64 48)" | sudo tee /opt/portway/.env > /dev/null
sudo chown portway:portway /opt/portway/.env
sudo chmod 600 /opt/portway/.env
```

### 3. Start the service

The package does not enable or start the service:

```bash
sudo systemctl enable --now portway
```

### 4. Verify

The first start creates `tokens/`, `log/` and `auth.db` in `/opt/portway/`. Checks:

- `curl http://localhost:5000/health/live` returns `Alive`
- `http://localhost:5000/docs` serves the API reference

## Listen address

The default listen address is `http://localhost:5000`. A systemd drop-in sets a different address with `ASPNETCORE_URLS`:

```bash
sudo systemctl edit portway
```

```ini
[Service]
Environment=ASPNETCORE_URLS=http://0.0.0.0:8080
```

```bash
sudo systemctl restart portway
```

HTTPS requires a reverse proxy. Nginx and Caddy configuration: [Instances](/guide/instances#linux).

## Package upgrades

Install the newer package with the command from [Package installation](#package-installation). The package restarts a running service.

The package marks `appsettings*.json`, `endpoints/` and `environments/` as configuration files:

| Format | Behavior for a modified configuration file |
|---|---|
| `.deb` | `dpkg` prompts if the new release changes the file. `-o Dpkg::Options::=--force-confold` keeps the local file without a prompt. |
| `.rpm` | `rpm` keeps the local file and writes the release version as `<file>.rpmnew`. |

Release notes and backup steps: [Upgrading Portway](/guide/upgrading).

## Package removal

::: code-group

```bash [Debian / Ubuntu]
sudo apt remove portway
```

```bash [RHEL / Fedora]
sudo dnf remove portway
```

:::

Removal stops and disables the service. Files created at runtime (`auth.db`, `mcp.db`, `tokens/`, `log/`, `.env`) remain in `/opt/portway/`.

## Archive installation

The archive requires the [ASP.NET Core Runtime 11](https://get.dot.net/11).

### 1. Extract the release

```bash
sudo useradd --system --home-dir /opt/portway --shell /usr/sbin/nologin portway
sudo mkdir -p /opt/portway
sudo unzip <tag>-Linux_x64.zip -d /opt/portway
sudo chown -R portway:portway /opt/portway
```

### 2. Set the encryption key

Identical to step 2 of [Package installation](#package-installation).

### 3. Create the systemd unit

`/etc/systemd/system/portway.service`:

```ini
[Unit]
Description=Portway API gateway
After=network-online.target
Wants=network-online.target

[Service]
User=portway
WorkingDirectory=/opt/portway
ExecStart=/usr/bin/dotnet /opt/portway/PortwayApi.dll
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now portway
```

`WorkingDirectory` is required: Portway reads and writes `auth.db`, `log/`, `endpoints/` and `environments/` relative to the working directory.

## Troubleshooting

Service log:

```bash
sudo journalctl -u portway -e
```

| Error | Likely cause | Resolution |
|---|---|---|
| `PORTWAY_ENCRYPTION_KEY is not set` | Missing `.env` | Create `/opt/portway/.env` ([Package installation](#package-installation)) |
| `Permission denied` on `auth.db` or `log/` | Files not owned by `portway` | `sudo chown -R portway:portway /opt/portway` |
| `Address already in use` | Port taken by another process | Set a different `ASPNETCORE_URLS` ([Listen address](#listen-address)) |
| `You must install or update .NET` | Archive installation without the runtime | Install the [ASP.NET Core Runtime 11](https://get.dot.net/11) |

| Log | Location |
|---|---|
| Application | `/opt/portway/log/portwayapi-*.log` |
| Service | `journalctl -u portway` |

## Backup

Files to back up in `/opt/portway/`: [Upgrading Portway](/guide/upgrading#2-back-up-the-installation).

Next: [Record the access token](/guide/getting-started#2-record-the-access-token).
