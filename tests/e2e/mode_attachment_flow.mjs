#!/usr/bin/env node
// Mode attachments: a preferred profile and home routine per mode are suggestions only. Nothing is suggested until
// attached; the suggestion follows the current mode and says what is visual and what still needs the owner; a profile
// that would widen access is called out; bad input is refused; clearing removes it.
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

const tag = `ma${Date.now().toString(36)}`;
await request('PUT', '/modes/active', { mode: 'auto' });
await request('PUT', '/modes/focus/attachment', {});

console.log('Nothing attached, nothing suggested...');
await request('PUT', '/modes/active', { mode: 'focus', minutes: 30 });
const none = await request('GET', '/modes/suggestion');
assert.equal(none.status, 200, none.text);
assert.equal(none.data.profile, null);
assert.equal(none.data.routine, null);

console.log('Attaching a profile and routine...');
const profile = await request('POST', '/profiles', { name: `Focus ${tag}`, memoryScope: 'pinned', allowRemember: false });
assert.equal(profile.status, 201, profile.text);
const attached = await request('PUT', '/modes/focus/attachment', { profileId: profile.data.id, routineId: 'focus' });
assert.equal(attached.status, 200, attached.text);
assert.equal(attached.data.profile.name, `Focus ${tag}`);
const suggestion = await request('GET', '/modes/suggestion');
assert.equal(suggestion.data.mode, 'focus');
assert.equal(suggestion.data.profile.name, `Focus ${tag}`);
assert.equal(suggestion.data.routine.title, 'Start focus');
assert.ok(suggestion.data.needsYou.some((x) => /security controls always need your approval/.test(x)));
assert.ok(suggestion.data.visualOnly.length > 0);

console.log('The suggestion is for this mode only...');
await request('PUT', '/modes/active', { mode: 'meeting', minutes: 30 });
assert.equal((await request('GET', '/modes/suggestion')).data.profile, null);
const list = await request('GET', '/modes/attachments');
assert.equal(list.data.filter((a) => a.mode === 'focus').length, 1);

console.log('Refusals and clearing...');
assert.equal((await request('PUT', '/modes/nope/attachment', {})).status, 400);
assert.equal((await request('PUT', '/modes/focus/attachment', { routineId: 'bedtime' })).status, 400);
assert.equal((await request('PUT', '/modes/focus/attachment', { profileId: '00000000-0000-0000-0000-000000000000' })).status, 400);
await request('PUT', '/modes/focus/attachment', {});
assert.equal((await request('GET', '/modes/attachments')).data.filter((a) => a.mode === 'focus').length, 0);
await request('PUT', '/modes/active', { mode: 'auto' });
await request('DELETE', `/profiles/${profile.data.id}`);
console.log('mode attachment flow OK');
