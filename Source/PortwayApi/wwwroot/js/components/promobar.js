// Promo bar with **bold**, *italic* and [link](url) markdown
(function (global) {
    'use strict';

    const PromoBar = {
        init: function () {
            if (sessionStorage.getItem('portway_promo_closed')) return;

            // Try cache to avoid layout shift/flicker
            try {
                const cached = localStorage.getItem('portway_promo_cache');
                if (cached) {
                    const data = JSON.parse(cached);
                    if (data && data.text) {
                        const isLogin =
                            window.location.pathname.endsWith('/_/login') ||
                            window.location.pathname.endsWith('/_/login.html');
                        if (!(isLogin && !data.login)) {
                            this.render(data.text);
                        }
                    }
                }
            } catch (e) {}

            this.refresh();
        },

        refresh: async function () {
            try {
                const data = await api('/_/api/customization', { silent: true });

                const isLogin =
                    window.location.pathname.endsWith('/_/login') || window.location.pathname.endsWith('/_/login.html');
                const shouldShow = data.promo_text && !(isLogin && !data.promo_login);

                localStorage.setItem(
                    'portway_promo_cache',
                    JSON.stringify({
                        text: data.promo_text || '',
                        login: data.promo_login || false
                    })
                );

                const existing = document.getElementById('portwayPromoBar');

                if (shouldShow) {
                    const html = this.parseMarkdown(data.promo_text);
                    if (existing) {
                        const content = existing.querySelector('.promo-bar-content');
                        if (content && content.innerHTML !== html) {
                            content.innerHTML = html;
                        }
                    } else {
                        this.render(data.promo_text);
                    }
                } else if (existing) {
                    existing.remove();
                    document.body.classList.remove('has-promo');
                }
            } catch (err) {
                console.error('Failed to load promo text:', err);
            }
        },

        render: function (text) {
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

            container.querySelector('.promo-bar-close').addEventListener('click', () => {
                container.classList.remove('visible');
                document.body.classList.remove('has-promo');
                sessionStorage.setItem('portway_promo_closed', 'true');
                setTimeout(() => container.remove(), 150);
            });
        },

        parseMarkdown: function (text) {
            if (!text) return '';

            return (
                text
                    // Escape HTML; the quote matters because link hrefs are built as attributes below
                    .replace(/&/g, '&amp;')
                    .replace(/</g, '&lt;')
                    .replace(/>/g, '&gt;')
                    .replace(/"/g, '&quot;')
                    .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
                    .replace(/__(.*?)__/g, '<strong>$1</strong>')
                    .replace(/\*(.*?)\*/g, '<em>$1</em>')
                    .replace(/_(.*?)_/g, '<em>$1</em>')
                    // Links: [text](url), http(s)/mailto only so javascript: and data: cannot ride in
                    .replace(/\[(.*?)\]\((.*?)\)/g, function (match, label, url) {
                        return /^(https?:\/\/|mailto:|\/)/i.test(url.trim())
                            ? '<a href="' + url.trim() + '" target="_blank" rel="noopener noreferrer">' + label + '</a>'
                            : label;
                    })
            );
        }
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => PromoBar.init());
    } else {
        PromoBar.init();
    }

    global.PromoBar = PromoBar;
})(typeof window !== 'undefined' ? window : this);
