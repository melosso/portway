// Promo Bar component - matching shadcn-ui design
// Supports simple markdown: **bold**, [link](url), *italic*
(function(global) {
  'use strict';

  const PromoBar = {
    init: function() {
      // Don't show if user closed it this session
      if (sessionStorage.getItem('portway_promo_closed')) return;

      // Try cache to avoid layout shift/flicker
      try {
        const cached = localStorage.getItem('portway_promo_cache');
        if (cached) {
          const data = JSON.parse(cached);
          if (data && data.text) {
            const isLogin = window.location.pathname.endsWith('/ui/login') || window.location.pathname.endsWith('/ui/login.html');
            if (!(isLogin && !data.login)) {
              this.render(data.text);
            }
          }
        }
      } catch (e) {}

      // Fetch fresh version in background
      this.refresh();
    },

    refresh: async function() {
      try {
        const response = await fetch('/ui/api/customization');
        if (!response.ok) return;
        const data = await response.json();
        
        const isLogin = window.location.pathname.endsWith('/ui/login') || window.location.pathname.endsWith('/ui/login.html');
        const shouldShow = data.promo_text && !(isLogin && !data.promo_login);

        // Cache for next page load
        localStorage.setItem('portway_promo_cache', JSON.stringify({
          text: data.promo_text || '',
          login: data.promo_login || false
        }));

        const existing = document.getElementById('portwayPromoBar');

        if (shouldShow) {
          const html = this.parseMarkdown(data.promo_text);
          if (existing) {
            // Update if changed
            const content = existing.querySelector('.promo-bar-content');
            if (content && content.innerHTML !== html) {
              content.innerHTML = html;
            }
          } else {
            // Render if not already there (first time visit or previously hidden)
            this.render(data.promo_text);
          }
        } else if (existing) {
          // Hide if it was there but now shouldn't be
          existing.remove();
          document.body.classList.remove('has-promo');
        }
      } catch (err) {
        console.error('Failed to load promo text:', err);
      }
    },

    render: function(text) {
      if (!text) return;

      const html = this.parseMarkdown(text);
      const container = document.createElement('div');
      container.className = 'promo-bar visible';
      container.id = 'portwayPromoBar';
      
      container.innerHTML = `
        <div class="promo-bar-content">${html}</div>
        <button class="promo-bar-close" title="Close">
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
        </button>
      `;

      // Apply layout shift (must be before prepend for smoother layout calculation in some browsers)
      document.body.classList.add('has-promo');
      document.body.prepend(container);

      // Handle close
      container.querySelector('.promo-bar-close').addEventListener('click', () => {
        container.classList.remove('visible');
        document.body.classList.remove('has-promo');
        sessionStorage.setItem('portway_promo_closed', 'true');
        // Remove from DOM after transition
        setTimeout(() => container.remove(), 150);
      });
    },

    parseMarkdown: function(text) {
      if (!text) return '';
      
      return text
        // Escape HTML
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        // Bold: **text** or __text__
        .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
        .replace(/__(.*?)__/g, '<strong>$1</strong>')
        // Italic: *text* or _text_
        .replace(/\*(.*?)\*/g, '<em>$1</em>')
        .replace(/_(.*?)_/g, '<em>$1</em>')
        // Links: [text](url)
        .replace(/\[(.*?)\]\((.*?)\)/g, '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');
    }
  };

  // Auto-init when DOM ready
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => PromoBar.init());
  } else {
    PromoBar.init();
  }

  global.PromoBar = PromoBar;

})(typeof window !== 'undefined' ? window : this);
