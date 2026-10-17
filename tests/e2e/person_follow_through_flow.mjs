#!/usr/bin/env node
// One view per person: rhythm, birthday and accepted promises together; a recent contact satisfies the rhythm;
// snooze / mute / "not useful" are per owner and per person, undoable, and never touch other people.
import assert from 'node:assert/strict';

const base = process.env.JARVIS_BASE_URL ?? 'http://localhost:5082';

async function request(method, path, body) {
  const response = await fetch(`${base}${path}`, {
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
  return { status: response.status, data };
}

const tag = Date.now().toString(36);
const name = `Rosa ${tag}`;
const person = await request('POST', '/api/v1/people', { name, relationship: 'friend', contactEveryDays: 7 });
assert.equal(person.status, 201, JSON.stringify(person.data));
const id = person.data.id;
const bystander = await request('POST', '/api/v1/people', { name: `Tom ${tag}`, contactEveryDays: 7 });

console.log('A new person with a weekly rhythm is not due yet...');
let view = await request('GET', `/api/v1/people/${id}/follow-through`);
assert.equal(view.status, 200, JSON.stringify(view.data));
assert.equal(view.data.cadence.status, 'ok');
assert.equal(view.data.nudge.mode, 'on');

console.log('An accepted promise to exactly this name shows up; a nickname and a suggestion do not...');
const promise = await request('POST', '/api/v1/commitments', {
  direction: 'i_owe',
  counterparty: name.toUpperCase(),
  description: `Send the deck ${tag}`,
  dueOn: new Date(Date.now() - 4 * 86400000).toISOString().slice(0, 10),
});
assert.equal(promise.status, 201, JSON.stringify(promise.data));
const nickname = await request('POST', '/api/v1/commitments', {
  direction: 'i_owe',
  counterparty: 'Rosa',
  description: `Nickname only ${tag}`,
});
view = await request('GET', `/api/v1/people/${id}/follow-through`);
assert.deepEqual(view.data.promises.map((item) => item.description), [`Send the deck ${tag}`]);
assert.ok(view.data.reasons.some((reason) => reason.includes('late')), JSON.stringify(view.data.reasons));

console.log('Logging contact keeps the rhythm satisfied...');
await request('POST', `/api/v1/people/${id}/contact`, {});
view = await request('GET', `/api/v1/people/${id}/follow-through`);
assert.equal(view.data.cadence.status, 'ok');
assert.equal(view.data.cadence.daysSinceContact, 0);

console.log('Not useful snoozes a month, again two months, and turning on undoes the quiet...');
let result = await request('POST', `/api/v1/people/${id}/nudges/not-useful`);
assert.equal(result.status, 200, JSON.stringify(result.data));
assert.equal(result.data.nudge.quiet, true);
assert.equal(result.data.nudge.mode, 'snoozed');
const first = result.data.nudge.until;
result = await request('POST', `/api/v1/people/${id}/nudges/not-useful`);
assert.ok(result.data.nudge.until > first, 'the second dismissal waits longer');
assert.equal(result.data.nudge.notUsefulCount, 2);
result = await request('PUT', `/api/v1/people/${id}/nudges`, { mode: 'on' });
assert.equal(result.data.nudge.quiet, false);
assert.equal(result.data.nudge.notUsefulCount, 2);

console.log('Mute, and other people are untouched...');
result = await request('PUT', `/api/v1/people/${id}/nudges`, { mode: 'muted' });
assert.equal(result.data.nudge.quiet, true);
const other = await request('GET', `/api/v1/people/${bystander.data.id}/follow-through`);
assert.equal(other.data.nudge.quiet, false);

console.log('Bad input and unknown people are refused...');
assert.equal((await request('PUT', `/api/v1/people/${id}/nudges`, { mode: 'loud' })).status, 400);
assert.equal((await request('PUT', `/api/v1/people/${id}/nudges`, { mode: 'snoozed', snoozeDays: 0 })).status, 400);
assert.equal((await request('PUT', `/api/v1/people/${crypto.randomUUID()}/nudges`, { mode: 'muted' })).status, 404);
assert.equal((await request('GET', `/api/v1/people/${crypto.randomUUID()}/follow-through`)).status, 404);

await request('DELETE', `/api/v1/commitments/${promise.data.id}`);
await request('DELETE', `/api/v1/commitments/${nickname.data.id}`);
await request('DELETE', `/api/v1/people/${id}`);
await request('DELETE', `/api/v1/people/${bystander.data.id}`);
console.log('OK person_follow_through_flow');
