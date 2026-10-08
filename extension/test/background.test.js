'use strict';
// A háttér-service worker viselkedése hamis chrome API-val és hamis idővel.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const SOURCE = fs.readFileSync(path.join(__dirname, '..', 'background.js'), 'utf8');
const NONE = -1;

function listeners() {
  const fns = [];
  return { addListener: (f) => fns.push(f), fire: (...a) => fns.map((f) => f(...a)), fns };
}

/** Hamis szolgáltatás: kategóriánként keret, a jelentéseket levonja. */
function fakeService(limits) {
  const used = { shortVideo: 0, facebookFeed: 0 };
  return {
    used,
    respond(msg) {
      if (msg.type === 'usage') used[msg.category] += msg.seconds;
      const web = (c) => ({
        limitSeconds: limits[c], usedSeconds: used[c],
        remainingSeconds: Math.max(0, limits[c] - used[c]), blocked: used[c] >= limits[c],
        availableAgainAt: '2026-10-09T10:00:00Z',
      });
      return {
        id: msg.id, ok: true,
        lol: { limit: 3, used: 0, remaining: 3, blocked: false },
        shortVideo: web('shortVideo'), facebookFeed: web('facebookFeed'),
      };
    },
  };
}

function setup({ limits = { shortVideo: 3600, facebookFeed: 3600 }, nativeFails = false } = {}) {
  let now = 1_000_000;
  const intervals = [];
  const service = fakeService(limits);
  const queue = [];
  let port = null;
  const badge = { text: '' };
  const pushed = [];

  const runtime = {
    lastError: null,
    onMessage: listeners(),
    connectNative() {
      if (nativeFails) throw new Error('Specified native messaging host not found.');
      port = {
        onMessage: listeners(),
        onDisconnect: listeners(),
        postMessage: (m) => queue.push(m),
      };
      return port;
    },
  };
  const focus = listeners();
  const chrome = {
    runtime,
    windows: { WINDOW_ID_NONE: NONE, onFocusChanged: focus, getLastFocused: () => Promise.resolve({ id: 1, focused: true }) },
    tabs: { onRemoved: listeners(), sendMessage: (id, m) => { pushed.push({ id, ...m }); return Promise.resolve(); } },
    idle: { setDetectionInterval() {}, onStateChanged: listeners(), queryState: () => Promise.resolve('active') },
    action: { setBadgeText: (o) => { badge.text = o.text; }, setBadgeBackgroundColor() {}, setTitle() {} },
    alarms: { create() {}, onAlarm: listeners() },
  };

  const ctx = {
    chrome, console,
    Date: { now: () => now },
    setInterval: (f) => intervals.push(f),
    setTimeout: () => 0,
    clearTimeout: () => {},
  };
  vm.createContext(ctx);
  vm.runInContext(SOURCE, ctx);

  const env = {
    service, badge, pushed, chrome,
    /** Szolgáltatói válaszok kézbesítése (késleltetés modellezése). */
    flush() {
      while (queue.length) port.onMessage.fire(service.respond(queue.shift()));
    },
    tick(ms = 1000) {
      now += ms;
      intervals.forEach((f) => f());
    },
    hb(tabId, { category = 'shortVideo', visible = true, playing = true, active = true, windowId = 1 } = {}) {
      let resp;
      runtime.onMessage.fire({ type: 'hb', category, visible, playing }, { tab: { id: tabId, active, windowId } }, (r) => { resp = r; });
      return resp;
    },
    focus: (id) => focus.fire(id),
    lock: (state) => chrome.idle.onStateChanged.fire(state),
    disconnect() {
      runtime.lastError = { message: 'Native host has exited.' };
      port.onDisconnect.fire();
      runtime.lastError = null;
    },
  };
  env.focus(1);
  env.flush();
  return env;
}

