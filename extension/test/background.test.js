'use strict';
// The behaviour of the background service worker with a fake chrome API and fake time.
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

/** A fake service: a limit per category, it deducts the reports. */
function fakeService(limits, schedule = { resetMode: 'rolling', dailyResetHour: 3, enforceOnWeekends: false, exemptNow: false }) {
  const used = { shortVideo: 0, facebookFeed: 0 };
  const service = {
    used,
    schedule,
    banned: false, // true: a banned period is active for short videos
    respond(msg) {
      if (msg.type === 'usage') used[msg.category] += msg.seconds;
      const banned = (c) => service.banned && c === 'shortVideo';
      const web = (c) => ({
        limitSeconds: limits[c], usedSeconds: used[c],
        remainingSeconds: schedule.exemptNow ? limits[c] : Math.max(0, limits[c] - used[c]),
        blocked: !schedule.exemptNow && (used[c] >= limits[c] || banned(c)),
        banned: banned(c),
        exempt: schedule.exemptNow,
        availableAgainAt: '2026-10-09T10:00:00Z',
      });
      return {
        id: msg.id, ok: true,
        lol: { limit: 3, used: 0, remaining: 3, blocked: false },
        shortVideo: web('shortVideo'), facebookFeed: web('facebookFeed'),
        schedule,
      };
    },
  };
  return service;
}

function setup({ limits = { shortVideo: 3600, facebookFeed: 3600 }, nativeFails = false, schedule } = {}) {
  let now = 1_000_000;
  const intervals = [];
  const service = fakeService(limits, schedule);
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
    /** Delivering the service's responses (modelling the delay). */
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

/** n seconds of watching: every second a heartbeat + tick + response. */
function watch(env, seconds, hbOpts, tabId = 10) {
  for (let i = 0; i < seconds; i++) {
    env.hb(tabId, hbOpts);
    env.tick();
    env.flush();
  }
}

test('The active, visible tab of the focused window uses time every second', () => {
  const env = setup();
  watch(env, 10);
  assert.ok(Math.abs(env.service.used.shortVideo - 10) < 0.01, String(env.service.used.shortVideo));
  assert.equal(env.service.used.facebookFeed, 0);
});

test('Watching a video counts without mouse movement (only locking stops it)', () => {
  const env = setup();
  env.lock('idle');
  watch(env, 5);
  assert.ok(env.service.used.shortVideo >= 4.99);
  env.lock('locked');
  watch(env, 5);
  assert.ok(env.service.used.shortVideo < 5.01);
});

test('A background tab and an inactive tab do not use time', () => {
  const env = setup();
  watch(env, 5, { visible: false });
  watch(env, 5, { active: false });
  assert.equal(env.service.used.shortVideo, 0);
});

test('Another application focused: only the playing, last focused window counts', () => {
  const env = setup();
  env.focus(NONE);
  watch(env, 3, { playing: false });
  assert.equal(env.service.used.shortVideo, 0);
  watch(env, 3, { playing: true });
  assert.ok(env.service.used.shortVideo >= 2.99);
  // A tab of another, unfocused window does not count.
  env.focus(2);
  watch(env, 3, { windowId: 1 });
  assert.ok(env.service.used.shortVideo < 3.01);
});

test('After sleep a step counts at most 1.5 seconds', () => {
  const env = setup();
  env.hb(10);
  env.tick(8 * 3600 * 1000);
  env.flush();
  assert.ok(env.service.used.shortVideo <= 1.5);
});

test('The Facebook feed uses a separate limit', () => {
  const env = setup();
  watch(env, 4, { category: 'facebookFeed' });
  assert.equal(env.service.used.shortVideo, 0);
  assert.ok(env.service.used.facebookFeed >= 3.99);
});

test('When the limit is reached it blocks within a second and tells the tab', () => {
  const env = setup({ limits: { shortVideo: 5, facebookFeed: 3600 } });
  watch(env, 4);
  assert.equal(env.hb(10).blocked, false);
  // The fifth second: the response is still on its way, but subtracting the pending report it already blocks.
  env.tick();
  assert.equal(env.hb(10).blocked, true);
  env.flush();
  const r = env.hb(10);
  assert.equal(r.blocked, true);
  assert.equal(r.availableAgainAt, '2026-10-09T10:00:00Z');
  assert.ok(env.pushed.some((m) => m.id === 10 && m.blocked));
  // While blocked it reports no more time.
  const before = env.service.used.shortVideo;
  watch(env, 3);
  assert.equal(env.service.used.shortVideo, before);
  // The Facebook feed is still free.
  assert.equal(env.hb(11, { category: 'facebookFeed' }).blocked, false);
});

test('On a service error it lets everything through (fail-open) and shows a red badge', () => {
  const env = setup({ limits: { shortVideo: 1, facebookFeed: 3600 } });
  watch(env, 2);
  assert.equal(env.hb(10).blocked, true);
  assert.equal(env.badge.text, '');
  env.disconnect();
  assert.equal(env.hb(10).blocked, false);
  assert.equal(env.badge.text, '!');
});

test('A missing native host: lets everything through and reports an error', () => {
  const env = setup({ nativeFails: true });
  assert.equal(env.hb(10).blocked, false);
  assert.equal(env.badge.text, '!');
});

test('The blocking state also passes the configured limit and reset mode to the tab', () => {
  const env = setup({
    limits: { shortVideo: 1200, facebookFeed: 3600 },
    schedule: { resetMode: 'daily', dailyResetHour: 3, enforceOnWeekends: false, exemptNow: false },
  });
  const r = env.hb(10);
  assert.equal(r.limitMinutes, 20);
  assert.equal(r.resetMode, 'daily');
  assert.equal(r.dailyResetHour, 3);
});

test('On a weekend (exempt period) it does not block, even if the limit would be used up', () => {
  const env = setup({
    limits: { shortVideo: 2, facebookFeed: 3600 },
    schedule: { resetMode: 'rolling', dailyResetHour: 0, enforceOnWeekends: false, exemptNow: true },
  });
  watch(env, 5);
  assert.equal(env.hb(10).blocked, false);
});

test('In a banned period it blocks and also passes the banned hours to the tab', () => {
  const env = setup({
    schedule: {
      resetMode: 'rolling', dailyResetHour: 0, enforceOnWeekends: false, exemptNow: false,
      shortVideoBannedHours: { enabled: true, from: 1, to: 5 },
    },
  });
  // The fake service reports a blocked state while there is still limit left.
  env.service.banned = true;
  env.hb(10);
  env.tick();
  env.flush();
  const r = env.hb(10);
  assert.equal(r.blocked, true);
  assert.equal(r.banned, true);
  assert.deepEqual({ ...r.bannedHours }, { enabled: true, from: 1, to: 5 });
  // The other category is not blocked.
  assert.equal(env.hb(11, { category: 'facebookFeed' }).blocked, false);
});
