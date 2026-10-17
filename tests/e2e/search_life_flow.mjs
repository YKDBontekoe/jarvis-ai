#!/usr/bin/env node
// Search finds people, promises, decisions and what was said inside a chat, and opens the chat at that message.
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
const search = async (query, kinds) =>
  (await request('GET', `/search?query=${encodeURIComponent(query)}${kinds ? `&kinds=${kinds}` : ''}`)).data;

console.log('Seeding a person, a promise, a decision and a chat...');
const person = await request('POST', '/people', { name: `Joël ${tag}`, relationship: 'colleague' });
assert.equal(person.status, 201, JSON.stringify(person.data));
const promise = await request('POST', '/commitments', {
  direction: 'i_owe',
  counterparty: 'Rosa',
  description: `Send the ${tag} quotes`,
});
assert.equal(promise.status, 201, JSON.stringify(promise.data));
const today = new Date().toISOString().slice(0, 10);
const decision = await request('POST', '/decisions', {
  title: `Pick ${tag} supplier`,
  prediction: 'It arrives on time',
  probability: 0.7,
  reviewOn: today,
});
assert.equal(decision.status, 201, JSON.stringify(decision.data));
const conversation = await request('POST', '/conversations', { title: 'Plain title' });
assert.equal(conversation.status, 201, JSON.stringify(conversation.data));
const sent = await request('POST', `/conversations/${conversation.data.id}/messages`, {
  content: `Please remember the ${tag}marker boiler appointment`,
});
assert.ok([200, 201, 202].includes(sent.status), `send failed: ${JSON.stringify(sent.data)}`);

console.log('People, promises and decisions are found and typed...');
let found = await search(tag, 'person,commitment,decision');
const byKind = Object.fromEntries(found.results.map((hit) => [hit.kind, hit]));
assert.equal(byKind.person.route.parameters.personId, person.data.id);
assert.equal(byKind.commitment.route.parameters.commitmentId, promise.data.id);
assert.equal(byKind.decision.route.parameters.decisionId, decision.data.id);
assert.match(byKind.commitment.summary, /You promised Rosa/);

console.log('Accents do not get in the way...');
found = await search(`joel ${tag}`, 'person');
assert.equal(found.results.length, 1);

console.log('A word only inside a chat message opens at that message...');
found = await search(`${tag}marker`, 'message');
const hit = found.results.find((item) => item.route.parameters.conversationId === conversation.data.id);
assert.ok(hit, 'the message is found');
assert.equal(hit.route.kind, 'message');
assert.match(hit.summary, /boiler appointment/);
const window = await request(
  'GET',
  `/conversations/${hit.route.parameters.conversationId}/messages?around=${hit.route.parameters.messageId}&limit=10`,
);
assert.equal(window.status, 200);
assert.equal(window.data.anchorId, hit.route.parameters.messageId);
assert.ok(window.data.items.some((item) => item.id === hit.route.parameters.messageId));

console.log('A bad kind is rejected; another chat id has no such message...');
assert.equal((await request('GET', '/search?query=x&kinds=bogus')).status, 400);
const otherChat = await request('POST', '/conversations', { title: 'Other' });
const missing = await request(
  'GET',
  `/conversations/${otherChat.data.id}/messages?around=${hit.route.parameters.messageId}`,
);
assert.equal(missing.status, 404);

await request('DELETE', `/people/${person.data.id}`);
await request('DELETE', `/commitments/${promise.data.id}`);
await request('DELETE', `/decisions/${decision.data.id}`);
await request('DELETE', `/conversations/${conversation.data.id}`);
await request('DELETE', `/conversations/${otherChat.data.id}`);
console.log('search_life_flow: OK');
