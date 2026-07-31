---
layout: home

hero:
  name: "Portway"
  text:
  tagline: Expose SQL databases, internal services, and files as MCP-tools and REST endpoints.
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: alt
      text: GitHub
      link: https://github.com/melosso/portway

features:
  - icon: |
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"/><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/></svg>
    title: SQL endpoints
    details: Expose tables and stored procedures via OData. Control access per column.
  - icon: |
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="17 1 21 5 17 9"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><polyline points="7 23 3 19 7 15"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/></svg>
    title: HTTP proxy
    details: Forward requests to existing services. Add auth and rate limiting.
  - icon: |
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M18 16.98h-5.99c-1.1 0-1.95.94-2.48 1.9A4 4 0 0 1 2 17c.01-.7.2-1.4.57-2"/><path d="m6 17 3.13-5.78c.53-.97.1-2.18-.5-3.1a4 4 0 1 1 6.89-4.06"/><path d="m12 6 3.13 5.73C15.66 12.7 16.9 13 18 13a4 4 0 0 1 0 8"/></svg>
    title: Webhooks
    details: Receive, validate, and process inbound webhooks.
---

<div class="home-platforms">

<span class="platforms-title">Available For</span>

<div class="platforms-list">
  <a href="guide/getting-started" class="platform-logo">
    <img src="icons/platforms/microsoft-windows.svg" alt="Windows" loading="lazy">
  </a>
  <a href="guide/deployment" class="platform-logo">
    <img src="icons/platforms/linux.svg" alt="Linux" loading="lazy">
  </a>
  <a href="guide/docker-compose" class="platform-logo">
    <img src="icons/platforms/docker.svg" alt="Docker" loading="lazy">
  </a>
  <a href="guide/deployment" class="platform-logo">
    <img src="icons/platforms/podman.svg" alt="Podman" loading="lazy">
  </a>
</div>

<div class="more-button-wrapper">
  <a class="more-button" href="guide/deployment">All deployment options →</a>
</div>

</div>

<style>

.home-platforms {
  margin-top: 48px;
  padding: 32px;
  text-align: center;
}

.platforms-title {
  font-size: 0.875rem;
  font-weight: 600;
  margin-bottom: 24px;
  color: var(--text-muted);
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
  background: var(--sidebar-bg);
  border: 1px solid var(--border);
  border-radius: 8px;
  transition: all 0.2s ease;
}

.platform-logo:hover {
  border-color: var(--accent-light);
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
  margin-bottom: 32px;
}


.more-button {
  display: inline-block;
  padding: 8px 20px;
  border: 1px solid var(--border);
  border-radius: 6px;
  color: var(--primary-color);
  font-size: 0.875rem;
  font-weight: 500;
  text-decoration: none;
  transition: all 0.2s ease;
}

.more-button:hover {
  border-color: var(--primary-color);
  background: var(--accent-light);
}

</style>
