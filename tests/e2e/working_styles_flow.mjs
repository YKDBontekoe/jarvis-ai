#!/usr/bin/env node
// Working styles: presets list what they cover; creating one never makes it the default and never duplicates;
// a suggestion explains why, compares scope and never switches or creates anything by itself; a profile's scope
// summary lists its limits.
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

console.log('Presets...');
const presets = await request('GET', '/profiles/presets');
assert.equal(presets.status, 200, presets.text);
assert.deepEqual(presets.data.map((p) => p.preset.id), ['planning', 'research', 'writing', 'tutor', 'operator']);
const research = presets.data.find((p) => p.preset.id === 'research');
assert.equal(research.preset.needsSources, true);
assert.ok(research.scope.limits.includes('Cannot save new memories.'));

console.log('Suggesting changes nothing...');
const before = (await request('GET', '/profiles')).data.length;
const suggestion = await request('POST', '/profiles/suggest', { text: 'Please rewrite this email draft' });
assert.equal(suggestion.status, 200, suggestion.text);
assert.ok(['preset', 'existing'].includes(suggestion.data.kind));
assert.match(suggestion.data.reason, /rewrite/);
assert.equal((await request('GET', '/profiles')).data.length, before, 'a suggestion creates nothing');
assert.equal((await request('POST', '/profiles/suggest', { text: 'what is the weather' })).data.kind, 'none');

console.log('Creating a style...');
const existing = presets.data.find((p) => p.preset.id === 'tutor').existingProfileId;
if (!existing) {
  const created = await request('POST', '/profiles/presets/tutor/create');
  assert.equal(created.status, 201, created.text);
  assert.equal(created.data.isDefault, false, 'a style is never made the default');
}
assert.equal((await request('POST', '/profiles/presets/tutor/create')).status, 400, 'no duplicates');
assert.equal((await request('POST', '/profiles/presets/nothing/create')).status, 404);

console.log('Scope summary...');
const profiles = (await request('GET', '/profiles')).data;
const tutor = profiles.find((p) => p.name === 'Tutor');
const scope = await request('GET', `/profiles/${tutor.id}/scope`);
assert.equal(scope.status, 200, scope.text);
assert.match(scope.data.snapshotNote, /keep the version they began with/);
assert.equal((await request('GET', '/profiles/00000000-0000-0000-0000-000000000000/scope')).status, 404);
console.log('working styles flow OK');
