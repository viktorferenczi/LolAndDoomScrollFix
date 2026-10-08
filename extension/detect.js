// Felismerés: melyik időkeretet fogyasztja az aktuális oldal.
// Tiszta függvények (nincs chrome.* hívás), így Node alatt is tesztelhetők.
(function (root) {
  'use strict';

  const SHORT = 'shortVideo';
  const FEED = 'facebookFeed';

  // TikTok-oldalak, amelyek nem rövidvideó-nézők.
  const TIKTOK_EXCLUDED = /^\/(messages|setting|settings|upload|login|signup|legal|privacy|tiktokstudio|business|coin|wallet)(\/|$)/i;

  function hostIs(host, domain) {
    host = String(host || '').toLowerCase();
    return host === domain || host.endsWith('.' + domain);
  }

  /** URL alapján. */
  function classifyUrl(host, path) {
    path = path || '/';
    if (hostIs(host, 'youtube.com')) {
      return /^\/shorts(\/|$)/i.test(path) ? SHORT : null;
    }
    if (hostIs(host, 'instagram.com')) {
      // /reels/, /reel/<id>/, /<felhasználó>/reel/<id>/ (a profil Reels-rácsa nem lejátszó)
      return /^\/reels?(\/|$)/i.test(path) || /^\/[^/]+\/reel\/[^/]+/i.test(path) ? SHORT : null;
    }
    if (hostIs(host, 'tiktok.com')) {
      return TIKTOK_EXCLUDED.test(path) ? null : SHORT;
    }
    if (hostIs(host, 'facebook.com')) {
      if (/^\/reels?(\/|$)/i.test(path) || /^\/watch\/reels?(\/|$)/i.test(path)) return SHORT;
      if (path === '/' || path === '' || /^\/home\.php$/i.test(path)) return FEED;
      // Messenger, profilok, csoportok és minden más: nincs időkorlát.
      return null;
    }
    return null;
  }

  /** Az oldalon megjelenő lejátszó alapján (pl. felugró Reels-nézet URL-váltás nélkül). */
  function classifyDom(host, doc) {
    if (!doc || typeof doc.querySelectorAll !== 'function') return null;
    if (hostIs(host, 'facebook.com') || hostIs(host, 'instagram.com')) {
      const dialogs = doc.querySelectorAll('[role="dialog"]');
      for (const dialog of dialogs) {
        if (!dialog.querySelector('video')) continue;
        const label = (dialog.getAttribute && dialog.getAttribute('aria-label')) || '';
        if (/reel/i.test(label) || dialog.querySelector('a[href*="/reel/"], a[href*="/reels/"]')) return SHORT;
      }
    }
    // YouTube Shorts mindig /shorts/ URL-en fut; a rejtve megmaradó Shorts-elemek miatt itt nem vizsgáljuk a DOM-ot.
    return null;
  }

  /** Egy oldal kategóriája. Egy időpontban csak egy kategória lehet: a rövid videó elsőbbséget élvez. */
  function classify(host, path, doc) {
    return classifyDom(host, doc) || classifyUrl(host, path);
  }

  const api = { SHORT, FEED, classify, classifyUrl, classifyDom, hostIs };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LimiterDetect = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
