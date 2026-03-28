---
layout: home

hero:
  name: "Portway"
  text:
  tagline: API gateway for Windows Server. Expose SQL databases, internal services, and files as MCP-tools and REST endpoints.
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: alt
      text: Demo
      link: https://portway-demo.melosso.com/
    - theme: alt
      text: GitHub
      link: https://github.com/melosso/portway

features:
  - title: SQL endpoints
    details: Expose tables and stored procedures via OData. Control access per column.
  - title: HTTP proxy
    details: Forward requests to existing services. Add auth and rate limiting.
  - title: Webhooks
    details: Receive, validate, and process inbound webhooks.
  - title: Token auth
    details: Scoped Bearer tokens restricted by endpoint, environment, and method.
  - title: Environment isolation
    details: Separate environments with independent connection strings.
  - title: Web UI
    details: Manage tokens, browse logs, and monitor health.
---

<div class="home-platforms">

<span class="platforms-title">Available For</span>

<div class="platforms-list">
  <a href="/guide/getting-started" class="platform-logo">
    <img src="https://cdn.jsdelivr.net/gh/selfhst/icons@main/svg/microsoft-windows.svg" alt="Windows" loading="lazy">
  </a>
  <a href="/guide/deployment" class="platform-logo">
    <img src="https://cdn.jsdelivr.net/gh/selfhst/icons@main/svg/linux.svg" alt="Linux" loading="lazy">
  </a>
  <a href="/guide/docker-compose" class="platform-logo">
    <img src="https://cdn.jsdelivr.net/gh/selfhst/icons@main/svg/docker.svg" alt="Docker" loading="lazy">
  </a>
  <a href="/guide/deployment" class="platform-logo">
    <img src="https://cdn.jsdelivr.net/gh/selfhst/icons@main/svg/podman.svg" alt="Podman" loading="lazy">
  </a>
</div>

<div class="more-button-wrapper">
  <a class="VPButton medium alt more-button" href="/guide/deployment">All deployment options</a>
</div>

</div>

<style>

.home-platforms {
  margin-top: 48px;
  padding: 32px;
  text-align: center;
}

.platforms-title {
  font-size: 0.875rem !important;
  font-weight: 600 !important;
  margin-bottom: 24px !important;
  color: var(--vp-c-text-2);
  display: block;
}

.platforms-list {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 32px;
  flex-wrap: wrap;
}

.platform-logo {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px 24px;
  background: var(--vp-c-bg-alt);
  border: 1px solid var(--vp-c-border);
  border-radius: 8px;
  transition: all 0.2s ease;
}

.platform-logo:hover {
  border-color: var(--vp-c-brand-soft);
  transform: translateY(-2px);
}

.platform-logo img {
  width: 32px;
  height: 32px;
  opacity: 0.8;
}

.platform-logo:hover img {
  opacity: 1;
}

.more-button-wrapper {
  margin-top: 24px;
}

</style>
