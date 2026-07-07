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
      text: GitHub
      link: https://github.com/melosso/portway

features:
  - title: SQL endpoints
    details: Expose tables and stored procedures via OData. Control access per column.
  - title: HTTP proxy
    details: Forward requests to existing services. Add auth and rate limiting.
  - title: Webhooks
    details: Receive, validate, and process inbound webhooks.
  - title: Token auth.
    details: Scoped Bearer tokens fully restricted per configuration.
  - title: Isolation
    details: Separate environments with independent connection strings.
  - title: Web UI
    details: Manage tokens, browse logs, and monitor health.
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

.bark-feature::before {
  content: '';
  display: block;
  width: 32px;
  height: 32px;
  margin: 24px auto 12px;
  background-size: contain;
  background-repeat: no-repeat;
  background-position: center;
}

.bark-feature:nth-child(1)::before {
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 24 24' fill='none' stroke='%233E63DD' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'%3E%3Cellipse cx='12' cy='5' rx='9' ry='3'/%3E%3Cpath d='M21 12c0 1.66-4 3-9 3s-9-1.34-9-3'/%3E%3Cpath d='M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5'/%3E%3C/svg%3E");
}

.bark-feature:nth-child(2)::before {
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 24 24' fill='none' stroke='%233E63DD' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='17 1 21 5 17 9'/%3E%3Cpath d='M3 11V9a4 4 0 0 1 4-4h14'/%3E%3Cpolyline points='7 23 3 19 7 15'/%3E%3Cpath d='M21 13v2a4 4 0 0 1-4 4H3'/%3E%3C/svg%3E");
}

.bark-feature:nth-child(3)::before {
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 24 24' fill='none' stroke='%233E63DD' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='M18 16.98h-5.99c-1.1 0-1.95.94-2.48 1.9A4 4 0 0 1 2 17c.01-.7.2-1.4.57-2'/%3E%3Cpath d='m6 17 3.13-5.78c.53-.97.1-2.18-.5-3.1a4 4 0 1 1 6.89-4.06'/%3E%3Cpath d='m12 6 3.13 5.73C15.66 12.7 16.9 13 18 13a4 4 0 0 1 0 8'/%3E%3C/svg%3E");
}

.bark-feature:nth-child(4)::before {
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 24 24' fill='none' stroke='%233E63DD' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='M12 2a7 7 0 0 0-7 7c0 2.4 1.2 4.5 3 5.7V21l2-2 2 2 2-2 2 2v-6.3c1.8-1.2 3-3.3 3-5.7a7 7 0 0 0-7-7z'/%3E%3Ccircle cx='12' cy='9' r='2'/%3E%3C/svg%3E");
}

.bark-feature:nth-child(5)::before {
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 24 24' fill='none' stroke='%233E63DD' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'%3E%3Crect x='3' y='3' width='7' height='9'/%3E%3Crect x='14' y='3' width='7' height='5'/%3E%3Crect x='14' y='12' width='7' height='9'/%3E%3Crect x='3' y='16' width='7' height='5'/%3E%3C/svg%3E");
}

.bark-feature:nth-child(6)::before {
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 24 24' fill='none' stroke='%233E63DD' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'%3E%3Crect x='2' y='3' width='20' height='14' rx='2' ry='2'/%3E%3Cline x1='8' y1='21' x2='16' y2='21'/%3E%3Cline x1='12' y1='17' x2='12' y2='21'/%3E%3C/svg%3E");
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
