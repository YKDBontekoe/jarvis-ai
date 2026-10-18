#!/usr/bin/env node
// Outcome learning: three "too long" answers about project updates yield one reviewable style change with a
// labelled example; nothing applies by itself; applying changes the style note and undo reverses it; a rejected
// change is never offered again; a profile kept out of learning stores nothing; bad input is refused.
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

const say = (body) => request('POST', '/outcomes/feedback', body);
const tooLong = { artifactKind: 'project_update', signal: 'revised', aspect: 'too_long' };

console.log('Two complaints are not enough...');
for (let i = 0; i < 2; i += 1) assert.equal((await say(tooLong)).data.recorded, true);
let changes = (await request('GET', '/outcomes/changes')).data.filter((c) => c.change.artifactKind === 'project_update');
assert.equal(changes.length, 0);

console.log('The third yields a reviewable change...');
await say(tooLong);
changes = (await request('GET', '/outcomes/changes')).data.filter((c) => c.change.artifactKind === 'project_update');
assert.equal(changes.length, 1);
const change = changes[0].change;
assert.equal(change.status, 'pending');
assert.equal(change.synthetic, true);
assert.match(change.reason, /3 times/);
assert.notEqual(change.before, change.after);
assert.equal((await request('GET', '/outcomes/learned')).data.waiting.length >= 1, true);

console.log('Applying and undoing...');
const applied = await request('POST', `/outcomes/changes/${change.id}/apply`);
assert.equal(applied.status, 200, applied.text);
assert.equal(applied.data.change.status, 'applied');
assert.equal((await request('POST', `/outcomes/changes/${change.id}/apply`)).status, 400, 'only once');
const undone = await request('POST', `/outcomes/changes/${change.id}/undo`);
assert.equal(undone.data.change.status, 'undone');

console.log('A closed change is not relearned...');
for (let i = 0; i < 4; i += 1) await say(tooLong);
changes = (await request('GET', '/outcomes/changes')).data.filter((c) => c.change.artifactKind === 'project_update');
assert.equal(changes.length, 0, 'an undone change is never offered again');

console.log('Rejecting a different change...');
const detailed = { artifactKind: 'story', signal: 'revised', aspect: 'too_short' };
for (let i = 0; i < 3; i += 1) await say(detailed);
const story = (await request('GET', '/outcomes/changes')).data.find((c) => c.change.artifactKind === 'story');
assert.ok(story, 'a story change is offered');
assert.equal((await request('POST', `/outcomes/changes/${story.change.id}/reject`)).data.change.status, 'rejected');
for (let i = 0; i < 3; i += 1) await say(detailed);
assert.equal((await request('GET', '/outcomes/changes')).data.some((c) => c.change.artifactKind === 'story'), false);

console.log('Suggestion answers count only what was answered...');
const before = (await request('GET', '/outcomes/learned')).data.suggestions.find((x) => x.kind === 'suggestion');
await say({ artifactKind: 'suggestion', signal: 'accepted' });
await say({ artifactKind: 'suggestion', signal: 'accepted' });
await say({ artifactKind: 'suggestion', signal: 'not_useful' });
const after = (await request('GET', '/outcomes/learned')).data.suggestions.find((x) => x.kind === 'suggestion');
assert.equal(after.rated - (before?.rated ?? 0), 3);
assert.equal(after.used - (before?.used ?? 0), 2);
assert.match(after.statement, /did not answer are not counted|too few|No answers/);

console.log('Refusals...');
assert.equal((await say({ artifactKind: 'nope', signal: 'accepted' })).status, 400);
assert.equal((await say({ artifactKind: 'trip', signal: 'silence' })).status, 400);
assert.equal((await say({ artifactKind: 'trip', signal: 'revised', aspect: 'weird' })).status, 400);
assert.equal((await say({ artifactKind: 'trip', signal: 'accepted', profileId: '00000000-0000-0000-0000-000000000000' })).status, 400);
assert.equal((await request('POST', '/outcomes/changes/unknown/apply')).status, 404);
console.log('outcome learning flow OK');
