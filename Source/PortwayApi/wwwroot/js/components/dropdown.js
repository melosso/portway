// Dropdown Menu component - click/hover activated menu
// Usage:
//   HTML:
//     <div class="dropdown">
//       <button class="dropdown-trigger">Menu</button>
//       <div class="dropdown-content">
//         <button class="dropdown-item" data-value="action1">Action 1</button>
//         <button class="dropdown-item" data-value="action2">Action 2</button>
//         <div class="dropdown-separator"></div>
//         <button class="dropdown-item" data-value="action3">Action 3</button>
//       </div>
//     </div>
//   
//   JS:
//     Dropdown.init() - auto-initialize all dropdowns
//     Dropdown.toggle(dropdownTrigger) - toggle specific dropdown
//     Dropdown.closeAll() - close all dropdowns
(function(global) {
  'use strict';

  const Dropdown = {
    _activeDropdown: null,

    init: function() {
      // Handle trigger clicks
      document.querySelectorAll('.dropdown-trigger').forEach(trigger => {
        trigger.addEventListener('click', (e) => {
          e.stopPropagation();
          const dropdown = trigger.closest('.dropdown');
          if (dropdown) {
            this.toggle(dropdown);
          }
        });

        // For keyboard accessibility
        trigger.addEventListener('keydown', (e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            const dropdown = trigger.closest('.dropdown');
            if (dropdown) {
              this.toggle(dropdown);
            }
          }
          if (e.key === 'ArrowDown' && trigger.closest('.dropdown').querySelector('.dropdown-content')) {
            e.preventDefault();
            this.toggle(dropdown.closest('.dropdown'));
            const firstItem = dropdown.querySelector('.dropdown-item');
            if (firstItem) firstItem.focus();
          }
        });
      });

      // Handle item clicks
      document.querySelectorAll('.dropdown-item').forEach(item => {
        item.addEventListener('click', (e) => {
          const value = item.dataset.value;
          const label = item.textContent?.trim();
          
          // Dispatch custom event
          item.dispatchEvent(new CustomEvent('dropdown-select', {
            detail: { value, label, item },
            bubbles: true
          }));

          this.closeAll();
        });

        // Keyboard navigation within dropdown
        item.addEventListener('keydown', (e) => {
          const items = Array.from(item.parentElement.querySelectorAll('.dropdown-item:not([disabled])'));
          const currentIndex = items.indexOf(item);

          if (e.key === 'ArrowDown') {
            e.preventDefault();
            const nextIndex = (currentIndex + 1) % items.length;
            items[nextIndex]?.focus();
          } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            const prevIndex = (currentIndex - 1 + items.length) % items.length;
            items[prevIndex]?.focus();
          } else if (e.key === 'Escape') {
            e.preventDefault();
            this.closeAll();
            const trigger = item.closest('.dropdown')?.querySelector('.dropdown-trigger');
            trigger?.focus();
          } else if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            item.click();
          }
        });
      });

      // Close on outside click
      document.addEventListener('click', (e) => {
        if (!e.target.closest('.dropdown')) {
          this.closeAll();
        }
      });

      // Close on escape
      document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
          this.closeAll();
        }
      });

      // Handle item hover for focus
      document.querySelectorAll('.dropdown-item').forEach(item => {
        item.addEventListener('mouseenter', () => {
          if (document.activeElement === document.body) {
            item.focus();
          }
        });
      });
    },

    toggle: function(dropdown) {
      const content = dropdown.querySelector('.dropdown-content');
      const isOpen = content?.classList.contains('open');

      this.closeAll();

      if (!isOpen && content) {
        content.classList.add('open');
        this._activeDropdown = dropdown;
        this._position(content, dropdown);
        
        // Focus first item
        const firstItem = content.querySelector('.dropdown-item');
        if (firstItem) {
          setTimeout(() => firstItem.focus(), 50);
        }
      }
    },

    close: function(dropdown) {
      const content = dropdown?.querySelector('.dropdown-content');
      if (content) {
        content.classList.remove('open');
      }
      if (this._activeDropdown === dropdown) {
        this._activeDropdown = null;
      }
    },

    closeAll: function() {
      document.querySelectorAll('.dropdown-content.open').forEach(content => {
        content.classList.remove('open');
      });
      this._activeDropdown = null;
    },

    _position: function(content, dropdown) {
      const trigger = dropdown.querySelector('.dropdown-trigger');
      if (!trigger || !content) return;

      const triggerRect = trigger.getBoundingClientRect();
      const contentRect = content.getBoundingClientRect();
      const gap = 4;

      // Default: align to bottom-left of trigger
      let top = triggerRect.bottom + gap + window.scrollY;
      let left = triggerRect.left + window.scrollX;

      // Check if it fits below
      const fitsBelow = top + contentRect.height <= window.innerHeight - gap;
      
      // Check if it fits to the right
      const fitsRight = left + contentRect.width <= window.innerWidth - gap;

      if (!fitsBelow) {
        // Show above
        top = triggerRect.top - contentRect.height - gap + window.scrollY;
      }

      if (!fitsRight) {
        // Align to right
        left = triggerRect.right - contentRect.width + window.scrollX;
      }

      // Ensure doesn't go off left edge
      if (left < gap) left = gap;

      content.style.top = `${top}px`;
      content.style.left = `${left}px`;
    },

    // Create dropdown programmatically
    create: function(options) {
      const {
        id,
        triggerLabel = 'Menu',
        triggerIcon = '',
        items = [], // { value, label, icon, disabled, separator }
        onSelect
      } = options;

      const itemsHtml = items.map(item => {
        if (item.separator) {
          return '<div class="dropdown-separator"></div>';
        }
        const iconHtml = item.icon ? `<span class="dropdown-item-icon">${item.icon}</span>` : '';
        const disabledAttr = item.disabled ? 'disabled' : '';
        return `<button class="dropdown-item" data-value="${item.value}" ${disabledAttr}>${iconHtml}<span>${item.label}</span></button>`;
      }).join('');

      const triggerHtml = triggerIcon 
        ? `<span class="dropdown-trigger-icon">${triggerIcon}</span>`
        : '';

      const html = `
        <div class="dropdown" id="${id}">
          <button class="dropdown-trigger" aria-haspopup="true" aria-expanded="false">
            ${triggerHtml}
            <span>${triggerLabel}</span>
            <svg class="dropdown-chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
          </button>
          <div class="dropdown-content">
            ${itemsHtml}
          </div>
        </div>
      `;

      return html;
    }
  };

  // Auto-init
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => Dropdown.init());
  } else {
    Dropdown.init();
  }

  // Export
  global.Dropdown = Dropdown;

})(typeof window !== 'undefined' ? window : this);
