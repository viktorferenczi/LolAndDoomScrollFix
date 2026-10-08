// Tartalomszkript: másodpercenként jelenti a háttérnek az oldal kategóriáját és láthatóságát,
// tiltásnál leállítja a videót és blokkoló felületet tesz az oldalra.
(() => {
  'use strict';
  const D = globalThis.LimiterDetect;
  if (!D || globalThis.__limiterContentLoaded) return;
  globalThis.__limiterContentLoaded = true;

  const TEXTS = {
    shortVideo: {
      title: 'Elfogyott a rövidvideós keret',
      body: 'Az Instagram Reels, a TikTok, a YouTube Shorts és a Facebook Reels együtt legfeljebb 60 percet használható 24 óra alatt.',
    },
    facebookFeed: {
      title: 'Elfogyott a Facebook-hírfolyam kerete',
      body: 'A kezdőlapi hírfolyam legfeljebb 60 percet használható 24 óra alatt. A Messenger, a profilok és a csoportok továbbra is elérhetők.',
    },
  };

  let category = null;
  let blockedState = null; // { category, availableAgainAt, limitMinutes }
  let host = null;         // az overlay gyökéreleme
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
      // A bővítmény frissült/újratöltődött: ez a példány már nem kap választ.
      if (String(e && e.message).includes('Extension context invalidated')) {
        alive = false;
        release();
        return;
      }
    }
    apply(response);
  }

  function apply(state) {
    if (state && state.blocked && category && state.category === category) {
      blockedState = state;
      enforce();
    } else {
      blockedState = null;
      release();
    }
  }

  // ---- Tiltás ----

  function pauseAll() {
    for (const m of document.querySelectorAll('video, audio')) {
      try {
        m.muted = true;
        if (!m.paused) m.pause();
      } catch (_) { /* nem kritikus */ }
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
    const time = d.toLocaleTimeString('hu-HU', { hour: '2-digit', minute: '2-digit' });
    const sameDay = d.toDateString() === now.toDateString();
    const tomorrow = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1).toDateString() === d.toDateString();
    if (sameDay) return 'ma ' + time;
    if (tomorrow) return 'holnap ' + time;
    return d.toLocaleDateString('hu-HU', { month: 'short', day: 'numeric' }) + ' ' + time;
  }

  function buildOverlay() {
    const el = document.createElement('limiter-block');
    el.style.cssText = 'all: initial; position: fixed; inset: 0; z-index: 2147483647; display: block;';
    const shadow = el.attachShadow({ mode: 'closed' });
    const texts = TEXTS[blockedState.category] || TEXTS.shortVideo;
    const when = formatTime(blockedState.availableAgainAt);
    const fbLinks = blockedState.category === 'facebookFeed'
      ? `<p class="links"><a href="https://www.facebook.com/messages/">Messenger</a>
           <a href="https://www.facebook.com/me/">Profilom</a>
           <a href="https://www.facebook.com/groups/">Csoportok</a></p>`
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
          <h1 id="t">${texts.title}</h1>
          <p>${texts.body}</p>
          ${when ? `<p class="when">Legközelebb ekkortól szabadul fel idő: ${when}</p>` : ''}
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

  // Ha az oldal eltávolítja az overlayt, azonnal visszatesszük.
  new MutationObserver(() => {
    if (blockedState && (!host || !host.isConnected)) enforce();
  }).observe(document, { childList: true, subtree: true });

  // ---- Események ----

  chrome.runtime.onMessage.addListener((msg) => {
    if (msg && msg.type === 'state') {
      category = currentCategory();
      apply(msg);
    }
  });

  // Oldalújratöltés nélküli navigáció (SPA): az URL változását gyorsan észleljük.
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
