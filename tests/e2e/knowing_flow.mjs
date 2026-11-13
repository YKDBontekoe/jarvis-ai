#!/usr/bin/env node
// Jarvis knows you: train its memory (right raises confidence, wrong deletes, outdated ends), the map of the unknown
// (a person missing a birthday becomes today's question; an answer lands in the graph; a skip is never asked again),
// the trust ladder (ten approvals in a row can be handed over and taken back; coding never can), and special chats
// (each starts a conversation with an opening line and the fake model answers in that chat's voice).
//
// Approvals and graph entities have no create endpoint, so they are written straight into Postgres: set
// JARVIS_E2E_PG_CONTAINER to the Postgres container (the first pgvector container by default).
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';

const base = process.env.JARVIS_BASE_URL ?? 'http://localhost:5082';
const api = `${base}/api/v1`;

async function request(method, path, body) {
  const response = await fetch(`${api}${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(240_000),
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

const container = process.env.JARVIS_E2E_PG_CONTAINER ??
  execFileSync('docker', ['ps', '--filter', 'ancestor=pgvector/pgvector:pg18', '--format', '{{.Names}}'])
    .toString().trim().split('\n')[0];
assert.ok(container, 'set JARVIS_E2E_PG_CONTAINER to the Postgres container name');
const password = execFileSync('docker', ['exec', container, 'printenv', 'POSTGRES_PASSWORD']).toString().trim();
const sql = (query) => execFileSync('docker', ['exec', '-e', `PGPASSWORD=${password}`, container, 'psql', '-U',
  'postgres', '-d', 'jarvis', '-At', '-v', 'ON_ERROR_STOP=1', '-c', query]).toString().trim();

const tag = Date.now().toString(36);

// ---- Train Jarvis ----
console.log('Train Jarvis: three unsure memories become cards...');
const made = {};
for (const [key, content] of [['right', `Runs before work ${tag}`], ['wrong', `Has a cat ${tag}`],
  ['outdated', `Lives in Leiden ${tag}`]]) {
  const created = await request('POST', '/memory', { kind: 'fact', content, importance: 0.5, confidence: 0.05 });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  made[key] = created.data.id;
}
const deck = await request('GET', '/knowing/training');
assert.equal(deck.status, 200, JSON.stringify(deck.data));
for (const id of Object.values(made))
  assert.ok(deck.data.cards.some((c) => c.memoryId === id), `missing card ${id} in ${JSON.stringify(deck.data.cards)}`);
const before = deck.data.stats.reviewed;

const right = await request('POST', `/knowing/training/${made.right}`, { verdict: 'right' });
assert.equal(right.status, 200, JSON.stringify(right.data));
assert.equal(right.data.stats.reviewed, before + 1);
assert.ok((await request('GET', `/memory/${made.right}`)).data.confidence >= 0.95);

assert.equal((await request('POST', `/knowing/training/${made.wrong}`, { verdict: 'wrong' })).status, 200);
assert.equal((await request('GET', `/memory/${made.wrong}`)).status, 404, 'a wrong memory is deleted');

assert.equal((await request('POST', `/knowing/training/${made.outdated}`, { verdict: 'outdated' })).status, 200);
const ended = await request('GET', `/memory/${made.outdated}`);
assert.ok(ended.data.validUntil, `an outdated memory is ended: ${JSON.stringify(ended.data)}`);

const after = await request('GET', '/knowing/training');
assert.ok(!after.data.cards.some((c) => Object.values(made).includes(c.memoryId)), 'answered cards leave the deck');
assert.equal(after.data.stats.reviewed, before + 3);
assert.ok(after.data.stats.today >= 3);
assert.equal((await request('POST', `/knowing/training/${made.right}`, { verdict: 'maybe' })).status, 400);
assert.equal((await request('POST', `/knowing/training/${randomUUID()}`, { verdict: 'right' })).status, 404);

// ---- map of the unknown ----
console.log('Map of the unknown: a well-known person without a birthday...');
const conversation = await request('POST', '/conversations', { title: `Knowing ${tag}` });
assert.ok(conversation.status < 300, JSON.stringify(conversation.data));
const owner = sql(`select "OwnerId" from conversations where "Id" = '${conversation.data.id}'`);
const person = randomUUID();
const name = `Lars ${tag}`;
sql(`insert into graph_entities ("Id", owner_id, name, key, type, summary, aliases, created_at, updated_at)
     values ('${person}', '${owner}', '${name}', '${name.toLowerCase().replace(/[^a-z0-9]/g, '')}', 'person', null, '[]', now(), now())`);
// Many relations make Lars one of the best-known entities, so he is considered for questions.
for (let i = 0; i < 40; i++)
  sql(`insert into graph_relations ("Id", owner_id, subject_id, predicate, object_value, valid_from, confidence,
       created_at) values ('${randomUUID()}', '${owner}', '${person}', 'likes', 'thing ${i}', now(), 1, now())`);
sql(`insert into graph_relations ("Id", owner_id, subject_id, predicate, object_value, valid_from, confidence,
     created_at) values ('${randomUUID()}', '${owner}', '${person}', 'lives_in', 'Utrecht', now(), 1, now())`);

let map = (await request('GET', '/knowing/map')).data;
const lars = map.entities.find((e) => e.id === person);
assert.ok(lars, `Lars is on the map: ${JSON.stringify(map.entities)}`);
assert.equal(lars.known, 1);
assert.equal(lars.expected, 3);

const answered = await request('POST', '/knowing/map/answer', {
  entityId: person, predicate: 'birthday', answer: '12 March',
});
assert.equal(answered.status, 200, JSON.stringify(answered.data));
assert.equal(answered.data.saved, true);
assert.equal(answered.data.map.entities.find((e) => e.id === person).known, 2);
const entity = await request('GET', `/graph/entities/${person}`);
assert.ok(JSON.stringify(entity.data).includes('12 March'), 'the answer is a fact in the graph');

map = (await request('POST', '/knowing/map/skip', { entityId: person, predicate: 'job_title' })).data;
for (let i = 0; i < 3; i++) {
  map = (await request('GET', '/knowing/map')).data;
  assert.ok(!(map.question?.entityId === person && map.question?.predicate === 'job_title'), 'a skip is not asked');
}
assert.equal((await request('POST', '/knowing/map/answer', {
  entityId: person, predicate: 'favourite_colour', answer: 'blue',
})).status, 404, 'only the map’s own questions can be answered');
assert.equal((await request('POST', '/knowing/map/answer', { entityId: person, predicate: 'birthday', answer: ' ' }))
  .status, 400);

// ---- trust ladder ----
console.log('Trust ladder: ten approvals in a row...');
// Seconds ago, so these are always the newest decisions even when an earlier run left some behind.
const decide = (tool, approved, secondsAgo) => sql(`insert into tool_approvals ("Id", "OwnerId", "ConversationId",
  "RequestId", "ToolCallId", "ToolName", "ArgumentsJson", "Status", "Approved", "CreatedAt", "DecidedAt", "ResumeStatus")
  values ('${randomUUID()}', '${owner}', '${conversation.data.id}', 'req-${randomUUID()}', 'call-${randomUUID()}',
  '${tool}', '{}', 'decided', ${approved}, now() - interval '${secondsAgo + 1} seconds',
  now() - interval '${secondsAgo} seconds', 'completed')`);
for (let i = 0; i < 11; i++) decide('DeleteExpense', true, 40 - i);
for (let i = 0; i < 11; i++) decide('RunCodingTask', true, 40 - i);

let ladder = (await request('GET', '/knowing/trust')).data;
const expenses = ladder.find((r) => r.category === 'expenses.delete');
assert.ok(expenses, JSON.stringify(ladder));
assert.equal(expenses.level, 'trusted');
assert.equal(expenses.canHandOver, true);
const coding = ladder.find((r) => r.category === 'coding.run');
assert.equal(coding.canHandOver, false);
assert.equal((await request('POST', '/knowing/trust/coding.run/hand-over')).status, 400);

assert.equal((await request('POST', '/knowing/trust/expenses.delete/hand-over')).status, 204);
ladder = (await request('GET', '/knowing/trust')).data;
assert.equal(ladder.find((r) => r.category === 'expenses.delete').level, 'hands_free');
assert.equal((await request('DELETE', '/knowing/trust/expenses.delete')).status, 204);

decide('DeleteExpense', false, 0);
ladder = (await request('GET', '/knowing/trust')).data;
const restarted = ladder.find((r) => r.category === 'expenses.delete');
assert.equal(restarted.level, 'new', 'a decline starts the climb again');
assert.equal(restarted.streak, 0);

// ---- special chats ----
console.log('Special chats: each opens with its own line and the model answers in its voice...');
async function startAndSay(body, say) {
  const started = await request('POST', '/chat-modes', body);
  assert.equal(started.status, 201, JSON.stringify(started.data));
  const id = started.data.conversationId;
  const history = await request('GET', `/conversations/${id}/messages`);
  const items = history.data.items ?? history.data;
  assert.ok(items.some((m) => m.role === 'assistant' && m.content === started.data.opening), 'opening line stored');
  const reply = await request('POST', `/conversations/${id}/messages`, { content: say });
  assert.ok(reply.status < 300, JSON.stringify(reply.data));
  const latest = await request('GET', `/conversations/${id}/messages`);
  const messages = latest.data.items ?? latest.data;
  return { id, answer: messages.filter((m) => m.role === 'assistant').at(-1)?.content ?? '' };
}

const stranger = await startAndSay({ kind: 'stranger' }, 'Mostly I write code and walk the dog.');
assert.match(stranger.answer, /closest to you/);

const council = await startAndSay({ kind: 'council', question: `Move to Berlin ${tag}?` }, 'Begin');
assert.match(council.answer, /Where they split/);
assert.match(council.answer, new RegExp(`Move to Berlin ${tag}`));

const someone = await request('POST', '/people', { name: `Rosa ${tag}` });
assert.ok(someone.status < 300, JSON.stringify(someone.data));
const personId = someone.data.id ?? someone.data.person?.id;
const rehearsal = await startAndSay(
  { kind: 'rehearse', personId, scenario: 'Telling her I can’t make the wedding' }, 'I have something to tell you.');
assert.match(rehearsal.answer, new RegExp(`\\(Rosa ${tag}\\)`));

const yesterdayYear = new Date(Date.now() - 400 * 86_400_000).toISOString().slice(0, 10);
const past = await startAndSay({ kind: 'past_self', asOf: yesterdayYear }, 'Do you know what happens next year?');
assert.match(past.answer, /don't know that yet/);

const modes = (await request('GET', '/chat-modes')).data;
for (const id of [stranger.id, council.id, rehearsal.id, past.id])
  assert.ok(modes.some((m) => m.conversationId === id), `listed: ${id}`);

assert.equal((await request('POST', '/chat-modes', { kind: 'council' })).status, 400);
assert.equal((await request('POST', '/chat-modes', { kind: 'past_self', asOf: '2999-01-01' })).status, 400);
assert.equal((await request('POST', '/chat-modes', { kind: 'rehearse', personId: randomUUID(), scenario: 'x' }))
  .status, 400);
assert.equal((await request('POST', '/chat-modes', { kind: 'poetry' })).status, 400);

console.log('Knowing flow passed.');
