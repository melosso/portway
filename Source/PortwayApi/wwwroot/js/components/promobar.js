// Promo Bar component - matching shadcn-ui design
// Supports simple markdown: **bold**, [link](url), *italic*
(function(global) {
  'use strict';

  const PromoBar = {
    init: async function() {
      // Don't show if user closed it this session or globally (optional)
      if (sessionStorage.getItem('portway_promo_closed')) return;

      try {
        const response = await fetch('/ui/api/overview');
        const data = await response.json();
        
        if (data.promo_text) {
          this.render(data.promo_text);
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

      // Apply layout shift
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
