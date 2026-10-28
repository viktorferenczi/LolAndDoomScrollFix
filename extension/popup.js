'use strict';

// Texts come from _locales/<language>/messages.json (the browser's UI language decides; English is the default).
const t = (key, ...subs) => chrome.i18n.getMessage(key, subs.map(String)) || key;
const LANG = chrome.i18n.getUILanguage();

document.documentElement.lang = LANG;
for (const el of document.querySelectorAll('[data-i18n]')) el.textContent = t(el.dataset.i18n);

function when(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  const now = new Date();
  const time = d.toLocaleTimeString(LANG, { hour: '2-digit', minute: '2-digit' });
  if (d.toDateString() === now.toDateString()) return t('whenToday', time);
  return d.toLocaleDateString(LANG, { month: 'short', day: 'numeric' }) + ' ' + time;
}

function minutes(sec) {
  const s = Math.max(0, Math.floor(sec));
  return s >= 60 ? t('minutesUnit', Math.floor(s / 60)) : t('secondsUnit', s);
}

function web(el, w) {
  if (!w) return;
  el.textContent = w.exempt
    ? t('weekendNoLimit')
    : w.banned
    ? t('bannedAgain', when(w.availableAgainAt))
    : w.blocked
    ? t('usedUpAgain', when(w.availableAgainAt))
    : t('timeLeft', minutes(w.remainingSeconds))
      + (w.bonusSeconds > 0 ? t('bonusMin', Math.round(w.bonusSeconds / 60)) : '')
      + (w.extraSeconds > 0 ? t('extraMin', Math.round(w.extraSeconds / 60)) : '');
}

function describeSchedule(schedule) {
  if (!schedule) return '';
  const reset = schedule.resetMode === 'daily'
    ? t('scheduleDaily', String(schedule.dailyResetHour).padStart(2, '0'))
    : t('scheduleRolling');
  const bonus = schedule.shortVideoRestDayBonusMinutes > 0 || schedule.lolRestDayBonusMatches > 0
    ? t('restBonus')
    : '';
  return reset + (schedule.enforceOnWeekends ? t('weekendsEnforced') : t('weekendsFree')) + bonus;
}

async function render() {
  const r = await chrome.runtime.sendMessage({ type: 'popup' }).catch(() => null);
  const state = document.getElementById('state');
  const s = r && r.status;
  if (s) {
    const lol = s.lol;
    document.getElementById('lol').textContent = lol.exempt
      ? t('weekendNoLimit')
      : lol.banned
      ? t('bannedAgain', when(lol.availableAgainAt))
      : lol.blocked
      ? t('usedUpAgain', when(lol.availableAgainAt))
      : t('lolLeft', lol.remaining, lol.limit)
        + (lol.bonus > 0 ? t('bonusMatches', lol.bonus) : '')
        + (lol.extra > 0 ? t('extraMatches', lol.extra) : '');
    web(document.getElementById('short'), s.shortVideo);
    web(document.getElementById('feed'), s.facebookFeed);
    document.getElementById('schedule').textContent = describeSchedule(s.schedule);
  }
  if (r && r.healthy) {
    state.className = 'state ok';
    state.textContent = t('stateWorks');
  } else {
    state.className = 'state bad';
    state.textContent = t('stateError', (r && r.error) || t('noConnection'));
  }
}

chrome.extension.isAllowedIncognitoAccess().then((allowed) => {
  document.getElementById('incognito').textContent = allowed ? t('incognitoOn') : t('incognitoOff');
}).catch(() => {});

render();
setInterval(render, 1000);
