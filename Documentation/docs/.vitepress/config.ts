import { defineConfig } from 'vitepress';
import { withMermaid } from "vitepress-plugin-mermaid";
import footnote from 'markdown-it-footnote';
import taskLists from 'markdown-it-task-checkbox';

export default withMermaid(
  defineConfig({
  title: 'Portway',
  description: 'A lightweight API gateway for Windows environments',
  head: [['link', { rel: 'icon', href: '/favicon.ico' }]],
  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Guide', link: '/guide/' },
      { text: 'Reference', link: '/reference/' },
      {
        text: 'More',
        items: [
          { text: 'Download', link: 'https://github.com/melosso/portway/releases/' },
          { text: 'Demo page', link: 'https://portway-demo.melosso.com/' }
        ]
      }
      
    ],
    sidebar: {
      '/guide/': [
        {
          text: 'Introduction',
          items: [
            { text: 'What is Portway?', link: '/guide/' },
            { text: 'Getting Started', link: '/guide/getting-started' },
            { text: 'Structure', link: '/guide/routing' },
            { text: 'Deploying', link: '/guide/deployment' }
          ]
        },
        {
          text: 'Endpoints',
          items: [
            { text: 'SQL', link: '/guide/endpoints-sql' },
            { text: 'Proxy', link: '/guide/endpoints-proxy' },
            { text: 'Static', link: '/guide/endpoints-static' },
            { text: 'Composite', link: '/guide/endpoints-composite' },
            { text: 'File System', link: '/guide/endpoints-file' },
            { text: 'Webhook', link: '/guide/endpoints-webhook' }            
          ]
        },
        {
          text: 'Configuration',
          items: [
            { text: 'Environments', link: '/guide/environments' },
            { text: 'Security', link: '/guide/security' },
            { text: 'Rate Limiting', link: '/guide/rate-limiting' },
            { text: 'Versioning', link: '/guide/versioning' },
            { text: 'Licensing', link: '/guide/licensing' }

          ]
        },
        {
          text: 'Operations',
          items: [
            { text: 'Monitoring', link: '/guide/monitoring' },
            { text: 'Troubleshooting', link: '/guide/troubleshooting' },
            { text: 'Upgrading', link: '/guide/upgrading' }
          ]
        },
        {
          text: 'Contributing',
          items: [
            { text: 'Bugs', link: 'https://github.com/melosso/portway/issues' },
            { text: 'Suggestions', link: 'https://github.com/melosso/portway/issues/' },
          ]
        },
        {
           text: 'Coding & API Reference', link: '/reference/' 
        },
        {
          text: 'Integrations', link: '/reference/integrations/' 
        }
      ],
      '/reference/': [
        {
          text: 'API Reference',
          items: [
            { text: 'Overview', link: '/reference/' },
            { text: 'Authentication', link: '/reference/authentication' },
            { text: 'HTTP Headers', link: '/reference/headers' }
          ]
        },
        {
          text: 'Configuration',
          items: [
            { text: 'Entity', link: '/reference/entity-config' },
            { text: 'Environment', link: '/reference/environment-settings' },
            { text: 'Namespaces', link: '/reference/namespaces' }
          ]
        },
        {         
          text: 'Settings',
          items: [
            { text: 'Application', link: '/reference/app-settings' },
            { text: 'OpenAPI', link: '/reference/openapi-settings' },
          ]
        },
        {
          text: 'Query Language',
          items: [
            { text: 'OData Syntax', link: '/reference/odata' },
            { text: 'Filter Operations', link: '/reference/filters' },
            { text: 'Sorting & Pagination', link: '/reference/sorting-pagination' }
          ]
        },
        {
          text: 'Tools & Options',
          items: [
            { text: 'Token Generator', link: '/reference/token-generator' },
            { text: 'Secret Encryption', link: '/reference/secrets' },
            { text: 'Health Checks', link: '/reference/health-checks' },
            { text: 'Caching', link: '/reference/caching' },
            { text: 'Logging', link: '/reference/logging' },
            { text: 'Audit', link: '/reference/audit' }
          ]
        },
        {
          text: 'Integrations',
          items: [
            { text: 'Exact Globe+', link: '/reference/integrations/exact-globe' },
            { text: 'Exact Synergy', link: '/reference/integrations/exact-synergy' },
            { text: 'NAV Business Central', link: '/reference/integrations/nav-business-central' }
          ]
        }
      ]
    },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/melosso/portway' }
    ],
    search: {
      provider: 'local'
    },
    footer: {
      message: 'Released under the GNU AGPL (3.0) License.',
      copyright: 'Copyright © 2025-Present Melosso.com'
    }
  },
  vite: {
    server: {
      host: true, // This allows external access
      allowedHosts: ['localhost', '0.0.0.0', 'portway-docs.melosso.com']
    }
  },
  markdown: {
    config(md) {
      md.use(footnote);
      md.use(taskLists, {
        disabled: true,
        divWrap: false,
        divClass: 'checkbox',
        idPrefix: 'cbx_',
        ulClass: 'task-list',
        liClass: 'task-list-item',
      });
    }
  }
})
);
