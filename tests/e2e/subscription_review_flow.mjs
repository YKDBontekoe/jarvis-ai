#!/usr/bin/env node
// Renewal review: facts and paths from bank data; a pack saved to the Library; the provider's reply tracked as one
// promise; a comparison started once; a lower price only counts when confirmed; cancelling needs written evidence;
// choices are per owner and survive another scan.
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
const merchant = `Streamly ${tag}`;
const day = (offset) => new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);

console.log('Seeding four monthly charges...');
const expenseIds = [];
for (const offset of [-95, -65, -35, -5]) {
  const expense = await request('POST', '/api/v1/expenses', {
    amount: 12,
    currency: 'EUR',
    merchant,
    category: 'entertainment',
    spentOn: day(offset),
  });
  assert.equal(expense.status, 201, JSON.stringify(expense.data));
  expenseIds.push(expense.data.id);
}
const overview = await request('GET', '/api/v1/finance/overview');
const subscription = overview.data.subscriptions.find((item) => item.merchant === merchant);
assert.ok(subscription, 'the recurring charge is detected');
const id = subscription.id;
const review = `/api/v1/finance/subscriptions/${id}/review`;

console.log('The review lists the facts and all six paths...');
let view = await request('GET', review);
assert.equal(view.status, 200, JSON.stringify(view.data));
assert.ok(view.data.facts.some((fact) => fact.includes('estimate')));
assert.deepEqual(view.data.paths.map((path) => path.id), ['keep', 'investigate', 'negotiate', 'cancel', 'remind', 'archive']);
assert.equal(view.data.saving, null);
assert.equal((await request('GET', `/api/v1/finance/subscriptions/${crypto.randomUUID()}/review`)).status, 404);

console.log('Choosing cancel only notes the choice...');
view = await request('POST', `${review}/choice`, { choice: 'cancel' });
assert.equal(view.data.review.choice, 'cancel');
assert.equal(view.data.subscription.status, 'active');
assert.equal((await request('POST', `${review}/choice`, { choice: 'explode' })).status, 400);

console.log('A comparison starts once...');
const first = await request('POST', `${review}/comparison`);
const second = await request('POST', `${review}/comparison`);
assert.equal(first.status, 200, JSON.stringify(first.data));
assert.equal(first.data.comparisonId, second.data.comparisonId);

console.log('A pack is saved to the Library; nothing is sent...');
const pack = await request('POST', `${review}/pack`, { goal: 'lower_price' });
assert.equal(pack.status, 200, JSON.stringify(pack.data));
assert.ok(pack.data.markdown.includes('[ADD]') && pack.data.markdown.includes('Nothing was sent'));
const item = await request('GET', `/api/v1/library/${pack.data.libraryItemId}`);
assert.equal(item.status, 200);

console.log('Their reply is tracked as one promise...');
const follow = await request('POST', `${review}/follow-up`, {});
const again = await request('POST', `${review}/follow-up`, {});
assert.equal(follow.data.created, true);
assert.equal(again.data.created, false);
assert.equal(follow.data.commitmentId, again.data.commitmentId);
const promises = await request('GET', '/api/v1/commitments');
const promise = promises.data.find((item) => item.id === follow.data.commitmentId);
assert.equal(promise.direction, 'owed_to_me');
assert.equal(promise.suggested, false);

console.log('A saving only exists with a confirmed lower price...');
for (const bad of [0, 12, 15]) {
  assert.equal((await request('POST', `${review}/outcome`, { confirmedAmount: bad })).status, 400, `amount ${bad}`);
}
view = await request('GET', review);
assert.equal(view.data.saving, null);
view = await request('POST', `${review}/outcome`, { confirmedAmount: 9, note: 'email' });
assert.equal(view.status, 200, JSON.stringify(view.data));
assert.equal(view.data.saving.perMonth, 3);
assert.equal(view.data.effectiveAmount, 9);
const closed = (await request('GET', '/api/v1/commitments')).data.find((item) => item.id === follow.data.commitmentId);
assert.equal(closed.status, 'done');

console.log('Cancelling needs written evidence...');
assert.equal((await request('POST', `${review}/cancellation`, { evidence: 'done' })).status, 400);
view = await request('GET', review);
assert.equal(view.data.subscription.status, 'active');
view = await request('POST', `${review}/cancellation`, { evidence: 'Reference ST-4410, their email of today' });
assert.equal(view.status, 200, JSON.stringify(view.data));
assert.equal(view.data.subscription.status, 'cancelled');

console.log('Another scan does not undo the choices...');
await request('GET', '/api/v1/finance/overview');
view = await request('GET', review);
assert.equal(view.data.review.choice, 'cancel');
assert.ok(view.data.review.cancelEvidence.includes('ST-4410'));

for (const expenseId of expenseIds) await request('DELETE', `/api/v1/expenses/${expenseId}`);
await request('DELETE', `/api/v1/library/${pack.data.libraryItemId}`);
await request('DELETE', `/api/v1/commitments/${follow.data.commitmentId}`);
await request('DELETE', `/api/v1/comparisons/${first.data.comparisonId}`);
console.log('OK subscription_review_flow');
