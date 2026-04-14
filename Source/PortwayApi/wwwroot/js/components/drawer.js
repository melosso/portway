// Drawer component - slide-in panel from right
// Usage:
//   HTML: 
//     <div class="drawer-backdrop" id="myDrawerBackdrop"></div>
//     <div class="drawer" id="myDrawer">
//       <div class="drawer-header">...</div>
//       <div class="drawer-body">...</div>
//       <div class="drawer-footer">...</div>
//     </div>
//   JS:
//     Drawer.open('myDrawer')
//     Drawer.close('myDrawer')
//     Drawer.toggle('myDrawer')
//     Drawer.onClose('myDrawer', callback) - called when drawer closes
(function(global) {
  'use strict';

  const Drawer = {
    _closeCallbacks: {},
    // Track dirty state per drawer to warn on unsaved edits
    _dirtyState: {},

  _findBackdrop: function(drawerId) {
    return document.querySelector(`[data-drawer-backdrop="${drawerId}"]`)
        || document.getElementById(drawerId + 'Backdrop');
  },

  open: function(drawerId) {
    const backdrop = this._findBackdrop(drawerId);
    const drawer = document.getElementById(drawerId);

    if (backdrop) backdrop.classList.add('open');
    if (drawer) drawer.classList.add('open');

    // Reset dirty state when drawer opens
    this._dirtyState[drawerId] = false;

    // Add body class for blur effect
    document.body.classList.add('drawer-open');

    // Trap focus in drawer
    this._trapFocus(drawer);
  },

  // Mark a drawer as having unsaved changes
  markDirty: function(drawerId) {
    this._dirtyState[drawerId] = true;
  },

  clearDirty: function(drawerId) {
    this._dirtyState[drawerId] = false;
  },

  close: function(drawerId, force = false) {
    // Warn before closing with unsaved changes
    if (!force && this._dirtyState[drawerId]) {
      if (!window.confirm('You have unsaved changes. Close without saving?')) {
        return;
      }
    }

    const backdrop = this._findBackdrop(drawerId);
    const drawer = document.getElementById(drawerId);

    if (backdrop) backdrop.classList.remove('open');
    if (drawer) drawer.classList.remove('open');

    this._dirtyState[drawerId] = false;

    // Remove body class if no other drawers are open
    const openDrawers = document.querySelectorAll('.drawer-backdrop.open');
    if (openDrawers.length === 0) {
      document.body.classList.remove('drawer-open');
    }

    // Call close callback if registered
    if (this._closeCallbacks[drawerId]) {
      this._closeCallbacks[drawerId]();
    }
  },

    toggle: function(drawerId) {
      const drawer = document.getElementById(drawerId);
      if (drawer && drawer.classList.contains('open')) {
        this.close(drawerId);
      } else {
        this.open(drawerId);
      }
    },

    isOpen: function(drawerId) {
      const drawer = document.getElementById(drawerId);
      return drawer && drawer.classList.contains('open');
    },

    onClose: function(drawerId, callback) {
      this._closeCallbacks[drawerId] = callback;
    },

    // Create drawer HTML programmatically
    create: function(options) {
      const {
        id,
        title = '',
        subtitle = '',
        showClose = true,
        headerContent = '',
        bodyContent = '',
        footerContent = '',
        position = 'right'
      } = options;

      const closeBtn = showClose 
        ? `<button class="btn-icon" data-drawer-close="${id}">
             <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
           </button>`
        : '';

      const html = `
        <div class="drawer-backdrop" id="${id}Backdrop" data-drawer-backdrop="${id}"></div>
        <div class="drawer${position === 'left' ? ' drawer-left' : ''}" id="${id}">
          <div class="drawer-header">
            <div>
              <div class="drawer-title">${title}</div>
              ${subtitle ? `<div class="drawer-subtitle">${subtitle}</div>` : ''}
              ${headerContent}
            </div>
            ${closeBtn}
          </div>
          <div class="drawer-body">${bodyContent}</div>
          ${footerContent ? `<div class="drawer-footer">${footerContent}</div>` : ''}
        </div>
      `;

      return html;
    },

    // Set up automatic backdrop click to close
    init: function() {
      document.addEventListener('click', (e) => {
        const backdrop = e.target.closest('[data-drawer-backdrop]');
        if (backdrop) {
          const drawerId = backdrop.getAttribute('data-drawer-backdrop');
          this.close(drawerId);
        }
        
        const closeBtn = e.target.closest('[data-drawer-close]');
        if (closeBtn) {
          const drawerId = closeBtn.getAttribute('data-drawer-close');
          this.close(drawerId);
        }
      });

      // Close on escape
      document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
          const openDrawer = document.querySelector('.drawer.open');
          if (openDrawer) {
            const drawerId = openDrawer.id;
            const backdrop = document.getElementById(drawerId + 'Backdrop');
            if (backdrop && backdrop.classList.contains('open')) {
              this.close(drawerId);
            }
          }
        }
      });
    },

    _trapFocus: function(drawer) {
      if (!drawer) return;
      // Simple focus management - focus first focusable element
      const focusable = drawer.querySelector('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])');
      if (focusable) {
        setTimeout(() => focusable.focus(), 100);
      }
    }
  };

  // Auto-init when DOM ready
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => Drawer.init());
  } else {
    Drawer.init();
  }

  // Export
  global.Drawer = Drawer;

})(typeof window !== 'undefined' ? window : this);
