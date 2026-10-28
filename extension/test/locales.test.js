'use strict';
// The English and Hungarian message catalogs must stay in sync, and the code may only use keys that exist.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.join(__dirname, '..');
const load = (lang) => JSON.parse(fs.readFileSync(path.join(ROOT, '_locales', lang, 'messages.json'), 'utf8'));
const en = load('en');
const hu = load('hu');

test('both languages define the same keys', () => {
  assert.deepEqual(Object.keys(en).sort(), Object.keys(hu).sort());
});

test('placeholders match between the languages and are all referenced in the text', () => {
  for (const [key, entry] of Object.entries(en)) {
    const other = hu[key];
    assert.deepEqual(Object.keys(entry.placeholders || {}).sort(), Object.keys(other.placeholders || {}).sort(), key);
    for (const lang of [entry, other]) {
      for (const name of Object.keys(lang.placeholders || {})) {
        assert.ok(lang.message.toLowerCase().includes(`$${name}$`), `${key}: $${name}$ is not used`);
      }
      const used = [...lang.message.matchAll(/\$([A-Za-z0-9_]+)\$/g)].map((m) => m[1].toLowerCase());
      for (const name of used) assert.ok(lang.placeholders && lang.placeholders[name], `${key}: $${name}$ is not defined`);
    }
  }
});

test('no message is empty and the English text is not a copy of the Hungarian one (except identical names)', () => {
  const same = new Set(['lolPvp', 'linkMessenger', 'extraMatches']);
  for (const [key, entry] of Object.entries(en)) {
    assert.ok(entry.message.trim().length > 0, key);
    assert.ok(hu[key].message.trim().length > 0, key);
    if (!same.has(key)) assert.notEqual(entry.message, hu[key].message, `${key} looks untranslated`);
  }
});

test('every key used by the code exists in the catalogs', () => {
  const used = new Set();
  for (const file of ['background.js', 'content.js', 'popup.js']) {
    const source = fs.readFileSync(path.join(ROOT, file), 'utf8');
    for (const m of source.matchAll(/\bt\('([A-Za-z0-9]+)'/g)) used.add(m[1]);
  }
  const html = fs.readFileSync(path.join(ROOT, 'popup.html'), 'utf8');
  for (const m of html.matchAll(/data-i18n="([A-Za-z0-9]+)"/g)) used.add(m[1]);
  assert.ok(used.size > 20);
  for (const key of used) assert.ok(en[key], `${key} is missing in the catalog`);
});

test('every catalog key is used somewhere (no dead texts), manifest keys included', () => {
  const sources = ['background.js', 'content.js', 'popup.js', 'popup.html', 'manifest.json']
    .map((f) => fs.readFileSync(path.join(ROOT, f), 'utf8')).join('\n');
  for (const key of Object.keys(en)) {
    assert.ok(sources.includes(`'${key}'`) || sources.includes(`"${key}"`) || sources.includes(`__MSG_${key}__`), `${key} is unused`);
  }
});

test('the manifest uses message keys that exist and declares a default locale', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(ROOT, 'manifest.json'), 'utf8'));
  assert.equal(manifest.default_locale, 'en');
  const text = JSON.stringify(manifest);
  for (const m of text.matchAll(/__MSG_([A-Za-z0-9]+)__/g)) assert.ok(en[m[1]], m[1]);
  assert.ok(manifest.description.length > 0 && en.extDescription.message.length <= 132);
  assert.ok(hu.extDescription.message.length <= 132);
});
