#!/usr/bin/env node
// Home layout: the account's Home is shared by the app and Jarvis. The app saves a layout, Jarvis changes it from
// chat with a reason and an event, and Undo puts it back. Needs the fake Codex server (scripts/dev/smoke.sh).
import assert from 'node:assert/strict';

const api = `${process.env.JARVIS_API_URL ?? 'http://localhost:5082'}/api/v1`;

async function request(method, path, body) {
  const response = await fetch(`${api}${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(120_000),
  });
  const text = await response.text();
  let data;
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  return { status: response.status, data, text };
}
const ids = view => view.tiles.map(t => `${t.id}:${t.size}`);

console.log('The app saves its layout; unknown tiles drop and sizes fit...');
const saved = await request('PUT', '/home/layout', {
  tiles: [{ id: 'today', size: 'wide' }, { id: 'tasks', size: 'square' }, { id: 'voice', size: 'large' },
    { id: 'teleporter', size: 'icon' }, { id: 'expenses', size: 'strip' }],
  hiddenCards: [],
});
assert.equal(saved.status, 200, saved.text);
assert.deepEqual(ids(saved.data), ['today:wide', 'tasks:square', 'voice:strip', 'finance:strip']);
assert.equal(saved.data.stored, true);
assert.equal(saved.data.changedBy, 'app');
assert.equal((await request('PUT', '/home/layout', {})).status, 400);

console.log('Jarvis moves a tile from chat, with its reason...');
const chat = (await request('POST', '/conversations', { title: 'New conversation' })).data;
const send = content => request('POST', `/conversations/${chat.id}/messages`, { content });
const reply = await send('Put my habits at the top of my home screen');
assert.equal(reply.status, 200, reply.text);
assert.match(reply.data.content, /habits is now at the top of Home/);
const changed = (await request('GET', '/home/layout')).data;
assert.deepEqual(ids(changed).slice(0, 2), ['habits:wide', 'today:wide']);
assert.equal(changed.changedBy, 'jarvis');
assert.equal(changed.note, 'habits first, as you asked');
assert.equal(changed.canUndo, true);
assert.ok(changed.revision > saved.data.revision);

console.log('The change shows in Activity as something Jarvis did...');
const events = (await request('GET', '/events?limit=20')).data;
const list = Array.isArray(events) ? events : events.items ?? events.events ?? [];
assert.ok(list.some(e => e.kind === 'home.layout_changed' && /habits first/.test(e.summary)), JSON.stringify(list).slice(0, 400));

console.log('Jarvis hides a card...');
assert.match((await send('Please hide the morning brief on my home screen')).data.content, /no longer shows/);
assert.deepEqual((await request('GET', '/home/layout')).data.hiddenCards, ['brief']);

console.log('Undo puts back the step before, twice...');
const undone = await request('POST', '/home/layout/undo');
assert.equal(undone.status, 200, undone.text);
assert.deepEqual(undone.data.hiddenCards, []);
assert.equal(undone.data.changedBy, 'app');
const undone2 = await request('POST', '/home/layout/undo');
assert.deepEqual(undone2.data.hiddenCards, ['brief'], 'undoing an undo redoes it');

console.log('Reset...');
const reset = await request('POST', '/home/layout/reset');
assert.equal(reset.status, 200, reset.text);
assert.deepEqual(reset.data.hiddenCards, []);
assert.equal(reset.data.tiles[0].id, 'today');
console.log('home_layout_flow ok');
