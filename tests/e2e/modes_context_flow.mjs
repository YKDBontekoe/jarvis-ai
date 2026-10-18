#!/usr/bin/env node
// Mode context card and focus sessions: the card explains what is quiet and that a mode grants nothing; focus
// is a timed manual mode with one chosen action; a second start is refused; a manual mode cannot be "corrected"
// (only a guess can); ending stores the reflection and returns to automatic; bad input is refused.
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

console.log('Starting from automatic modes...');
assert.equal((await request('PUT', '/modes/active', { mode: 'auto' })).status, 200);
const base = await request('GET', '/modes/context');
assert.equal(base.status, 200, base.text);
assert.match(base.data.effects, /never grants a permission/);
assert.ok(base.data.alwaysAvailable.some((x) => /Approvals/.test(x)), 'essential controls stay visible');

await request('POST', '/modes/focus/end', {}); // clear any session left by an earlier run

console.log('Refusals...');
assert.equal((await request('POST', '/modes/focus', { minutes: 2 })).status, 400);
assert.equal((await request('POST', '/modes/focus', { minutes: 999 })).status, 400);
assert.equal((await request('POST', '/modes/focus', { intention: 'a'.repeat(201) })).status, 400);
assert.equal((await request('POST', '/modes/focus/end', {})).status, 400, 'nothing to end');

console.log('Starting focus...');
const started = await request('POST', '/modes/focus', { intention: '  Finish the   outline ', minutes: 45 });
assert.equal(started.status, 200, started.text);
const card = started.data;
assert.equal(card.mode, 'focus');
assert.equal(card.source, 'manual');
assert.equal(card.focusRunning, true);
assert.equal(card.focus.intention, 'Finish the outline');
assert.ok(card.minutesLeft > 40 && card.minutesLeft <= 45);
assert.ok(card.stillReaches.includes('Approvals'));
assert.equal((await request('POST', '/modes/focus', { minutes: 30 })).status, 400, 'one session at a time');
assert.equal((await request('POST', '/modes/correct')).status, 400, 'a mode you chose is not a guess');

console.log('Ending with a reflection...');
const ended = await request('POST', '/modes/focus/end', { reflection: 'Outline done' });
assert.equal(ended.status, 200, ended.text);
assert.equal(ended.data.focus, null);
assert.notEqual(ended.data.source, 'manual', 'back to automatic');
const recent = await request('GET', '/modes/focus');
assert.equal(recent.data[0].reflection, 'Outline done');
assert.ok(recent.data[0].endedAt);
console.log('modes context flow OK');
