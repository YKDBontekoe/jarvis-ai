#!/usr/bin/env node
// Home time zone: an admitted default, a preview that explains what moves, a chosen zone, refusal of unknown zones,
// and nothing already scheduled changes when the home zone changes.
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

await request('PUT', '/preferences/home-zone', { timeZoneId: null });
const before = await request('GET', '/preferences');
assert.equal(before.status, 200, before.text);
assert.equal(before.data.chosen, false);
assert.ok(['default', 'brief'].includes(before.data.source), before.data.source);

console.log('A reminder with its own zone...');
const due = new Date(Date.now() + 3 * 86400000).toISOString();
const reminder = await request('POST', '/reminders', { title: `Zone check ${Date.now().toString(36)}`, dueAt: due });
assert.ok([200, 201].includes(reminder.status), reminder.text);
const reminderId = reminder.data.id;
const reminderBefore = await request('GET', `/reminders/${reminderId}`);

console.log('Preview, then choose...');
const preview = await request('POST', '/preferences/home-zone/preview', { timeZoneId: 'Asia/Tokyo' });
assert.equal(preview.status, 200, preview.text);
assert.equal(preview.data.proposed, 'Asia/Tokyo');
assert.ok(preview.data.effects.some((e) => e.text.includes('Nothing already scheduled moves')));
assert.equal((await request('GET', '/preferences')).data.chosen, false, 'previewing changes nothing');
assert.equal((await request('POST', '/preferences/home-zone/preview', { timeZoneId: 'Mars/Olympus' })).status, 400);
assert.equal((await request('PUT', '/preferences/home-zone', { timeZoneId: 'Mars/Olympus' })).status, 400);

const chosen = await request('PUT', '/preferences/home-zone', { timeZoneId: 'Asia/Tokyo' });
assert.equal(chosen.status, 200, chosen.text);
assert.equal(chosen.data.homeTimeZone, 'Asia/Tokyo');
assert.equal(chosen.data.chosen, true);
assert.equal(chosen.data.source, 'chosen');

console.log('Nothing already scheduled moved...');
const reminderAfter = await request('GET', `/reminders/${reminderId}`);
assert.equal(reminderAfter.data.dueAt, reminderBefore.data.dueAt);
assert.equal(reminderAfter.data.timeZoneId, reminderBefore.data.timeZoneId);

console.log('Going back to automatic...');
const cleared = await request('PUT', '/preferences/home-zone', { timeZoneId: '' });
assert.equal(cleared.data.chosen, false);
await request('DELETE', `/reminders/${reminderId}`);
console.log('home_zone_flow ok');
