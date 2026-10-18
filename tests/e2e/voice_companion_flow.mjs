#!/usr/bin/env node
// Voice companion cards: a voice tool call that answers "what needs me" also stores a card with the same real items;
// saving writes one private Library note, a put-away card cannot be saved, and the recap keeps saved, put away and
// open apart without claiming anything was sent. Audio itself needs a device and is not exercised here.
import assert from 'node:assert/strict';

const api = `${process.env.JARVIS_BASE_URL ?? 'http://localhost:5082'}/api/v1`;
const secret = process.env.VOICE_WORKER_SECRET ?? 'jarvis-local-voice-worker-development-secret';

async function request(method, path, body, headers = {}) {
  const response = await fetch(`${api}${path}`, {
    method,
    headers: { ...(body ? { 'Content-Type': 'application/json' } : {}), ...headers },
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

console.log('Opening a conversation...');
const conversation = await request('POST', '/conversations', { title: 'Voice companion' });
assert.equal(conversation.status, 201, conversation.text);
const id = conversation.data.id;
const owner = conversation.data.ownerId ?? '01996b8c-6000-7000-8000-000000000001';

console.log('A promise that is late, so something needs attention...');
const day = (offset) => new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);
await request('POST', '/commitments', {
  direction: 'i_owe', counterparty: 'Rosa', description: `Voice deck ${Date.now().toString(36)}`, dueOn: day(-2),
});

const empty = await request('GET', `/voice/${id}/cards`);
assert.equal(empty.status, 200, empty.text);
assert.deepEqual(empty.data, []);

console.log('A voice tool call produces a card...');
const call = await request('POST', `/voice/internal/${id}/tools/GetOpportunities`, { ownerId: owner, arguments: {} },
  { 'X-Jarvis-Voice-Secret': secret });
assert.equal(call.status, 200, call.text);
assert.equal(call.data.isError, false, call.text);
const cards = await request('GET', `/voice/${id}/cards`);
assert.equal(cards.data.length, 1, cards.text);
const card = cards.data[0];
assert.equal(card.kind, 'needs_you');
assert.equal(card.state, 'shown');
assert.ok(card.items.length >= 1 && card.items.every((i) => i.title));
assert.ok(card.items.some((i) => i.title.includes('Voice deck') || i.title.length > 0));

console.log('Asking again replaces the open card...');
await request('POST', `/voice/internal/${id}/tools/GetOpportunities`, { ownerId: owner, arguments: {} },
  { 'X-Jarvis-Voice-Secret': secret });
assert.equal((await request('GET', `/voice/${id}/cards`)).data.length, 1);

console.log('Saving keeps one private note...');
const current = (await request('GET', `/voice/${id}/cards`)).data[0];
const saved = await request('POST', `/voice/cards/${current.id}/save`);
assert.equal(saved.status, 200, saved.text);
assert.equal(saved.data.state, 'saved');
assert.ok(saved.data.libraryItemId);
const again = await request('POST', `/voice/cards/${current.id}/save`);
assert.equal(again.data.libraryItemId, saved.data.libraryItemId);

console.log('A put-away card cannot be saved...');
await request('POST', `/voice/internal/${id}/tools/GetOpportunities`, { ownerId: owner, arguments: {} },
  { 'X-Jarvis-Voice-Secret': secret });
const open = (await request('GET', `/voice/${id}/cards`)).data.find((c) => c.state === 'shown');
assert.ok(open);
assert.equal((await request('POST', `/voice/cards/${open.id}/dismiss`)).data.state, 'dismissed');
assert.equal((await request('POST', `/voice/cards/${open.id}/save`)).status, 400);

console.log('The recap keeps them apart...');
const recap = await request('GET', `/voice/${id}/recap`);
assert.equal(recap.status, 200, recap.text);
assert.equal(recap.data.saved, 1);
assert.equal(recap.data.dismissed, 1);
assert.equal(recap.data.open, 0);
assert.ok(recap.data.statement.includes('Nothing was sent, accepted or changed outside Jarvis by voice'));

console.log('Unknown ids and other conversations...');
assert.equal((await request('POST', '/voice/cards/nope/save')).status, 404);
assert.equal((await request('GET', '/voice/00000000-0000-0000-0000-000000000000/recap')).status, 404);
assert.equal((await request('POST', `/voice/internal/${id}/tools/GetOpportunities`, { ownerId: owner, arguments: {} })).status, 401);

await request('DELETE', `/conversations/${id}`);
console.log('voice_companion_flow ok');
