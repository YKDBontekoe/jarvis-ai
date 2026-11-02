// Search inside one conversation: finds a match far above the latest messages, stays inside that
// conversation, and treats a conversation that is not the caller's as not found.
import assert from 'node:assert/strict';

const origin = process.env.JARVIS_API_URL ?? 'http://localhost:5082';
const base = origin + '/api/v1';
const results = [];

async function request(method, path, data, statuses = [200]) {
  const response = await fetch(base + path, {
    method, headers: data ? { 'Content-Type': 'application/json' } : {},
    body: data ? JSON.stringify(data) : undefined, signal: AbortSignal.timeout(120_000),
  });
  const body = await response.text();
  assert(statuses.includes(response.status), `${method} ${path}: ${response.status} ${body}`);
  return { status: response.status, data: body ? JSON.parse(body) : null };
}

async function step(name, action) {
  try {
    const evidence = await action();
    results.push({ name, status: 'passed' });
    console.log(`PASS ${name}` + (evidence ? ` — ${JSON.stringify(evidence).slice(0, 160)}` : ''));
  } catch (error) {
    results.push({ name, status: 'failed' });
    console.log(`FAIL ${name}: ${error.message}`);
  }
}

const chat = (await request('POST', '/conversations', { title: 'New conversation' }, [201])).data;
const other = (await request('POST', '/conversations', { title: 'New conversation' }, [201])).data;
const send = (id, content) => request('POST', `/conversations/${id}/messages`, { content });
const search = (query, conversationId, statuses = [200]) =>
  request('GET', `/search?query=${encodeURIComponent(query)}${conversationId ? `&conversationId=${conversationId}` : ''}`, undefined, statuses);

await send(chat.id, 'Hello Jarvis, remember zebracrossing for later');
await send(other.id, 'Hello Jarvis, zebracrossing is also here');
for (let i = 0; i < 4; i++) await send(chat.id, `Hello again number ${i}`);

await step('a scoped search finds the early message in that conversation only', async () => {
  const { data } = await search('zebracrossing', chat.id);
  const messages = data.results.filter(r => r.kind === 'message');
  assert.equal(messages.length, 1);
  assert.equal(messages[0].route.parameters.conversationId, chat.id);
  assert.match(messages[0].summary, /zebracrossing/);
  return { hits: data.results.length };
});

await step('an unscoped search finds it in both conversations', async () => {
  const { data } = await search('zebracrossing');
  const chats = new Set(data.results.filter(r => r.kind === 'message').map(r => r.route.parameters.conversationId));
  assert(chats.has(chat.id) && chats.has(other.id));
});

await step('a scoped search returns messages only, newest first', async () => {
  const { data } = await search('Hello', chat.id);
  assert(data.results.length >= 5);
  assert(data.results.every(r => r.kind === 'message'));
  const times = data.results.map(r => Date.parse(r.timestamp));
  assert.deepEqual(times, [...times].sort((a, b) => b - a));
});

await step('an unknown conversation id is not found', async () => {
  await search('zebracrossing', '00000000-0000-4000-8000-000000000000', [404]);
});

await step('an invalid conversation id is rejected', async () => {
  await search('zebracrossing', 'not-a-guid', [400, 404]);
});

const failed = results.filter(result => result.status === 'failed');
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exitCode = failed.length ? 1 : 0;
