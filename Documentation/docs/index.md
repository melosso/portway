---
layout: home

hero:
  name: Portway
  text: Bridge your infrastructure to AI and REST.
  tagline: Instantly expose SQL databases, internal services, and files as secure MCP tools and OData endpoints.
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: alt
      text: GitHub
      link: https://github.com/melosso/portway

features:
  - icon: |
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2a10 10 0 1 0 10 10H12V2z"/><path d="M12 12 2.1 12.1"/><path d="M12 12v9.9"/><path d="M12 12l7.07-7.07"/></svg>
    title: MCP & OData Support
    details: Expose SQL databases, webhooks, and internal APIs as AI tools using the Model Context Protocol or standard OData REST endpoints.
  - icon: |
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
    title: Secure Service Routing
    details: Control access down to specific SQL columns with granular rate limiting, Azure Key Vault integration, and request validation. All fully documented.
  - icon: |
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>
    title: File-Based Configuration
    details: Set up endpoints and environments using simple JSON configs. Includes full audit logging, caching, and automated docs out of the box.
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
