#!/usr/bin/env node
// Notification concierge: what reaches the phone is explained per kind and per mode; a kind can be kept in the app,
// approvals and failures cannot; the current mode shows up in the explanation; nothing is filtered from the in-app list.
import assert from 'node:assert/strict';

const api = `${process.env.JARVIS_BASE_URL ?? 'http://localhost:5082'}/api/v1`;

async function request(method, path, body) {
  const response = await fetch(`${api}${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  let data;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = text;
  }
  return { status: response.status, data, text };
}

const kind = (view, id) => view.categories.find((c) => c.id === id);

console.log('Reading preferences...');
await request('PUT', '/modes/active', { mode: 'normal', minutes: 30 });
const first = await request('GET', '/notifications/preferences');
assert.equal(first.status, 200, first.text);
assert.deepEqual(first.data.categories.map((c) => c.id), ['approvals', 'reminders', 'problems', 'briefings', 'updates']);
assert.equal(kind(first.data, 'approvals').locked, true);
assert.ok(kind(first.data, 'reminders').modes.length >= 7, 'every mode is explained');

console.log('Keeping a kind in the app...');
const quiet = await request('PUT', '/notifications/preferences/updates', { push: 'quiet' });
assert.equal(quiet.status, 200, quiet.text);
assert.equal(kind(quiet.data, 'updates').setting, 'quiet');
assert.equal(kind(quiet.data, 'updates').now.outcome, 'app_only');
assert.equal(kind(quiet.data, 'reminders').now.outcome, 'push');

console.log('Locked kinds and bad input are refused...');
assert.equal((await request('PUT', '/notifications/preferences/approvals', { push: 'quiet' })).status, 400);
assert.equal((await request('PUT', '/notifications/preferences/problems', { push: 'quiet' })).status, 400);
assert.equal((await request('PUT', '/notifications/preferences/gossip', { push: 'on' })).status, 400);
assert.equal((await request('PUT', '/notifications/preferences/updates', { push: 'digest' })).status, 400);

console.log('A mode changes the explanation...');
const sleep = await request('PUT', '/modes/active', { mode: 'sleep', minutes: 30 });
assert.equal(sleep.status, 200, sleep.text);
const held = await request('GET', '/notifications/preferences');
assert.equal(held.data.currentMode, 'sleep');
assert.equal(kind(held.data, 'approvals').now.outcome, 'held');
assert.equal(kind(held.data, 'updates').now.outcome, 'app_only', 'the owner choice comes first');
assert.ok(held.data.summary.startsWith('Sleep is on'), held.data.summary);

console.log('Nothing is hidden from the app...');
const list = await request('GET', '/notifications');
assert.equal(list.status, 200);
assert.ok(Array.isArray(list.data));

console.log('Cleaning up...');
await request('PUT', '/modes/active', { mode: 'auto' });
await request('PUT', '/notifications/preferences/updates', { push: 'on' });
const done = await request('GET', '/notifications/preferences');
assert.equal(kind(done.data, 'updates').setting, 'on');
console.log('notification_preferences_flow ok');
