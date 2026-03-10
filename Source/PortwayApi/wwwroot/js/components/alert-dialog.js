// Alert Dialog component - modal confirmation dialog
// Usage:
//   HTML: <div id="alertDialogContainer"></div>
//   JS:
//     AlertDialog.show({
//       title: 'Delete item?',
//       description: 'This action cannot be undone.',
//       actionLabel: 'Delete',
//       onConfirm: () => { ... },
//       onCancel: () => { ... },
//       variant: 'destructive' // optional: 'default' | 'destructive'
//     })
(function(global) {
  'use strict';

  const icons = {
    default: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>',
    destructive: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>',
    success: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>',
    info: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>'
  };

  const defaultOptions = {
    title: 'Confirm Action',
    description: '',
    actionLabel: 'Confirm',
    cancelLabel: 'Cancel',
    variant: 'default',
    extraHtml: '',
    showCancel: true,
    showCloseButton: true
  };

  let _currentConfirmCallback = null;
  let _currentCancelCallback = null;

  function getContainer() {
    let container = document.getElementById('alertDialogContainer');
    if (!container) {
      container = document.createElement('div');
      container.id = 'alertDialogContainer';
      document.body.appendChild(container);
    }
    return container;
  }

  function esc(s) {
    if (s == null) return '';
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  const AlertDialog = {
    show: function(options) {
      const opts = { ...defaultOptions, ...options };
      const container = getContainer();
      
      _currentConfirmCallback = opts.onConfirm || null;
      _currentCancelCallback = opts.onCancel || null;

      const iconHtml = icons[opts.variant] || icons.default;
      const actionBtnClass = opts.variant === 'destructive' ? 'btn-destructive' : 'btn-primary';

      container.innerHTML = `
        <div class="alert-dialog-overlay open" id="alertDialogOverlay">
          <div class="alert-dialog" role="alertdialog" aria-modal="true" aria-labelledby="alertDialogTitle" aria-describedby="alertDialogDescription">
            <div class="alert-dialog-icon">
              ${iconHtml}
            </div>
            <div>
              <div class="alert-dialog-title" id="alertDialogTitle">${esc(opts.title)}</div>
              ${opts.description ? `<div class="alert-dialog-description" id="alertDialogDescription">${esc(opts.description)}</div>` : ''}
            </div>
            ${opts.extraHtml ? `<div class="alert-dialog-extra">${opts.extraHtml}</div>` : ''}
            <div class="alert-dialog-footer">
              ${opts.showCancel ? `<button class="btn btn-outline" id="alertDialogCancel">${esc(opts.cancelLabel)}</button>` : ''}
              <button class="btn ${actionBtnClass}" id="alertDialogAction">${esc(opts.actionLabel)}</button>
            </div>
          </div>
        </div>
      `;

      // Event listeners
      document.getElementById('alertDialogCancel')?.addEventListener('click', this.hide);
      document.getElementById('alertDialogAction')?.addEventListener('click', () => {
        if (_currentConfirmCallback) {
          _currentConfirmCallback();
        }
        this.hide();
      });
      
      document.getElementById('alertDialogOverlay')?.addEventListener('click', (e) => {
        if (e.target.id === 'alertDialogOverlay') {
          if (_currentCancelCallback) {
            _currentCancelCallback();
          }
          this.hide();
        }
      });

      // Focus trap
      const actionBtn = document.getElementById('alertDialogAction');
      if (actionBtn) {
        setTimeout(() => actionBtn.focus(), 100);
      }

      // ESC key handler
      this._escHandler = (e) => {
        if (e.key === 'Escape') {
          if (_currentCancelCallback) {
            _currentCancelCallback();
          }
          this.hide();
        }
      };
      document.addEventListener('keydown', this._escHandler);
    },

    hide: function() {
      const container = getContainer();
      const overlay = document.getElementById('alertDialogOverlay');
      
      if (overlay) {
        overlay.classList.remove('open');
        setTimeout(() => {
          container.innerHTML = '';
          _currentConfirmCallback = null;
          _currentCancelCallback = null;
        }, 200);
      }

      if (this._escHandler) {
        document.removeEventListener('keydown', this._escHandler);
        this._escHandler = null;
      }
    },

    // Convenience methods
    confirm: function(options) {
      return this.show({ variant: 'default', ...options });
    },

    danger: function(options) {
      return this.show({ variant: 'destructive', actionLabel: 'Delete', ...options });
    },

    success: function(options) {
      return this.show({ variant: 'success', ...options });
    },

    info: function(options) {
      return this.show({ variant: 'info', ...options });
    }
  };

  // Export
  global.AlertDialog = AlertDialog;

})(typeof window !== 'undefined' ? window : this);
