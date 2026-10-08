'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const D = require('../detect.js');

const S = D.SHORT, F = D.FEED;

test('URL-alapú felismerés', () => {
  const cases = [
    ['www.youtube.com', '/shorts/abc123', S],
    ['m.youtube.com', '/shorts/abc123', S],
    ['www.youtube.com', '/watch', null],
    ['www.youtube.com', '/', null],
    ['www.instagram.com', '/reels/', S],
    ['www.instagram.com', '/reel/Cxyz/', S],
    ['www.instagram.com', '/someone/reel/Cxyz/', S],
    ['www.instagram.com', '/someone/reels/', null],
    ['www.instagram.com', '/', null],
    ['www.instagram.com', '/direct/inbox/', null],
    ['www.tiktok.com', '/', S],
    ['www.tiktok.com', '/foryou', S],
    ['www.tiktok.com', '/@user/video/123', S],
    ['www.tiktok.com', '/messages', null],
    ['www.facebook.com', '/', F],
    ['web.facebook.com', '/home.php', F],
    ['m.facebook.com', '/', F],
    ['www.facebook.com', '/reel/123456', S],
    ['www.facebook.com', '/reels/', S],
    ['www.facebook.com', '/watch/reels/', S],
    ['www.facebook.com', '/messages/t/123', null],
    ['www.facebook.com', '/zuck', null],
    ['www.facebook.com', '/groups/123', null],
    ['www.facebook.com', '/groups/feed/', null],
    ['www.notfacebook.com', '/', null],
    ['facebook.com.evil.test', '/', null],
  ];
  for (const [host, path, expected] of cases) {
    assert.equal(D.classifyUrl(host, path), expected, `${host}${path}`);
  }
});

function fakeDoc(dialogs) {
  return {
    querySelectorAll: (sel) => (sel === '[role="dialog"]' ? dialogs : []),
  };
}
function fakeDialog({ video = true, label = '', reelLink = false }) {
  return {
    getAttribute: (n) => (n === 'aria-label' ? label : null),
    querySelector: (sel) => {
      if (sel === 'video') return video ? {} : null;
      if (sel.includes('/reel')) return reelLink ? {} : null;
      return null;
    },
  };
}

test('Felugró Reels-nézet a hírfolyamon: rövid videónak számít, nem hírfolyamnak', () => {
  const doc = fakeDoc([fakeDialog({ reelLink: true })]);
  assert.equal(D.classify('www.facebook.com', '/', doc), S);
});

test('Felugró ablak videó nélkül vagy nem Reels: marad a hírfolyam', () => {
  assert.equal(D.classify('www.facebook.com', '/', fakeDoc([fakeDialog({ video: false, reelLink: true })])), F);
  assert.equal(D.classify('www.facebook.com', '/', fakeDoc([fakeDialog({ label: 'Bejegyzés' })])), F);
});

test('Instagram felugró Reels a kezdőlapon', () => {
  assert.equal(D.classify('www.instagram.com', '/', fakeDoc([fakeDialog({ label: 'Reel' })])), S);
});

test('Messenger felugró ablak nem számít', () => {
  assert.equal(D.classify('www.facebook.com', '/messages/t/1', fakeDoc([fakeDialog({ label: 'Hívás' })])), null);
});
