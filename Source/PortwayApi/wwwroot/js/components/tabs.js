// Tabs component - switchable tab panels
// Usage:
//   HTML:
//     <div class="tabs" data-tabs="myTabs">
//       <button class="tab-trigger active" data-tab="tab1">Tab 1</button>
//       <button class="tab-trigger" data-tab="tab2">Tab 2</button>
//     </div>
//     <div class="tab-content active" id="tab1">Content 1</div>
//     <div class="tab-content" id="tab2">Content 2</div>
//   
//   JS:
//     Tabs.init('myTabs') 
//     Tabs.switch('myTabs', 'tab2', onSwitch callback)
//     Tabs.getActive('myTabs') 
(function(global) {
  'use strict';

  const Tabs = {
    _onSwitchCallbacks: {},

    init: function(tabsId, defaultTab, onSwitch) {
      const tabsContainer = document.querySelector(`[data-tabs="${tabsId}"]`);
      if (!tabsContainer) return;

      // Use first tab if no default specified
      if (!defaultTab) {
        const firstTrigger = tabsContainer.querySelector('.tab-trigger');
        defaultTab = firstTrigger?.dataset?.tab;
      }

      // Set initial state
      if (defaultTab) {
        this.switch(tabsId, defaultTab, onSwitch, true);
      }

      // Add click handlers
      tabsContainer.querySelectorAll('.tab-trigger').forEach(trigger => {
        trigger.addEventListener('click', (e) => {
          const tab = trigger.dataset.tab;
          this.switch(tabsId, tab, onSwitch);
        });
      });
    },

    switch: function(tabsId, tabId, onSwitch, initial) {
      const tabsContainer = document.querySelector(`[data-tabs="${tabsId}"]`);
      if (!tabsContainer) return;

      // Update triggers
      tabsContainer.querySelectorAll('.tab-trigger').forEach(trigger => {
        const isActive = trigger.dataset.tab === tabId;
        trigger.classList.toggle('active', isActive);
        trigger.setAttribute('aria-selected', isActive);
      });

      // Update content panels - look for content within same parent or by ID
      const parent = tabsContainer.parentElement;
      
      // Find all tab content elements (either with matching data-tab or matching ID)
      parent?.querySelectorAll('.tab-content').forEach(content => {
        const contentTab = content.dataset.tab || content.id;
        const isActive = contentTab === tabId;
        content.classList.toggle('active', isActive);
        content.hidden = !isActive;
      });

      // Dispatch custom event
      tabsContainer.dispatchEvent(new CustomEvent('tabchange', { 
        detail: { tab: tabId },
        bubbles: true 
      }));

      // Call callback
      if (onSwitch && !initial) {
        onSwitch(tabId);
      }

      if (this._onSwitchCallbacks[tabsId]) {
        this._onSwitchCallbacks[tabsId](tabId);
      }
    },

    onSwitch: function(tabsId, callback) {
      this._onSwitchCallbacks[tabsId] = callback;
    },

    getActive: function(tabsId) {
      const tabsContainer = document.querySelector(`[data-tabs="${tabsId}"]`);
      if (!tabsContainer) return null;
      
      const activeTrigger = tabsContainer.querySelector('.tab-trigger.active');
      return activeTrigger?.dataset?.tab || null;
    },

    // Create tabs HTML programmatically
    create: function(options) {
      const {
        id,
        tabs = [], // Array of { value, label, icon }
        defaultValue,
        onChange
      } = options;

      const triggersHtml = tabs.map(tab => `
        <button class="tab-trigger ${tab.value === defaultValue ? 'active' : ''}" 
                data-tab="${tab.value}" 
                aria-selected="${tab.value === defaultValue}">
          ${tab.icon || ''}
          <span>${tab.label}</span>
        </button>
      `).join('');

      const contentHtml = tabs.map(tab => `
        <div class="tab-content ${tab.value === defaultValue ? 'active' : ''}" 
             data-tab="${tab.value}"
             ${tab.value !== defaultValue ? 'hidden' : ''}>
          ${tab.content || ''}
        </div>
      `).join('');

      return {
        triggers: `<div class="tabs" data-tabs="${id}">${triggersHtml}</div>`,
        content: contentHtml
      };
    }
  };

  // Export
  global.Tabs = Tabs;

})(typeof window !== 'undefined' ? window : this);
