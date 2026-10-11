'use strict';

function when(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  const now = new Date();
  const time = d.toLocaleTimeString('hu-HU', { hour: '2-digit', minute: '2-digit' });
  if (d.toDateString() === now.toDateString()) return 'ma ' + time;
  return d.toLocaleDateString('hu-HU', { month: 'short', day: 'numeric' }) + ' ' + time;
}

function minutes(sec) {
  const s = Math.max(0, Math.floor(sec));
  return s >= 60 ? Math.floor(s / 60) + ' perc' : s + ' mp';
}

function web(el, w) {
  if (!w) return;
  el.textContent = w.exempt
    ? 'hétvége — nincs korlát'
    : w.blocked
    ? 'elfogyott — újra: ' + when(w.availableAgainAt)
    : minutes(w.remainingSeconds) + ' maradt';
}

function describeSchedule(schedule) {
  if (!schedule) return '';
  const reset = schedule.resetMode === 'daily'
    ? `A keretek naponta ${String(schedule.dailyResetHour).padStart(2, '0')}:00-kor nullázódnak.`
    : 'Minden keret az előző 24 órára vonatkozik.';
  return reset + (schedule.enforceOnWeekends ? ' Hétvégén is érvényes.' : ' Hétvégén nincs korlát.');
}

async function render() {
  const r = await chrome.runtime.sendMessage({ type: 'popup' }).catch(() => null);
  const state = document.getElementById('state');
  const s = r && r.status;
  if (s) {
    const lol = s.lol;
    document.getElementById('lol').textContent = lol.exempt
      ? 'hétvége — nincs korlát'
      : lol.blocked
      ? 'elfogyott — újra: ' + when(lol.availableAgainAt)
      : lol.remaining + ' / ' + lol.limit + ' meccs maradt';
    web(document.getElementById('short'), s.shortVideo);
    web(document.getElementById('feed'), s.facebookFeed);
    document.getElementById('schedule').textContent = describeSchedule(s.schedule);
  }
  if (r && r.healthy) {
    state.className = 'state ok';
    state.textContent = 'A védelem működik.';
  } else {
    state.className = 'state bad';
    state.textContent = 'Hiba: ' + ((r && r.error) || 'nincs kapcsolat') + ' A korlát most nem érvényesül.';
  }
}

render();
setInterval(render, 1000);
