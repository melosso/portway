// Dialog component - general-purpose modal dialog
// Usage:
//   Dialog.open({
//     id: 'myDialog',
//     title: 'Edit Item',
//     content: '...',
//     footer: '...',
//     closeOnBackdrop: true,
//     closeOnEscape: true,
//     onClose: () => { ... }
//   })
//   
//   Dialog.close('myDialog')
//   Dialog.getContent('myDialog') - returns content element for dynamic updates
(function(global) {
  'use strict';

  const Dialog = {
    _dialogs: {},
    _escHandler: null,

    open: function(options) {
      const {
        id,
        title = '',
        content = '',
        footer = '',
        closeOnBackdrop = true,
        closeOnEscape = true,
        showCloseButton = true,
        size = 'default', // 'small' | 'default' | 'large' | 'full'
        onOpen,
        onClose
      } = options;

      // Remove existing dialog with same ID
      if (this._dialogs[id]) {
        this.close(id);
      }

      // Create container
      const container = document.createElement('div');
      container.id = `dialog-${id}`;
      container.className = `dialog-wrapper`;
      
      const sizeClass = size !== 'default' ? `dialog-${size}` : '';
      const closeBtn = showCloseButton 
        ? `<button class="dialog-close" aria-label="Close" data-dialog-close>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          </button>`
        : '';

      container.innerHTML = `
        <div class="dialog-overlay open" data-dialog-overlay data-dialog-backdrop="${closeOnBackdrop ? 'true' : 'false'}">
          <div class="dialog ${sizeClass}" role="dialog" aria-modal="true" aria-labelledby="dialog-title-${id}">
            ${title ? `
              <div class="dialog-header">
                <h2 class="dialog-title" id="dialog-title-${id}">${title}</h2>
                ${closeBtn}
              </div>
            ` : closeBtn}
            <div class="dialog-content" id="dialog-content-${id}">
              ${content}
            </div>
            ${footer ? `<div class="dialog-footer">${footer}</div>` : ''}
          </div>
        </div>
      `;

      document.body.appendChild(container);

      // Store dialog state
      this._dialogs[id] = {
        options,
        container,
        closeOnEscape,
        onClose
      };

      // Focus first focusable element
      setTimeout(() => {
        const focusable = container.querySelector('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])');
        if (focusable) {
          focusable.focus();
        } else {
          container.querySelector('.dialog-close')?.focus();
        }
      }, 100);

      // Setup close handlers
      if (closeOnEscape) {
        this._setupEscapeHandler();
      }

      // Backdrop click
      container.querySelector('[data-dialog-overlay]')?.addEventListener('click', (e) => {
        if (e.target.hasAttribute('data-dialog-backdrop')) {
          this.close(id);
        }
      });

      // Close buttons
      container.querySelectorAll('[data-dialog-close]').forEach(btn => {
        btn.addEventListener('click', () => this.close(id));
      });

      // Dispatch open event
      container.dispatchEvent(new CustomEvent('dialog-open', { bubbles: true }));

      if (onOpen) onOpen(container);

      return container;
    },

    close: function(id) {
      const dialog = this._dialogs[id];
      if (!dialog) return;

      const { container, onClose } = dialog;
      const overlay = container?.querySelector('.dialog-overlay');
      
      if (overlay) {
        overlay.classList.remove('open');
        
        setTimeout(() => {
          container?.remove();
          delete this._dialogs[id];
          
          if (onClose) onClose();
          
          // Dispatch close event
          document.body.dispatchEvent(new CustomEvent('dialog-close', { 
            detail: { id },
            bubbles: true 
          }));
        }, 200);
      }

      // Manage escape handler
      const hasOpenDialogs = Object.keys(this._dialogs).length > 1;
      if (!hasOpenDialogs && this._escHandler) {
        document.removeEventListener('keydown', this._escHandler);
        this._escHandler = null;
      }
    },

    getContent: function(id) {
      return document.getElementById(`dialog-content-${id}`);
    },

    getFooter: function(id) {
      const wrapper = document.getElementById(`dialog-${id}`);
      return wrapper?.querySelector('.dialog-footer');
    },

    update: function(id, updates) {
      const container = document.getElementById(`dialog-${id}`);
      if (!container) return;

      if (updates.title) {
        const titleEl = container.querySelector('.dialog-title');
        if (titleEl) titleEl.textContent = updates.title;
      }

      if (updates.content !== undefined) {
        const contentEl = container.querySelector('.dialog-content');
        if (contentEl) contentEl.innerHTML = updates.content;
      }

      if (updates.footer !== undefined) {
        let footerEl = container.querySelector('.dialog-footer');
        if (updates.footer) {
          if (!footerEl) {
            footerEl = document.createElement('div');
            footerEl.className = 'dialog-footer';
            container.querySelector('.dialog-content').after(footerEl);
          }
          footerEl.innerHTML = updates.footer;
        } else if (footerEl) {
          footerEl.remove();
        }
      }
    },

    isOpen: function(id) {
      const wrapper = document.getElementById(`dialog-${id}`);
      return wrapper?.querySelector('.dialog-overlay.open') != null;
    },

    _setupEscapeHandler: function() {
      if (this._escHandler) return;
      
      this._escHandler = (e) => {
        if (e.key === 'Escape') {
          const openDialogs = Object.keys(this._dialogs);
          if (openDialogs.length > 0) {
            // Close the most recently opened dialog
            this.close(openDialogs[openDialogs.length - 1]);
          }
        }
      };
      
      document.addEventListener('keydown', this._escHandler);
    },

    // Convenience: show a form dialog
    form: function(options) {
      const {
        id,
        title,
        fields = [], // { name, label, type, value, placeholder, options }
        onSubmit,
        onCancel,
        submitLabel = 'Save',
        cancelLabel = 'Cancel'
      } = options;

      const fieldHtml = fields.map(field => {
        const id = `field-${field.name}`;
        let inputHtml = '';

        switch (field.type) {
          case 'textarea':
            inputHtml = `<textarea id="${id}" name="${field.name}" placeholder="${field.placeholder || ''}" rows="4">${field.value || ''}</textarea>`;
            break;
          case 'select':
            const options = (field.options || []).map(o => 
              `<option value="${o.value}" ${o.value === field.value ? 'selected' : ''}>${o.label}</option>`
            ).join('');
            inputHtml = `<select id="${id}" name="${field.name}">${options}</select>`;
            break;
          case 'checkbox':
            inputHtml = `<label class="dialog-checkbox"><input type="checkbox" name="${field.name}" ${field.value ? 'checked' : ''}><span>${field.checkboxLabel || ''}</span></label>`;
            break;
          default:
            inputHtml = `<input type="${field.type || 'text'}" id="${id}" name="${field.name}" value="${field.value || ''}" placeholder="${field.placeholder || ''}">`;
        }

        return `
          <div class="dialog-field">
            ${field.type !== 'checkbox' ? `<label for="${id}">${field.label}</label>` : ''}
            ${inputHtml}
          </div>
        `;
      }).join('');

      const footerHtml = `
        <button type="button" class="btn btn-outline" data-dialog-close>${cancelLabel}</button>
        <button type="button" class="btn btn-primary" id="dialog-submit-${id}">${submitLabel}</button>
      `;

      const container = this.open({
        id,
        title,
        content: `<form class="dialog-form">${fieldHtml}</form>`,
        footer: footerHtml,
        onClose: onCancel
      });

      // Handle submit
      const submitBtn = document.getElementById(`dialog-submit-${id}`);
      submitBtn?.addEventListener('click', (e) => {
        e.preventDefault();
        
        const formData = new FormData(container.querySelector('form'));
        const data = Object.fromEntries(formData);
        
        // Handle checkboxes
        fields.forEach(field => {
          if (field.type === 'checkbox') {
            data[field.name] = container.querySelector(`[name="${field.name}"]`)?.checked || false;
          }
        });

        if (onSubmit) {
          onSubmit(data, () => this.close(id));
        }
      });

      return container;
    }
  };

  // Export
  global.Dialog = Dialog;

})(typeof window !== 'undefined' ? window : this);
