---
title: Deploying
description: "Choose between deploying Portway with Docker Compose or on Windows Server behind IIS"
---

# Deploying

Portway ships as a container image and as a Windows release, so there are two well-trodden paths to a running gateway. Both end up in the same place: the endpoint and environment configuration is identical either way, and so is everything in the rest of this guide.

## Deploying with Docker

Most deployments use Docker Compose. It is the quickest way to a running gateway, it keeps your configuration in files you can version, and it works the same on a laptop, a Home Lab box, and a production host.

Start with [Deploying with Docker](/guide/docker-compose), which walks from a first container through the settings worth having in place before real traffic arrives.

## Deploying on Windows Server

If your gateway needs to live alongside existing IIS sites, or a proxy endpoint depends on NTLM pass-through to something like Exact Globe+ or AFAS Profit, hosting on Windows Server gives you a domain identity to work with.

[Deploying on Windows Server](/guide/deployment-windows) covers the Hosting Bundle, the Application Pool, HTTPS bindings, and folder permissions.

## Next steps

Once Portway is running, the configuration is the same whichever path you took:

- [Getting Started](/guide/getting-started) for your access token and first environment
- [Configure Environments](/guide/environments)
- [Configure Endpoints](/guide/endpoints-sql)
- [Security](/guide/security)
- [Monitoring](/guide/monitoring)
