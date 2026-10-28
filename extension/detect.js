// Detection: which time limit the current page uses.
// Pure functions (no chrome.* calls), so they can also be tested under Node.
(function (root) {
  'use strict';

  const SHORT = 'shortVideo';
  const FEED = 'facebookFeed';

  // TikTok pages that are not short-video viewers.
  const TIKTOK_EXCLUDED = /^\/(messages|setting|settings|upload|login|signup|legal|privacy|tiktokstudio|business|coin|wallet)(\/|$)/i;

  function hostIs(host, domain) {
    host = String(host || '').toLowerCase();
    return host === domain || host.endsWith('.' + domain);
  }

  /** By URL. */
  function classifyUrl(host, path) {
    path = path || '/';
    if (hostIs(host, 'youtube.com')) {
      return /^\/shorts(\/|$)/i.test(path) ? SHORT : null;
    }
    if (hostIs(host, 'instagram.com')) {
      // /reels/, /reel/<id>/, /<user>/reel/<id>/ (a profile's Reels grid is not a player)
      return /^\/reels?(\/|$)/i.test(path) || /^\/[^/]+\/reel\/[^/]+/i.test(path) ? SHORT : null;
    }
    if (hostIs(host, 'tiktok.com')) {
      return TIKTOK_EXCLUDED.test(path) ? null : SHORT;
    }
    if (hostIs(host, 'facebook.com')) {
      if (/^\/reels?(\/|$)/i.test(path) || /^\/watch\/reels?(\/|$)/i.test(path)) return SHORT;
      if (path === '/' || path === '' || /^\/home\.php$/i.test(path)) return FEED;
      // Messenger, profiles, groups and everything else: no time limit.
      return null;
    }
    return null;
  }

  /** By the player shown on the page (e.g. a pop-up Reels view without a URL change). */
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
    // YouTube Shorts always runs on a /shorts/ URL; because of Shorts elements that stay hidden we do not inspect the DOM here.
    return null;
  }

  /** A page's category. Only one category can apply at a time: short video takes precedence. */
  function classify(host, path, doc) {
    return classifyDom(host, doc) || classifyUrl(host, path);
  }

  const api = { SHORT, FEED, classify, classifyUrl, classifyDom, hostIs };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LimiterDetect = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
