---
layout: home

hero:
  name: Portway
  text: Bridge your infrastructure to AI and REST.
  tagline: Expose SQL databases, internal services and files as MCP tools and OData endpoints.
  image: /assets/brand-image.svg
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: minimal
      text: View on GitHub
      link: https://github.com/melosso/portway

features:
  - icon: |
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2a10 10 0 1 0 10 10H12V2z"/><path d="M12 12 2.1 12.1"/><path d="M12 12v9.9"/><path d="M12 12l7.07-7.07"/></svg>
    title: MCP & OData Support
    details: Expose SQL databases and internal APIs as Model Context Protocol tools or OData REST endpoints.
  - icon: |
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
    title: Secure Service Routing
    details: Column allowlists, tokens scoped to endpoints, environments and tenants, per-token rate limits and Azure Key Vault integration.
  - icon: |
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>
    title: File-Based Configuration
    details: Endpoints and environments are JSON files, reloaded on change. Audit logging, caching and generated OpenAPI docs included.
---

<div class="home-platforms">

<span class="platforms-title">Available For</span>

<div class="platforms-list">
  <a href="guide/deployment-windows" class="platform-logo">
    <img src="icons/platforms/microsoft-windows.svg" alt="" loading="lazy"><span>Windows</span>
  </a>
  <a href="guide/deployment" class="platform-logo">
    <img src="icons/platforms/linux.svg" alt="" loading="lazy"><span>Linux</span>
  </a>
  <a href="guide/deployment-docker" class="platform-logo">
    <img src="icons/platforms/docker.svg" alt="" loading="lazy"><span>Docker</span>
  </a>
  <a href="guide/deployment" class="platform-logo">
    <img src="icons/platforms/podman.svg" alt="" loading="lazy"><span>Podman</span>
  </a>
</div>

<div class="more-button-wrapper">
  <a class="more-button" href="guide/deployment">All deployment options →</a>
</div>

</div>

<style>

.home-platforms {
  margin: 48px 0 32px;
  text-align: center;
}

.platforms-title {
  display: block;
  margin-bottom: 16px;
  font-size: 0.75rem;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--text-muted);
}

.platforms-list {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 12px;
}

.platform-logo {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px 8px 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--bg-color);
  color: var(--text-color);
  font-size: 0.875rem;
  font-weight: 500;
  text-decoration: none;
  transition: border-color 0.15s ease, background-color 0.15s ease;
}

.platform-logo:hover {
  border-color: var(--accent);
  background: var(--accent-light);
}

.platform-logo:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.platform-logo img {
  width: 20px;
  height: 20px;
}

.more-button-wrapper {
  margin-top: 20px;
}

.more-button {
  color: var(--accent);
  font-size: 0.875rem;
  font-weight: 500;
  text-decoration: none;
}

.more-button:hover {
  text-decoration: underline;
}

</style>
