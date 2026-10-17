---
title: Deploying
description: "Choose between deploying Portway with Docker Compose, on Linux as a systemd service or on Windows Server behind IIS"
---

# Deploying

Portway ships as a container image, as `.deb` and `.rpm` packages, and as Linux and Windows archives. Each release targets x64 and arm64. Endpoint and environment configuration is the same for every installation method.

Installation methods:

::: cards 2
- [Deploying with Docker](/guide/deployment-docker)
  Docker Compose, from a first container to production.
- [Deploying on Linux](/guide/deployment-linux)
  `.deb` or `.rpm` package, or release archive, as a systemd service.
- [Deploying on Windows Server](/guide/deployment-windows)
  IIS website with HTTPS and a dedicated Application Pool.
:::

When you're done, make sure to read the guide about access tokens and setting up your first environment in [Initial configuration](/guide/getting-started#initial-configuration).
