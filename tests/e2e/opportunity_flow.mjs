#!/usr/bin/env node
// Opportunity radar: a late promise and a near renewal show up with the reason and a destination; feedback hides
// the exact one, "fewer" mutes the kind and can be undone, useful ones become history; reading changes nothing.
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
  return { status: response.status, data, text };
}

const tag = Date.now().toString(36);
const day = (offset) => new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);

const promise = await request('POST', '/api/v1/commitments', {
  direction: 'i_owe',
  counterparty: `Rosa ${tag}`,
  description: `Send the deck ${tag}`,
  dueOn: day(-2),
});
assert.equal(promise.status, 201, promise.text);
const owed = await request('POST', '/api/v1/commitments', {
  direction: 'owed_to_me',
  counterparty: `Anna ${tag}`,
  description: `Tile quote ${tag}`,
  dueOn: day(-5),
});

console.log('Late promises appear with the reason and a destination...');
let list = await request('GET', '/api/v1/opportunities');
assert.equal(list.status, 200, list.text);
const mine = list.data.items.find((item) => item.evidence.includes(`commitment:${promise.data.id}`));
const theirs = list.data.items.find((item) => item.evidence.includes(`commitment:${owed.data.id}`));
assert.ok(mine && theirs, JSON.stringify(list.data.items.map((i) => i.title)));
assert.equal(mine.destination, 'plan-today');
assert.ok(mine.whyNow.includes('2 days late'));
assert.equal(theirs.destination, 'inbox');
assert.ok(theirs.whyNow.includes('A draft nudge is ready.'));
assert.equal(mine.urgency, 3);

console.log('Reading again changes nothing (no commitment was created or changed)...');
const promises = await request('GET', '/api/v1/commitments');
assert.equal(promises.data.filter((item) => item.description.endsWith(tag)).length, 2);

console.log('Feedback must name something that is on the radar...');
assert.equal((await request('POST', '/api/v1/opportunities/feedback', { key: 'follow_up:invented', verdict: 'handled' })).status, 404);
assert.equal((await request('POST', '/api/v1/opportunities/feedback', { key: mine.key, verdict: 'shrug' })).status, 400);

console.log('Handled hides exactly that one; useful becomes history...');
assert.equal((await request('POST', '/api/v1/opportunities/feedback', { key: mine.key, verdict: 'handled' })).status, 204);
assert.equal((await request('POST', '/api/v1/opportunities/feedback', { key: theirs.key, verdict: 'useful' })).status, 204);
list = await request('GET', '/api/v1/opportunities');
assert.ok(!list.data.items.some((item) => item.key === mine.key || item.key === theirs.key));
assert.ok(list.data.history.some((entry) => entry.title.includes(`Tile quote ${tag}`) || entry.title.includes(`Anna ${tag}`)));

console.log('Fewer like this mutes the kind, and Bring back undoes it...');
const third = await request('POST', '/api/v1/commitments', {
  direction: 'i_owe', counterparty: `Tom ${tag}`, description: `Third ${tag}`, dueOn: day(-1),
});
list = await request('GET', '/api/v1/opportunities');
const item = list.data.items.find((entry) => entry.evidence.includes(`commitment:${third.data.id}`));
assert.ok(item);
assert.equal((await request('POST', '/api/v1/opportunities/feedback', { key: item.key, verdict: 'fewer' })).status, 204);
list = await request('GET', '/api/v1/opportunities');
assert.ok(list.data.mutedKinds.includes('follow_up'));
assert.ok(!list.data.items.some((entry) => entry.kind === 'follow_up'));
assert.equal((await request('POST', '/api/v1/opportunities/unmute', { kind: 'follow_up' })).status, 204);
assert.equal((await request('POST', '/api/v1/opportunities/unmute', { kind: 'follow_up' })).status, 404);
list = await request('GET', '/api/v1/opportunities');
assert.ok(list.data.items.some((entry) => entry.evidence.includes(`commitment:${third.data.id}`)));

for (const id of [promise.data.id, owed.data.id, third.data.id]) await request('DELETE', `/api/v1/commitments/${id}`);
console.log('OK opportunity_flow');
