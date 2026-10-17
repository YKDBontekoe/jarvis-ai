#!/usr/bin/env node
// Memory review: an unsure memory is listed, a correction replaces it (old wording kept as history, no longer found),
// a pinned memory is not replaced, and "no longer true" ends one.
import assert from 'node:assert/strict';

const base = process.env.JARVIS_BASE_URL ?? 'http://localhost:5082';
const api = `${base}/api/v1`;

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
  return { status: response.status, data };
}

const tag = `zq${Date.now().toString(36)}`;
const make = (content, extra = {}) =>
  request('POST', '/memory', { kind: 'routine', content, importance: 0.6, confidence: 0.4, isPinned: false, ...extra });

console.log('An unsure memory shows up in the review...');
const memory = await make(`${tag} Rosa visits on Wednesdays`);
assert.equal(memory.status, 201, JSON.stringify(memory.data));
let review = (await request('GET', '/memory/review')).data;
const entry = review.find((item) => item.id === memory.data.id);
assert.ok(entry, 'listed');
assert.ok(entry.reasons.includes('low_confidence'));
assert.equal(entry.source.type, 'user');

console.log('Correcting it replaces it and keeps the old wording as history...');
const corrected = await request('POST', `/memory/${memory.data.id}/correct`, {
  content: `${tag} Rosa visits on Thursdays now`,
});
assert.equal(corrected.status, 200, JSON.stringify(corrected.data));
const newId = corrected.data.replacementId;
assert.ok(newId && newId !== memory.data.id);
// Search ORs its terms, so leftovers from other runs can match too: look only at this run's memories.
const mine = (hits) => hits.filter((hit) => hit.memory.content.includes(tag));
const found = mine((await request('GET', `/memory/search?query=${encodeURIComponent(`${tag} Rosa visits`)}`)).data);
assert.deepEqual(found.map((hit) => hit.memory.id), [newId]);
assert.match(found[0].memory.content, /Thursdays/);
const history = (await request('GET', `/memory/${newId}/history`)).data;
assert.equal(history.length, 1);
assert.match(history[0].content, /Wednesdays/);
assert.notEqual(history[0].replacedAt, null);

console.log('The old one cannot be corrected again...');
const again = await request('POST', `/memory/${memory.data.id}/correct`, { content: `${tag} Rosa visits on Fridays` });
assert.equal(again.status, 409);
const tooShort = await request('POST', `/memory/${newId}/correct`, { content: 'no' });
assert.equal(tooShort.status, 400);

console.log('A pinned memory is refused...');
const pinned = await make(`${tag} Always answer in Dutch`, { isPinned: true, confidence: 0.95 });
assert.equal(pinned.status, 201, JSON.stringify(pinned.data));
const refused = await request('POST', `/memory/${pinned.data.id}/correct`, { content: `${tag} Answer in English` });
assert.equal(refused.status, 409);
assert.match(refused.data.detail, /pinned/);
assert.equal((await request('POST', `/memory/${pinned.data.id}/expire`)).status, 409);

console.log('"No longer true" ends a memory without a replacement...');
const plain = await make(`${tag} Lives near the harbour`, { confidence: 0.95 });
const ended = await request('POST', `/memory/${plain.data.id}/expire`);
assert.equal(ended.status, 200, JSON.stringify(ended.data));
assert.equal(ended.data.replacementId, null);
const gone = mine((await request('GET', `/memory/search?query=${encodeURIComponent(`${tag} harbour`)}`)).data)
  .filter((hit) => hit.memory.content.includes('harbour'));
assert.equal(gone.length, 0);

assert.equal((await request('POST', '/memory/00000000-0000-7000-8000-000000000000/correct', { content: 'whatever now' })).status, 404);

for (const id of [memory.data.id, newId, pinned.data.id, plain.data.id]) await request('DELETE', `/memory/${id}`);
console.log('memory_review_flow: OK');
