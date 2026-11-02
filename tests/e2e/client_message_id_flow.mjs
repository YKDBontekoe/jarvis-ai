// A send carries a client message id, so retrying it (after a reconnect or a lost response) never starts
// a second turn: the retry gets the original reply, and a changed payload under the same id is refused.
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

const conversation = (await request('POST', '/conversations', { title: 'New conversation' }, [201])).data;
const send = (body, statuses = [200]) =>
  request('POST', `/conversations/${conversation.id}/messages`, body, statuses);
const messages = async () => (await request('GET', `/conversations/${conversation.id}`)).data.messages;
const id = `outbox-${Date.now()}-1`;
let first;

await step('the first send with an id is answered normally', async () => {
  first = (await send({ content: 'Hello Jarvis', clientMessageId: id })).data;
  assert.match(first.content, /I'm Jarvis/);
  assert.equal((await messages()).filter(m => m.role === 'user').length, 1);
});

await step('retrying the same send returns the original reply and starts no new turn', async () => {
  const retry = (await send({ content: 'Hello Jarvis', clientMessageId: id })).data;
  assert.equal(retry.id, first.id);
  const all = await messages();
  assert.equal(all.filter(m => m.role === 'user').length, 1);
  assert.equal(all.filter(m => m.role === 'assistant').length, 1);
});

await step('the same id with different content is refused as a conflict', async () => {
  await send({ content: 'Something else entirely', clientMessageId: id }, [409]);
  assert.equal((await messages()).filter(m => m.role === 'user').length, 1);
});

await step('identical text under a new id is a new message', async () => {
  const again = (await send({ content: 'Hello Jarvis', clientMessageId: `${id}-2` })).data;
  assert.notEqual(again.id, first.id);
  assert.equal((await messages()).filter(m => m.role === 'user').length, 2);
});

await step('requests without an id keep working', async () => {
  const legacy = (await send({ content: 'Hello again' })).data;
  assert(legacy.content.length > 0);
});

await step('an invalid id is rejected', async () => {
  await send({ content: 'Hello', clientMessageId: 'not valid!' }, [400, 422]);
});

const failed = results.filter(result => result.status === 'failed');
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exitCode = failed.length ? 1 : 0;