/** n másodperc nézés: minden másodpercben szívverés + tick + válasz. */
function watch(env, seconds, hbOpts, tabId = 10) {
  for (let i = 0; i < seconds; i++) {
    env.hb(tabId, hbOpts);
    env.tick();
    env.flush();
  }
}

test('A fókuszban lévő ablak aktív, látható lapja másodpercenként fogyaszt', () => {
  const env = setup();
  watch(env, 10);
  assert.ok(Math.abs(env.service.used.shortVideo - 10) < 0.01, String(env.service.used.shortVideo));
  assert.equal(env.service.used.facebookFeed, 0);
});

test('Videónézés egérmozgatás nélkül is számít (csak a zárolás állítja meg)', () => {
  const env = setup();
  env.lock('idle');
  watch(env, 5);
  assert.ok(env.service.used.shortVideo >= 4.99);
  env.lock('locked');
  watch(env, 5);
  assert.ok(env.service.used.shortVideo < 5.01);
});

test('Háttérlap és nem aktív lap nem fogyaszt', () => {
  const env = setup();
  watch(env, 5, { visible: false });
  watch(env, 5, { active: false });
  assert.equal(env.service.used.shortVideo, 0);
});

test('Másik alkalmazás fókuszban: csak a lejátszó, utoljára fókuszált ablak számít', () => {
  const env = setup();
  env.focus(NONE);
  watch(env, 3, { playing: false });
  assert.equal(env.service.used.shortVideo, 0);
  watch(env, 3, { playing: true });
  assert.ok(env.service.used.shortVideo >= 2.99);
  // Nem fókuszált, másik ablak lapja nem számít.
  env.focus(2);
  watch(env, 3, { windowId: 1 });
  assert.ok(env.service.used.shortVideo < 3.01);
});

test('Alvás után egy lépés legfeljebb 1,5 másodpercet számol', () => {
  const env = setup();
  env.hb(10);
  env.tick(8 * 3600 * 1000);
  env.flush();
  assert.ok(env.service.used.shortVideo <= 1.5);
});

test('Facebook-hírfolyam külön keretet fogyaszt', () => {
  const env = setup();
  watch(env, 4, { category: 'facebookFeed' });
  assert.equal(env.service.used.shortVideo, 0);
  assert.ok(env.service.used.facebookFeed >= 3.99);
});

test('A keret elérésekor egy másodpercen belül tilt, és szól a lapnak', () => {
  const env = setup({ limits: { shortVideo: 5, facebookFeed: 3600 } });
  watch(env, 4);
  assert.equal(env.hb(10).blocked, false);
  // Az ötödik másodperc: a válasz még úton van, de a függő jelentést levonva már tilt.
  env.tick();
  assert.equal(env.hb(10).blocked, true);
  env.flush();
  const r = env.hb(10);
  assert.equal(r.blocked, true);
  assert.equal(r.availableAgainAt, '2026-10-09T10:00:00Z');
  assert.ok(env.pushed.some((m) => m.id === 10 && m.blocked));
  // Tiltás alatt nem jelent több időt.
  const before = env.service.used.shortVideo;
  watch(env, 3);
  assert.equal(env.service.used.shortVideo, before);
  // A Facebook-hírfolyam ettől még szabad.
  assert.equal(env.hb(11, { category: 'facebookFeed' }).blocked, false);
});

test('Szolgáltatáshiba esetén enged (fail-open) és piros jelvényt mutat', () => {
  const env = setup({ limits: { shortVideo: 1, facebookFeed: 3600 } });
  watch(env, 2);
  assert.equal(env.hb(10).blocked, true);
  assert.equal(env.badge.text, '');
  env.disconnect();
  assert.equal(env.hb(10).blocked, false);
  assert.equal(env.badge.text, '!');
});

test('Hiányzó native host: enged és hibát jelez', () => {
  const env = setup({ nativeFails: true });
  assert.equal(env.hb(10).blocked, false);
  assert.equal(env.badge.text, '!');
});
