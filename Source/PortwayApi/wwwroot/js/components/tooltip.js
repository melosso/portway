// Tooltip component - hover/click activated info popup
// Usage:
//   HTML:
//     <button class="shadcn-tooltip" data-tooltip="Help text" data-tooltip-side="top">Hover me</button>
//     <button class="shadcn-tooltip" data-tooltip="<strong>HTML</strong> content">With HTML</button>
//   
//   JS:
//     ShadcnTooltip.init() - auto-initialize all shadcn-tooltip triggers
//     ShadcnTooltip.show(triggerElement, content, options)
//     ShadcnTooltip.hide()
//   
//   Options:
//     - side: 'top' | 'bottom' | 'left' | 'right' (default: 'top')
//     - delay: number (ms)
//     - arrow: boolean (default: true)
(function(global) {
  'use strict';

  const ShadcnTooltip = {
    _currentTooltip: null,
    _timeout: null,

    init: function(options = {}) {
      document.querySelectorAll('.shadcn-tooltip').forEach(trigger => {
        trigger.addEventListener('mouseenter', (e) => {
          const content = trigger.dataset.tooltip;
          const side = trigger.dataset.tooltipSide || options.side || 'top';
          const delay = parseInt(trigger.dataset.tooltipDelay) || options.delay || 300;
          
          this._timeout = setTimeout(() => {
            this.show(trigger, content, { side });
          }, delay);
        });

        trigger.addEventListener('mouseleave', () => {
          if (this._timeout) {
            clearTimeout(this._timeout);
            this._timeout = null;
          }
          this.hide();
        });

        trigger.addEventListener('focus', () => {
          const content = trigger.dataset.tooltip;
          const side = trigger.dataset.tooltipSide || options.side || 'top';
          this.show(trigger, content, { side });
        });

        trigger.addEventListener('blur', () => {
          this.hide();
        });
      });
    },

    show: function(trigger, content, options = {}) {
      this.hide();

      const side = options.side || 'top';
      const tooltip = document.createElement('div');
      tooltip.className = `tooltip tooltip-${side}`;
      tooltip.setAttribute('role', 'tooltip');
      
      // Parse HTML content
      tooltip.innerHTML = content;

      document.body.appendChild(tooltip);
      this._currentTooltip = tooltip;

      // Position
      this._position(trigger, tooltip, side);
    },

    hide: function() {
      if (this._currentTooltip) {
        this._currentTooltip.remove();
        this._currentTooltip = null;
      }
      if (this._timeout) {
        clearTimeout(this._timeout);
        this._timeout = null;
      }
    },

    _position: function(trigger, tooltip, side) {
      const triggerRect = trigger.getBoundingClientRect();
      const tooltipRect = tooltip.getBoundingClientRect();
      const gap = 6;
      
      let top, left;

      switch (side) {
        case 'top':
          top = triggerRect.top - tooltipRect.height - gap + window.scrollY;
          left = triggerRect.left + (triggerRect.width - tooltipRect.width) / 2 + window.scrollX;
          break;
        case 'bottom':
          top = triggerRect.bottom + gap + window.scrollY;
          left = triggerRect.left + (triggerRect.width - tooltipRect.width) / 2 + window.scrollX;
          break;
        case 'left':
          top = triggerRect.top + (triggerRect.height - tooltipRect.height) / 2 + window.scrollY;
          left = triggerRect.left - tooltipRect.width - gap + window.scrollX;
          break;
        case 'right':
          top = triggerRect.top + (triggerRect.height - tooltipRect.height) / 2 + window.scrollY;
          left = triggerRect.right + gap + window.scrollX;
          break;
      }

      // Keep within viewport
      const padding = 8;
      if (left < padding) left = padding;
      if (left + tooltipRect.width > window.innerWidth - padding) {
        left = window.innerWidth - tooltipRect.width - padding;
      }
      if (top < padding) top = padding;
      if (top + tooltipRect.height > window.innerHeight - padding) {
        top = window.innerHeight - tooltipRect.height - padding;
      }

      tooltip.style.top = `${top}px`;
      tooltip.style.left = `${left}px`;
    },

    // Create tooltip programmatically
    create: function(trigger, content, options = {}) {
      trigger.setAttribute('data-tooltip', content);
      if (options.side) {
        trigger.setAttribute('data-tooltip-side', options.side);
      }
      if (options.delay) {
        trigger.setAttribute('data-tooltip-delay', options.delay);
      }
    }
  };

  // Auto-init - only if there are elements with .shadcn-tooltip class
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
      if (document.querySelector('.shadcn-tooltip')) {
        ShadcnTooltip.init();
      }
    });
  } else {
    if (document.querySelector('.shadcn-tooltip')) {
      ShadcnTooltip.init();
    }
  }

  // Export
  global.ShadcnTooltip = ShadcnTooltip;

})(typeof window !== 'undefined' ? window : this);
