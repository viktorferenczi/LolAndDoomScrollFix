// Háttér (service worker): időmérés és kapcsolat a korlátozó szolgáltatással Native Messagingen keresztül.
// A számlálók a szolgáltatásban vannak; a bővítmény csak használatot jelent és tiltási állapotot kap vissza.
// Hiba esetén (nincs kapcsolat / elavult állapot) enged tovább, és hibát jelez (piros „!” jelvény).
'use strict';

const HOST_NAME = 'hu.kmsoft.lolscrolllimiter';
const CATEGORIES = ['shortVideo', 'facebookFeed'];
const TICK_MS = 1000;
const MAX_TICK_SECONDS = 1.5;     // alvás/felfüggesztés után sem számol többet egy lépésben
const HEARTBEAT_FRESH_MS = 3000;  // ennél régebbi tartalomszkript-jelentés nem számít
const STATUS_FRESH_MS = 10000;    // ennél régebbi szolgáltatói állapotnál fail-open
const PING_MS = 5000;        // ennél sűrűbb, mint STATUS_FRESH_MS, így tétlenül sem avul el az állapot

let port = null;
let reconnectDelay = 1000;
let reconnectTimer = null;
let lastStatus = null;
let lastStatusAt = 0;
let lastError = 'Kapcsolódás a korlátozó szolgáltatáshoz…';
let nextId = 1;
const inflight = new Map(); // id -> { category, seconds }

const tabs = new Map(); // tabId -> { category, visible, playing, windowId, at }
let focusedWindowId = chrome.windows.WINDOW_ID_NONE;
let lastFocusedWindowId = null;
let idleState = 'active';
let lastTick = Date.now();
let lastPing = 0;
const lastBlocked = { shortVideo: false, facebookFeed: false };

// ---------- Native Messaging ----------

function connect() {
  clearTimeout(reconnectTimer);
  reconnectTimer = null;
  try {
    port = chrome.runtime.connectNative(HOST_NAME);
  } catch (e) {
    onDisconnected(String(e && e.message || e));
    return;
  }
  port.onMessage.addListener(onNativeMessage);
  port.onDisconnect.addListener(() => {
    const msg = chrome.runtime.lastError && chrome.runtime.lastError.message;
    onDisconnected(msg || 'A kapcsolat megszakadt.');
  });
  send({ type: 'status' });
}

function onDisconnected(message) {
  port = null;
  inflight.clear();
  lastStatusAt = 0;
  lastError = 'A korlátozó szolgáltatás nem érhető el: ' + message;
  updateBadge();
  pushStates();
  reconnectTimer = setTimeout(connect, reconnectDelay);
  reconnectDelay = Math.min(reconnectDelay * 2, 30000);
}

function send(message) {
  if (!port) return false;
  const id = String(nextId++);
  try {
    port.postMessage({ id, ...message });
  } catch (e) {
    onDisconnected(String(e && e.message || e));
    return false;
  }
  if (message.type === 'usage') inflight.set(id, { category: message.category, seconds: message.seconds });
  return true;
}

function onNativeMessage(msg) {
  if (msg && msg.id) inflight.delete(msg.id);
  if (!msg || !msg.ok) {
    lastError = (msg && msg.error) || 'Ismeretlen hiba a szolgáltatásban.';
    lastStatusAt = 0;
  } else {
    lastStatus = msg;
    lastStatusAt = Date.now();
    lastError = null;
    reconnectDelay = 1000;
  }
  updateBadge();
  pushStates();
}

function healthy() {
  return !!(port && lastStatus && Date.now() - lastStatusAt < STATUS_FRESH_MS);
}

/** Tiltott-e a kategória. Hibás/elavult állapotnál enged (fail-open). */
function isBlocked(category) {
  if (!category || !healthy()) return false;
  const s = lastStatus[category];
  if (!s) return false;
  if (s.blocked) return true;
  // A még nyugtázatlan jelentéseket is levonjuk, így a tiltás egy másodpercen belül életbe lép.
  let pending = 0;
  for (const r of inflight.values()) if (r.category === category) pending += r.seconds;
  return s.remainingSeconds - pending <= 0;
}

