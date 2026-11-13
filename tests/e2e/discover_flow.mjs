#!/usr/bin/env node
// Discover: a library note and a journal entry from a month earlier that share rare words collide; the same collision
// stays all day and "another" moves on; the museum shows the best-rated day; and a duel runs end to end between this
// Jarvis and itself over its own Agent2Agent endpoint (challenge, join by code, each side sees the other's score),
// with unknown codes and missing tokens refused.
import assert from 'node:assert/strict';

const base = process.env.JARVIS_BASE_URL ?? 'http://localhost:5082';
const api = `${base}/api/v1`;

async function request(method, path, body, headers = {}) {
  const url = path.startsWith('http') ? path : `${api}${path}`;
  const response = await fetch(url, {
    method,
    headers: { ...(body ? { 'Content-Type': 'application/json' } : {}), ...headers },
    body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(120_000),
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

const tag = Date.now().toString(36).replace(/[0-9]/g, (d) => 'abcdefghij'[d]);
const rare1 = `zephyrine${tag}`;
const rare2 = `quillwort${tag}`;
const monthAgo = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10);

// ---- collisions ----
console.log('Collisions: a note today and a journal entry a month ago share two rare words...');
const note = await request('POST', '/library/notes', {
  title: `On ${rare1}`, content: `Notes about ${rare1} and the ${rare2} by the lake.`,
});
assert.ok(note.status < 300, JSON.stringify(note.data));
const entry = await request('POST', '/journal', {
  entryDate: monthAgo, content: `Walked past the ${rare2} again and thought about ${rare1}.`, rating: 10, mood: 5,
});
assert.ok(entry.status < 300, JSON.stringify(entry.data));

// Earlier runs may have left their own collisions; ask for another until ours comes up.
let collision = (await request('GET', '/discover/collision')).data;
for (let i = 0; i < 30 && !(collision?.shared ?? []).includes(rare1); i++)
  collision = (await request('POST', '/discover/collision/next')).data;
assert.ok(collision, 'a collision was found');
assert.deepEqual([...collision.shared].sort(), [rare1, rare2].sort());
assert.equal(collision.left.kind, 'journal', 'the older side comes first');
assert.equal(collision.right.kind, 'library');
assert.match(collision.prompt, /^Help me see a connection between my journal entry/);
const again = (await request('GET', '/discover/collision')).data;
assert.equal(again.key, collision.key, 'the same collision all day');
const next = await request('POST', '/discover/collision/next');
assert.ok(next.status === 204 || next.data.key !== collision.key, 'another one is a different pair');

// ---- museum ----
console.log('Museum: the best-rated day hangs in its room...');
const museum = await request('GET', '/discover/museum');
assert.equal(museum.status, 200, JSON.stringify(museum.data));
const bestDays = museum.data.rooms.find((r) => r.key === 'best-days');
assert.ok(bestDays, JSON.stringify(museum.data.rooms.map((r) => r.key)));
assert.ok(bestDays.exhibits.some((e) => e.title.includes(rare2) && e.value === '10/10'), JSON.stringify(bestDays));
assert.equal(museum.data.rooms[0].key, 'numbers');
assert.equal((await request('GET', '/discover/museum?year=1990')).status, 400);

// ---- duels over A2A ----
console.log('Duels: this Jarvis registers itself as a friend and duels itself...');
const token = await request('POST', '/a2a/tokens', { name: `duel ${tag}` });
assert.ok(token.status < 300, JSON.stringify(token.data));
const agent = await request('POST', '/agents', {
  name: `Mirror ${tag}`, url: `${base}/a2a`, enabled: true, token: token.data.token,
});
assert.equal(agent.status, 201, JSON.stringify(agent.data));

const challenge = await request('POST', '/duels', { remoteAgentId: agent.data.id, metric: 'journal_days', days: 7 });
assert.equal(challenge.status, 201, JSON.stringify(challenge.data));
assert.equal(challenge.data.role, 'challenger');
assert.match(challenge.data.code, /^[A-HJ-NP-Z2-9]{8}$/);

const joined = await request('POST', '/duels/join', {
  remoteAgentId: agent.data.id, code: challenge.data.code.toLowerCase(),
});
assert.equal(joined.status, 201, JSON.stringify(joined.data));
assert.equal(joined.data.role, 'joiner');
assert.equal(joined.data.metric, 'journal_days');
assert.equal(joined.data.endsOn, challenge.data.endsOn);
assert.equal(joined.data.theirScore, challenge.data.myScore, 'the joiner sees the challenger’s score');
assert.equal((await request('POST', '/duels/join', { remoteAgentId: agent.data.id, code: challenge.data.code }))
  .status, 400, 'joining twice is refused');

const duels = (await request('GET', '/duels')).data;
const mine = duels.find((d) => d.id === challenge.data.id);
assert.equal(mine.theirScore, joined.data.myScore, 'the challenger sees the joiner’s score');
assert.equal(mine.lastError, null);

assert.equal((await request('POST', '/duels/join', { remoteAgentId: agent.data.id, code: 'ZZZZ2222' })).status, 400);
assert.equal((await request('POST', '/duels', { remoteAgentId: agent.data.id, metric: 'steps', days: 7 })).status, 400);
assert.equal((await request('POST', '/duels', { remoteAgentId: agent.data.id, metric: 'journal_days', days: 30 }))
  .status, 400);

console.log('The A2A methods need a token and say nothing about unknown codes...');
const rpc = (method, params, auth) => request('POST', `${base}/a2a`, { jsonrpc: '2.0', id: '1', method, params },
  auth ? { Authorization: `Bearer ${auth}` } : {});
assert.equal((await rpc('jarvis/duel.score', { code: challenge.data.code, role: 'joiner' })).status, 401);
const unknown = await rpc('jarvis/duel.score', { code: 'ZZZZ2222', role: 'joiner' }, token.data.token);
assert.equal(unknown.data.error.code, -32004);
const score = await rpc('jarvis/duel.score', { code: challenge.data.code, role: 'joiner' }, token.data.token);
assert.deepEqual(Object.keys(score.data.result), ['score'], 'only the score crosses');

assert.equal((await request('DELETE', `/duels/${challenge.data.id}`)).status, 204);
assert.equal((await request('DELETE', `/duels/${joined.data.id}`)).status, 204);
const cancelled = await rpc('jarvis/duel.score', { code: challenge.data.code, role: 'joiner' }, token.data.token);
assert.equal(cancelled.data.error.code, -32004, 'a called-off duel shares nothing');

console.log('Discover flow passed.');
