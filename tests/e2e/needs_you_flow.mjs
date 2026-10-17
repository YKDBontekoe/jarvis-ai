#!/usr/bin/env node
// Needs you: an accepted promise and a decision whose review day has come show up once in the Home briefing,
// next to the existing items, and leave it when the owner finishes them. A suggested promise never shows.
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

const today = new Date().toISOString().slice(0, 10);
const tag = Date.now().toString(36);

console.log('Creating a promise due today and a decision to review today...');
const promise = await request('POST', '/api/v1/commitments', {
  direction: 'i_owe',
  counterparty: 'Rosa',
  description: `Send the deck ${tag}`,
  dueOn: today,
});
assert.equal(promise.status, 201, JSON.stringify(promise.data));
const decision = await request('POST', '/api/v1/decisions', {
  title: `Switch bank ${tag}`,
  prediction: 'Fees drop',
  probability: 0.7,
  reviewOn: today,
});
assert.equal(decision.status, 201, JSON.stringify(decision.data));

console.log('Home lists each exactly once...');
let home = await request('GET', '/api/v1/home');
assert.equal(home.status, 200);
const promises = home.data.commitments.filter((item) => item.id === promise.data.id);
const decisions = home.data.decisions.filter((item) => item.id === decision.data.id);
assert.equal(promises.length, 1, 'the promise appears once');
assert.equal(promises[0].daysOverdue, 0);
assert.equal(promises[0].direction, 'i_owe');
assert.equal(decisions.length, 1, 'the decision appears once');
assert.equal(decisions[0].prediction, 'Fees drop');

console.log('A suggested promise is not an obligation...');
// Suggestions only come from triage, so check the other half: a promise without a date is not due either.
const undated = await request('POST', '/api/v1/commitments', {
  direction: 'owed_to_me',
  counterparty: 'Tom',
  description: `Share the invoice ${tag}`,
});
assert.equal(undated.status, 201, JSON.stringify(undated.data));
home = await request('GET', '/api/v1/home');
assert.ok(!home.data.commitments.some((item) => item.id === undated.data.id), 'undated promise stays out');

console.log('Finishing them clears Home...');
const done = await request('PATCH', `/api/v1/commitments/${promise.data.id}`, { status: 'done' });
assert.equal(done.status, 200, JSON.stringify(done.data));
const resolved = await request('POST', `/api/v1/decisions/${decision.data.id}/resolve`, { outcome: true });
assert.equal(resolved.status, 200, JSON.stringify(resolved.data));
home = await request('GET', '/api/v1/home');
assert.ok(!home.data.commitments.some((item) => item.id === promise.data.id), 'fulfilled promise is gone');
assert.ok(!home.data.decisions.some((item) => item.id === decision.data.id), 'resolved decision is gone');

await request('DELETE', `/api/v1/commitments/${undated.data.id}`);
await request('DELETE', `/api/v1/commitments/${promise.data.id}`);
await request('DELETE', `/api/v1/decisions/${decision.data.id}`);
console.log('needs_you_flow: OK');
