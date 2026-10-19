#!/usr/bin/env node
// A promise becomes a to-do on today's plan: titled after the promise, planned once however often it is asked,
// placed in a free block, and finishing the to-do leaves the promise open. Anything that is not an open, accepted,
// own promise is refused.
import assert from 'node:assert/strict';

const base = process.env.JARVIS_BASE_URL ?? 'http://localhost:5082';
const zone = 'Europe/Amsterdam';
const plan = `/api/v1/planner/today`;

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
const promise = await request('POST', '/api/v1/commitments', {
  direction: 'i_owe',
  counterparty: 'Rosa',
  description: `Send the deck ${tag}`,
});
assert.equal(promise.status, 201, JSON.stringify(promise.data));
const ref = `commitment:${promise.data.id}`;

console.log('Planning the promise titles the to-do after it...');
const first = await request('POST', `${plan}/items?timeZone=${zone}`, { sourceRef: ref, minutes: 45 });
assert.equal(first.status, 200, JSON.stringify(first.data));
assert.equal(first.data.title, `Send the deck ${tag}`);
assert.equal(first.data.minutes, 45);
assert.equal(first.data.sourceRef, ref);

console.log('Asking again returns the same to-do instead of doubling it...');
const again = await request('POST', `${plan}/items?timeZone=${zone}`, { sourceRef: ref });
assert.equal(again.data.id, first.data.id);
let today = await request('GET', `${plan}?timeZone=${zone}`);
assert.equal(today.data.items.filter((item) => item.sourceRef === ref).length, 1);

console.log('Plan my day places it...');
const placed = await request('POST', `${plan}/plan?timeZone=${zone}`);
assert.equal(placed.status, 200, JSON.stringify(placed.data));

console.log('Finishing the to-do does not close the promise...');
const done = await request('PATCH', `${plan}/items/${first.data.id}?timeZone=${zone}`, { done: true });
assert.equal(done.data.done, true);
const listed = await request('GET', '/api/v1/commitments');
assert.equal(listed.data.find((item) => item.id === promise.data.id)?.status, 'open');

console.log('Refuses what is not an open promise...');
for (const bad of ['commitment:nope', `commitment:${crypto.randomUUID()}`, 'task:1']) {
  const refused = await request('POST', `${plan}/items?timeZone=${zone}`, { sourceRef: bad });
  assert.equal(refused.status, 400, `${bad}: ${JSON.stringify(refused.data)}`);
}
await request('PATCH', `/api/v1/commitments/${promise.data.id}`, { status: 'dropped' });
const closed = await request('POST', `${plan}/items?timeZone=${zone}`, { sourceRef: ref });
assert.equal(closed.status, 400);

await request('DELETE', `${plan}/items/${first.data.id}?timeZone=${zone}`);
await request('DELETE', `/api/v1/commitments/${promise.data.id}`);
console.log('OK planner_commitment_flow');
