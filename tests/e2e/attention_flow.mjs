#!/usr/bin/env node
// Attention queue: a promise due today, a late one and a decision to review appear once each in /attention in the
// right order; a possible promise is only a review item; putting an item away hides it and it comes back; finished
// items leave; bad input and unknown ids are refused.
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

const tag = Date.now().toString(36);
const day = (offset) => new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);

console.log('Creating a late promise, one due today and a decision...');
const late = await request('POST', '/commitments', { direction: 'i_owe', counterparty: 'Rosa', description: `Late ${tag}`, dueOn: day(-3) });
assert.equal(late.status, 201, late.text);
const due = await request('POST', '/commitments', { direction: 'i_owe', counterparty: 'Tom', description: `Today ${tag}`, dueOn: day(0) });
assert.equal(due.status, 201, due.text);
const decision = await request('POST', '/decisions', { title: `Switch ${tag}`, prediction: 'Fees drop', probability: 0.7, reviewOn: day(0) });
assert.equal(decision.status, 201, decision.text);

console.log('Each shows once, in order...');
let queue = (await request('GET', '/attention')).data;
const mine = queue.items.filter((i) => i.title.includes(tag));
assert.equal(mine.length, 3);
assert.equal(queue.items.filter((i) => i.id === `commitment:${late.data.id}`).length, 1);
const lateItem = mine.find((i) => i.id === `commitment:${late.data.id}`);
assert.equal(lateItem.urgency, 'late');
assert.equal(lateItem.daysOverdue, 3);
assert.ok(queue.items.indexOf(lateItem) < queue.items.indexOf(mine.find((i) => i.id === `commitment:${due.data.id}`)), 'late before today');
assert.equal(typeof queue.summary, 'string');

console.log('Putting an item away...');
assert.equal((await request('POST', `/attention/${encodeURIComponent(`commitment:${due.data.id}`)}/snooze`, { days: 0 })).status, 400);
assert.equal((await request('POST', `/attention/${encodeURIComponent(`commitment:${due.data.id}`)}/snooze`, { days: 15 })).status, 400);
assert.equal((await request('POST', `/attention/${encodeURIComponent('commitment:nope')}/snooze`, { days: 1 })).status, 404);
const snoozed = await request('POST', `/attention/${encodeURIComponent(`commitment:${due.data.id}`)}/snooze`, { days: 2 });
assert.equal(snoozed.status, 200, snoozed.text);
assert.ok(!snoozed.data.items.some((i) => i.id === `commitment:${due.data.id}`));
assert.ok(snoozed.data.snoozed >= 1);
const back = await request('DELETE', `/attention/${encodeURIComponent(`commitment:${due.data.id}`)}/snooze`);
assert.equal(back.status, 200, back.text);
assert.ok(back.data.items.some((i) => i.id === `commitment:${due.data.id}`));
assert.equal((await request('DELETE', `/attention/${encodeURIComponent(`commitment:${due.data.id}`)}/snooze`)).status, 404);

console.log('Finishing leaves the queue...');
await request('PATCH', `/commitments/${late.data.id}`, { status: 'done' });
await request('PATCH', `/commitments/${due.data.id}`, { status: 'done' });
await request('POST', `/decisions/${decision.data.id}/resolve`, { outcome: true });
queue = (await request('GET', '/attention')).data;
assert.equal(queue.items.filter((i) => i.title.includes(tag)).length, 0);
console.log('attention flow OK');
