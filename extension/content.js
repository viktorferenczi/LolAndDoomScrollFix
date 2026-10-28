// Content script: reports the page's category and visibility to the background every second;
// when blocked, it stops the video and puts a blocking overlay on the page.
(() => {
  'use strict';
  const D = globalThis.LimiterDetect;
  if (!D || globalThis.__limiterContentLoaded) return;
  globalThis.__limiterContentLoaded = true;

  // Texts come from _locales/<language>/messages.json (the browser's UI language decides; English is the default).
  const t = (key, ...subs) => chrome.i18n.getMessage(key, subs.map(String)) || key;
  const LANG = chrome.i18n.getUILanguage();

  const TEXTS = {
    shortVideo: {
      title: () => t('limitShortTitle'),
      body: (limit) => t('limitShortBody', limit),
    },
    facebookFeed: {
      title: () => t('limitFeedTitle'),
      body: (limit) => t('limitFeedBody', limit),
    },
  };

  const BANNED_TEXTS = {
    shortVideo: () => t('bannedWhatShort'),
    facebookFeed: () => t('bannedWhatFeed'),
  };

  function hourRange(ban) {
    if (!ban) return '';
    const two = (h) => String(h).padStart(2, '0') + ':00';
    return t('bannedRange', two(ban.from), two(ban.to));
  }

  /** E.g. "60 minutes in 24 hours" or "60 minutes per day (reset at 03:00)". */
  function limitText(state) {
    const minutes = Number.isFinite(state.limitMinutes) ? state.limitMinutes : 60;
    if (state.resetMode === 'daily') {
      const hour = String(state.dailyResetHour ?? 0).padStart(2, '0');
      return t('limitPerDay', minutes, hour);
    }
    return t('limitRolling', minutes);
  }

  let category = null;
  let blockedState = null; // { category, availableAgainAt, limitMinutes }
  let host = null;         // the overlay's root element
  let lastHref = location.href;
  let alive = true;

  function playing() {
    for (const v of document.querySelectorAll('video')) {
      if (!v.paused && !v.ended && v.readyState > 2) return true;
    }
    return false;
  }

  function currentCategory() {
    try {
      return D.classify(location.hostname, location.pathname, document);
    } catch (_) {
      return D.classifyUrl(location.hostname, location.pathname);
    }
  }

  async function heartbeat() {
    if (!alive) return;
    category = currentCategory();
    let response = null;
    try {
      response = await chrome.runtime.sendMessage({
        type: 'hb',
        category,
        visible: document.visibilityState === 'visible',
        playing: category ? playing() : false,
      });
    } catch (e) {
      // The extension was updated/reloaded: this instance no longer gets a response.
      if (String(e && e.message).includes('Extension context invalidated')) {
        alive = false;
        release();
        return;
      }
    }
    apply(response);
  }

  let overlayKey = null; // the key of the displayed text; if it changes (e.g. ban → used-up limit), we redraw

  function apply(state) {
    if (state && state.blocked && category && state.category === category) {
      blockedState = state;
      const key = [state.category, !!state.banned, state.availableAgainAt].join('|');
      if (host && key !== overlayKey) release();
      overlayKey = key;
      enforce();
    } else {
      blockedState = null;
      release();
    }
  }

  // ---- Blocking ----

  function pauseAll() {
    for (const m of document.querySelectorAll('video, audio')) {
      try {
        m.muted = true;
        if (!m.paused) m.pause();
      } catch (_) { /* not critical */ }
    }
  }

  function onPlay(e) {
    if (!blockedState) return;
    const t = e.target;
    if (t && typeof t.pause === 'function') {
      t.muted = true;
      t.pause();
    }
  }
  document.addEventListener('play', onPlay, true);
  document.addEventListener('playing', onPlay, true);

  function formatTime(iso) {
    if (!iso) return null;
    const d = new Date(iso);
    if (isNaN(d)) return null;
    const now = new Date();
    const time = d.toLocaleTimeString(LANG, { hour: '2-digit', minute: '2-digit' });
    const sameDay = d.toDateString() === now.toDateString();
    const tomorrow = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1).toDateString() === d.toDateString();
    if (sameDay) return t('whenToday', time);
    if (tomorrow) return t('whenTomorrow', time);
    return d.toLocaleDateString(LANG, { month: 'short', day: 'numeric' }) + ' ' + time;
  }

  function buildOverlay() {
    const el = document.createElement('limiter-block');
    el.style.cssText = 'all: initial; position: fixed; inset: 0; z-index: 2147483647; display: block;';
    const shadow = el.attachShadow({ mode: 'closed' });
    const texts = TEXTS[blockedState.category] || TEXTS.shortVideo;
    const when = formatTime(blockedState.availableAgainAt);
    const banned = !!blockedState.banned;
    const title = banned ? t('bannedTitle') : texts.title();
    const body = banned
      ? t('bannedBody', (BANNED_TEXTS[blockedState.category] || BANNED_TEXTS.shortVideo)(), hourRange(blockedState.bannedHours))
      : texts.body(limitText(blockedState));
    const fbLinks = blockedState.category === 'facebookFeed'
      ? `<p class="links"><a href="https://www.facebook.com/messages/">${t('linkMessenger')}</a>
           <a href="https://www.facebook.com/me/">${t('linkProfile')}</a>
           <a href="https://www.facebook.com/groups/">${t('linkGroups')}</a></p>`
      : '';
    shadow.innerHTML = `
      <style>
        :host { all: initial; }
        .wrap { position: fixed; inset: 0; display: flex; align-items: center; justify-content: center;
                background: rgba(18, 18, 22, 0.97); color: #f2f2f2; font: 16px/1.5 "Segoe UI", system-ui, sans-serif; }
        .card { max-width: 520px; padding: 32px; text-align: center; }
        h1 { font-size: 24px; margin: 0 0 12px; font-weight: 600; }
        p { margin: 8px 0; color: #c9c9cf; }
        .when { color: #fff; font-weight: 600; margin-top: 16px; }
        .links { margin-top: 20px; display: flex; gap: 16px; justify-content: center; }
        a { color: #8ab4f8; text-decoration: none; }
        a:hover { text-decoration: underline; }
      </style>
      <div class="wrap" role="alertdialog" aria-modal="true" aria-labelledby="t">
        <div class="card">
          <h1 id="t">${title}</h1>
          <p>${body}</p>
          ${when ? `<p class="when">${banned ? t('availableAgain') : t('nextRelease')}: ${when}</p>` : ''}
          ${fbLinks}
        </div>
      </div>`;
    return el;
  }

  function enforce() {
    pauseAll();
    const root = document.documentElement;
    if (!root) return;
    if (!host || !host.isConnected) {
      host = buildOverlay();
      root.appendChild(host);
    }
  }

  function release() {
    if (host) {
      host.remove();
      host = null;
    }
  }

  // If the page removes the overlay, we put it back at once.
  new MutationObserver(() => {
    if (blockedState && (!host || !host.isConnected)) enforce();
  }).observe(document, { childList: true, subtree: true });

  // ---- Events ----

  chrome.runtime.onMessage.addListener((msg) => {
    if (msg && msg.type === 'state') {
      category = currentCategory();
      apply(msg);
    }
  });

  // Navigation without a page reload (SPA): we detect the URL change quickly.
  setInterval(() => {
    if (location.href !== lastHref) {
      lastHref = location.href;
      heartbeat();
    } else if (blockedState) {
      pauseAll();
    }
  }, 250);

  document.addEventListener('visibilitychange', heartbeat);
  setInterval(heartbeat, 1000);
  heartbeat();
})();