function stateFor(category) {
  const s = lastStatus && lastStatus[category];
  return {
    type: 'state',
    category,
    blocked: isBlocked(category),
    availableAgainAt: s ? (s.availableAgainAt || s.nextReleaseAt || null) : null,
    error: healthy() ? null : lastError,
  };
}

// ---------- Fókusz, lapok, zárolás ----------

chrome.windows.onFocusChanged.addListener((windowId) => {
  focusedWindowId = windowId;
  if (windowId !== chrome.windows.WINDOW_ID_NONE) lastFocusedWindowId = windowId;
});
chrome.windows.getLastFocused().then((w) => {
  if (w) {
    lastFocusedWindowId = w.id;
    if (w.focused) focusedWindowId = w.id;
  }
}).catch(() => {});

chrome.tabs.onRemoved.addListener((tabId) => tabs.delete(tabId));

chrome.idle.setDetectionInterval(60);
chrome.idle.onStateChanged.addListener((state) => { idleState = state; });
chrome.idle.queryState(60).then((state) => { idleState = state; }).catch(() => {});

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg && msg.type === 'hb' && sender.tab) {
    tabs.set(sender.tab.id, {
      category: msg.category || null,
      visible: !!msg.visible,
      playing: !!msg.playing,
      active: !!sender.tab.active,
      windowId: sender.tab.windowId,
      at: Date.now(),
    });
    sendResponse(stateFor(msg.category));
    return false;
  }
  if (msg && msg.type === 'popup') {
    sendResponse({ healthy: healthy(), error: healthy() ? null : lastError, status: lastStatus });
    return false;
  }
  return false;
});

/**
 * Melyik lap fogyaszt most időt? Az aktív, látható lap a fókuszban lévő Chrome-ablakban.
 * Ha egyik Chrome-ablak sincs fókuszban (pl. másik monitoron fut a videó), az utoljára fókuszált ablak
 * látható lapja akkor számít, ha éppen lejátszik.
 */
function countingCategory(now) {
  if (idleState === 'locked') return null;
  let fallback = null;
  for (const t of tabs.values()) {
    if (!t.category || !t.visible || !t.active || now - t.at > HEARTBEAT_FRESH_MS) continue;
    if (t.windowId === focusedWindowId) return t.category;
    if (focusedWindowId === chrome.windows.WINDOW_ID_NONE && t.windowId === lastFocusedWindowId && t.playing) {
      fallback = t.category;
    }
  }
  return fallback;
}

function tick() {
  const now = Date.now();
  const seconds = Math.min((now - lastTick) / 1000, MAX_TICK_SECONDS);
  lastTick = now;

  const category = countingCategory(now);
  if (category && seconds > 0 && !isBlocked(category)) {
    send({ type: 'usage', category, seconds: Math.round(seconds * 1000) / 1000 });
  }
  if (now - lastPing > PING_MS) {
    lastPing = now;
    if (port) send({ type: 'ping' });
    else if (!reconnectTimer) connect();
  }
  pushStates();
}

/** Tiltási állapot változásakor azonnal szól az érintett lapoknak. */
function pushStates() {
  for (const category of CATEGORIES) {
    const blocked = isBlocked(category);
    if (blocked === lastBlocked[category]) continue;
    lastBlocked[category] = blocked;
    const state = stateFor(category);
    for (const [tabId, t] of tabs) {
      if (t.category === category) chrome.tabs.sendMessage(tabId, state).catch(() => {});
    }
  }
}

function updateBadge() {
  if (healthy()) {
    chrome.action.setBadgeText({ text: '' });
    chrome.action.setTitle({ title: 'LoL- és görgetéskorlátozó — a védelem működik' });
  } else {
    chrome.action.setBadgeBackgroundColor({ color: '#cf222e' });
    chrome.action.setBadgeText({ text: '!' });
    chrome.action.setTitle({ title: 'Hiba: ' + (lastError || 'a korlátozó szolgáltatás nem érhető el') + ' — a korlát most nem érvényesül.' });
  }
}

// A service workert a nyitott Native Messaging port életben tartja; ha mégis leállna,
// az alarm 30 másodpercen belül újraindítja és újracsatlakozik.
chrome.alarms.create('keepalive', { periodInMinutes: 0.5 });
chrome.alarms.onAlarm.addListener(() => { if (!port && !reconnectTimer) connect(); });

updateBadge();
connect();
setInterval(tick, TICK_MS);
