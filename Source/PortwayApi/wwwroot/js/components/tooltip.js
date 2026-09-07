(function (global) {
  'use strict';

  const SHOW_DELAY = 300;

  const Tooltip = {
    _current: null,
    _timer: null,

    show: function (trigger, content, options) {
      this.hide();
      if (!content) return;

      const side = (options && options.side) || trigger.dataset.tooltipSide || 'top';
      const tip = document.createElement('div');
      tip.className = 'tooltip';
      tip.setAttribute('role', 'tooltip');
      tip.innerHTML = content;
      document.body.appendChild(tip);
      this._current = tip;
      this._position(trigger, tip, side);
    },

    hide: function () {
      if (this._current) {
        this._current.remove();
        this._current = null;
      }
      clearTimeout(this._timer);
      this._timer = null;
    },

    _position: function (trigger, tip, side) {
      const t = trigger.getBoundingClientRect();
      const r = tip.getBoundingClientRect();
      const gap = 6;
      let top, left;

      switch (side) {
        case 'bottom':
          top = t.bottom + gap + window.scrollY;
          left = t.left + (t.width - r.width) / 2 + window.scrollX;
          break;
        case 'left':
          top = t.top + (t.height - r.height) / 2 + window.scrollY;
          left = t.left - r.width - gap + window.scrollX;
          break;
        case 'right':
          top = t.top + (t.height - r.height) / 2 + window.scrollY;
          left = t.right + gap + window.scrollX;
          break;
        default:
          top = t.top - r.height - gap + window.scrollY;
          left = t.left + (t.width - r.width) / 2 + window.scrollX;
      }

      const pad = 8;
      if (left < pad) left = pad;
      if (left + r.width > window.innerWidth - pad) left = window.innerWidth - r.width - pad;
      if (top < window.scrollY + pad) top = t.bottom + gap + window.scrollY;

      tip.style.top = `${top}px`;
      tip.style.left = `${left}px`;
    },
  };

  function triggerFrom(target) {
    return target && target.closest ? target.closest('[data-tooltip]') : null;
  }

  document.addEventListener('mouseover', (e) => {
    const trigger = triggerFrom(e.target);
    if (!trigger || trigger === Tooltip._pending) return;
    clearTimeout(Tooltip._timer);
    Tooltip._pending = trigger;
    Tooltip._timer = setTimeout(() => {
      Tooltip.show(trigger, trigger.dataset.tooltip);
    }, parseInt(trigger.dataset.tooltipDelay) || SHOW_DELAY);
  });

  document.addEventListener('mouseout', (e) => {
    const trigger = triggerFrom(e.target);
    if (!trigger) return;
    Tooltip._pending = null;
    Tooltip.hide();
  });

  document.addEventListener('focusin', (e) => {
    const trigger = triggerFrom(e.target);
    if (trigger) Tooltip.show(trigger, trigger.dataset.tooltip);
  });

  document.addEventListener('focusout', () => Tooltip.hide());
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') Tooltip.hide();
  });
  window.addEventListener('scroll', () => Tooltip.hide(), true);

  global.Tooltip = Tooltip;
})(typeof window !== 'undefined' ? window : this);
