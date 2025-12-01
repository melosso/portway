# Guide

Here’s where you get the practical context before setting up your first endpoint. If you’re new to Portway, this page gives you the high-level picture so you know what to expect as you move through the docs.

## Quick Links

Most people start with installation, deployment, or security configuration, so these shortcuts take you straight there.

* [Getting Started](./getting-started)
* [Deployment](./deployment)
* [Security](./security)

## What Is Portway?

Portway is an API gateway built for Windows environments. It sits in front of SQL Server, internal services, and static content, and exposes them through a consistent REST interface. If you’re running a mix of legacy systems and new services, Portway gives you a way to surface them without rewriting anything.

It works with:

* SQL Server tables and stored procedures
* Internal web services that need authentication
* JSON, XML, and CSV files
* Incoming webhooks
* Multi-step composite operations

---

# Core Concepts

These are the ideas that shape how Portway works. You’ll see each one referenced throughout the documentation, so a quick overview here helps when you’re configuring endpoints later.

## Security

Portway includes built-in mechanisms to control who can access what and how requests move between environments.

<details>
<summary>Show details</summary>

* Token authentication
* Scoped permissions
* Environment-aware controls
* Encryption
* Azure Key Vault integration
* Rate limiting and request validation

</details>

## Environment Awareness

Environments are treated as independent spaces. This keeps configuration clean and avoids cross-contamination between dev, test, and production.

<details>
<summary>Show details</summary>

* Routing based on environment
* Environment-specific configuration
* Isolated headers and connection strings

</details>

## Developer Experience

A few tools are included so you can understand what’s happening at runtime and debug issues without extra setup.

<details>
<summary>Show details</summary>

* Auto-generated API documentation
* Logging and tracing
* Health endpoints
* JSON-based configuration

</details>

## Endpoint Types

Everything Portway exposes fits into a small set of endpoint categories. Knowing these helps you decide which one matches your use case.

<details>
<summary>Show details</summary>

* SQL endpoints with OData query support
* Proxy endpoints for forwarding requests
* Composite endpoints for multi-step workflows
* File endpoints for storage and retrieval
* Webhook receivers
* Static file endpoints

</details>

---

# What’s Next?

If you’re ready to dig deeper, the next sections walk through installation, configuring endpoints, and deploying to production. If you run into something unexpected, the GitHub links are the fastest way to get help or report an issue.

* **Issues:** [https://github.com/melosso/portway/issues](https://github.com/melosso/portway/issues)
* **Discussions:** [https://github.com/melosso/portway/discussions](https://github.com/melosso/portway/discussions)