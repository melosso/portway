// Accordion component - collapsible sections
// Usage:
//   HTML:
//     <div class="accordion" data-accordion="myAccordion">
//       <div class="accordion-item">
//         <button class="accordion-trigger" dataAccordionValue="item1">
//           <span>Section 1</span>
//           <svg class="accordion-chevron" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
//         </button>
//         <div class="accordion-content">Content 1</div>
//       </div>
//       <div class="accordion-item">
//         <button class="accordion-trigger" dataAccordionValue="item2">Section 2</button>
//         <div class="accordion-content">Content 2</div>
//       </div>
//     </div>
//   
//   JS:
//     Accordion.init() - auto-initialize
//     Accordion.open(accordionId, itemValue) - open specific item
//     Accordion.close(accordionId, itemValue) - close specific item
//     Accordion.toggle(accordionId, itemValue) - toggle item
//     Accordion.closeAll(accordionId) - close all items
(function(global) {
  'use strict';

  const Accordion = {
    init: function() {
      document.querySelectorAll('.accordion-trigger').forEach(trigger => {
        trigger.addEventListener('click', (e) => {
          e.preventDefault();
          const item = trigger.closest('.accordion-item');
          const accordion = trigger.closest('.accordion');
          
          if (item && accordion) {
            const accordionId = accordion.dataset.accordion;
            const itemValue = trigger.dataset.accordionValue || trigger.textContent.trim();
            
            // Check if accordion allows multiple open
            const allowMultiple = accordion.dataset.accordionMultiple === 'true';
            
            if (!allowMultiple) {
              this.closeAll(accordionId);
            }
            
            this.toggle(accordionId, itemValue);
          }
        });

        // Keyboard navigation
        trigger.addEventListener('keydown', (e) => {
          const accordion = trigger.closest('.accordion');
          if (!accordion) return;

          const items = Array.from(accordion.querySelectorAll('.accordion-item'));
          const currentItem = trigger.closest('.accordion-item');
          const currentIndex = items.indexOf(currentItem);

          if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            let nextIndex;
            
            if (e.key === 'ArrowDown') {
              nextIndex = (currentIndex + 1) % items.length;
            } else {
              nextIndex = (currentIndex - 1 + items.length) % items.length;
            }
            
            const nextTrigger = items[nextIndex].querySelector('.accordion-trigger');
            nextTrigger?.focus();
          } else if (e.key === 'Home') {
            e.preventDefault();
            const firstTrigger = accordion.querySelector('.accordion-item:first-child .accordion-trigger');
            firstTrigger?.focus();
          } else if (e.key === 'End') {
            e.preventDefault();
            const lastTrigger = accordion.querySelector('.accordion-item:last-child .accordion-trigger');
            lastTrigger?.focus();
          }
        });
      });
    },

    toggle: function(accordionId, itemValue) {
      const accordion = document.querySelector(`[data-accordion="${accordionId}"]`);
      if (!accordion) return;

      const trigger = Array.from(accordion.querySelectorAll('.accordion-trigger')).find(
        t => (t.dataset.accordionValue || t.textContent.trim()) === itemValue
      );
      
      if (!trigger) return;

      const item = trigger.closest('.accordion-item');
      const content = item?.querySelector('.accordion-content');
      const isOpen = item?.classList.contains('open');

      if (isOpen) {
        this._closeItem(item, content);
      } else {
        this._openItem(item, content, trigger);
      }
    },

    open: function(accordionId, itemValue) {
      const accordion = document.querySelector(`[data-accordion="${accordionId}"]`);
      if (!accordion) return;

      const trigger = Array.from(accordion.querySelectorAll('.accordion-trigger')).find(
        t => (t.dataset.accordionValue || t.textContent.trim()) === itemValue
      );

      if (!trigger) return;

      const item = trigger.closest('.accordion-item');
      const content = item?.querySelector('.accordion-content');

      if (item && content && !item.classList.contains('open')) {
        // Check if single-open accordion, close others first
        if (accordion.dataset.accordionMultiple !== 'true') {
          this.closeAll(accordionId);
        }
        this._openItem(item, content, trigger);
      }
    },

    close: function(accordionId, itemValue) {
      const accordion = document.querySelector(`[data-accordion="${accordionId}"]`);
      if (!accordion) return;

      const trigger = Array.from(accordion.querySelectorAll('.accordion-trigger')).find(
        t => (t.dataset.accordionValue || t.textContent.trim()) === itemValue
      );

      if (!trigger) return;

      const item = trigger.closest('.accordion-item');
      const content = item?.querySelector('.accordion-content');

      if (item && content && item.classList.contains('open')) {
        this._closeItem(item, content);
      }
    },

    closeAll: function(accordionId) {
      const accordion = document.querySelector(`[data-accordion="${accordionId}"]`);
      if (!accordion) return;

      accordion.querySelectorAll('.accordion-item.open').forEach(item => {
        const content = item.querySelector('.accordion-content');
        this._closeItem(item, content);
      });
    },

    _openItem: function(item, content, trigger) {
      item.classList.add('open');
      trigger?.setAttribute('aria-expanded', 'true');
      
      // Animate height
      content.style.height = 'auto';
      const height = content.offsetHeight;
      content.style.height = '0px';
      
      requestAnimationFrame(() => {
        content.style.height = height + 'px';
        content.style.transition = 'height 200ms ease-out';
      });

      // Dispatch event
      item.dispatchEvent(new CustomEvent('accordion-change', {
        detail: { open: true, value: trigger?.dataset?.accordionValue || trigger?.textContent?.trim() },
        bubbles: true
      }));
    },

    _closeItem: function(item, content) {
      const trigger = item.querySelector('.accordion-trigger');
      
      content.style.transition = 'height 200ms ease-out';
      content.style.height = content.offsetHeight + 'px';
      
      requestAnimationFrame(() => {
        content.style.height = '0px';
      });

      item.classList.remove('open');
      trigger?.setAttribute('aria-expanded', 'false');

      setTimeout(() => {
        if (!item.classList.contains('open')) {
          content.style.height = '';
          content.style.transition = '';
        }
      }, 200);

      // Dispatch event
      item.dispatchEvent(new CustomEvent('accordion-change', {
        detail: { open: false, value: trigger?.dataset?.accordionValue || trigger?.textContent?.trim() },
        bubbles: true
      }));
    },

    // Create accordion programmatically
    create: function(options) {
      const {
        id,
        items = [], // { value, title, content, defaultOpen }
        allowMultiple = false,
        onChange
      } = options;

      const itemsHtml = items.map((item, index) => `
        <div class="accordion-item ${item.defaultOpen ? 'open' : ''}">
          <button class="accordion-trigger" data-accordion-value="${item.value}" aria-expanded="${item.defaultOpen ? 'true' : 'false'}">
            <span>${item.title}</span>
            <svg class="accordion-chevron" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
          </button>
          <div class="accordion-content" ${item.defaultOpen ? '' : 'style="height: 0"'} >${item.content}</div>
        </div>
      `).join('');

      return `
        <div class="accordion" data-accordion="${id}" data-accordion-multiple="${allowMultiple}">
          ${itemsHtml}
        </div>
      `;
    }
  };

  // Auto-init
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => Accordion.init());
  } else {
    Accordion.init();
  }

  // Export
  global.Accordion = Accordion;

})(typeof window !== 'undefined' ? window : this);
