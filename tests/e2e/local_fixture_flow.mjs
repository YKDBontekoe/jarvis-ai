// Full-flow check against a local Development API whose Codex executable is
// fake_codex_app_server.mjs. Exercises streaming, tool chaining, approvals, memory,
// reminders, and durable background tasks through the public HTTP + SignalR surface.
import assert from 'node:assert/strict';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

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
  const started = Date.now();
  try {
    const evidence = await action();
    results.push({ name, status: 'passed', ms: Date.now() - started, evidence });
    console.log(`PASS ${name}` + (evidence ? ` — ${JSON.stringify(evidence).slice(0, 160)}` : ''));
  } catch (error) {
    results.push({ name, status: 'failed', error: error.message });
    console.log(`FAIL ${name}: ${error.message}`);
  }
}

async function waitFor(path, predicate, seconds = 60) {
  let last;
  for (const until = Date.now() + seconds * 1000; Date.now() < until;) {
    last = (await request('GET', path)).data;
    if (predicate(last)) return last;
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error(`Timed out waiting for ${path}: ${JSON.stringify(last).slice(0, 300)}`);
}

const conversation = (await request('POST', '/conversations', { title: 'New conversation' }, [201])).data;
const events = [];
const hub = new HubConnectionBuilder().withUrl(origin + '/hubs/events').configureLogging(LogLevel.Warning).build();
for (const name of ['message.delta', 'message.completed', 'tool.started', 'tool.completed', 'tool.failed',
  'tool.approval_required', 'agent.started', 'agent.completed', 'agent.waiting_for_approval'])
  hub.on(name, payload => { events.push({ name, payload }); });
await hub.start();
await hub.invoke('JoinConversation', conversation.id);
const send = (content, statuses = [200]) =>
  request('POST', `/conversations/${conversation.id}/messages`, { content }, statuses);
const eventsSince = index => events.slice(index);

await step('greeting streams deltas and titles the conversation', async () => {
  const mark = events.length;
  const reply = (await send('Hello Jarvis')).data;
  assert.match(reply.content, /I'm Jarvis/);
  const deltas = eventsSince(mark).filter(event => event.name === 'message.delta');
  assert(deltas.length > 3, 'expected several streamed deltas');
  assert.equal(deltas.map(event => event.payload.delta).join(''), reply.content);
  const listed = (await request('GET', '/conversations')).data.find(item => item.id === conversation.id);
  assert.equal(listed.title, 'Hello Jarvis');
  return { deltas: deltas.length };
});

await step('reminder tool schedules a durable reminder', async () => {
  const mark = events.length;
  const reply = (await send('Remind me to stretch in 10 minutes')).data;
  assert.match(reply.content, /stretch/);
  const tools = eventsSince(mark).filter(event => event.name.startsWith('tool.')).map(event => `${event.name}:${event.payload.tool}`);
  assert.deepEqual(tools, ['tool.started:CreateReminder', 'tool.completed:CreateReminder']);
  const reminder = (await request('GET', '/reminders')).data.find(item => item.title === 'stretch');
  assert(reminder && reminder.status === 'pending', 'reminder should be pending');
  return { tools, dueAt: reminder.dueAt };
});

await step('list reminders renders a Markdown table', async () => {
  const reply = (await send('What reminders do I have?')).data;
  assert.match(reply.content, /\| When \(UTC\) \| Reminder \|/);
  assert.match(reply.content, /stretch/);
});

await step('remember tool saves an explicit preference', async () => {
  const reply = (await send('Remember that I prefer oat milk in my coffee')).data;
  assert.match(reply.content, /remember/i);
  const memory = (await request('GET', '/memory')).data.find(item => /oat milk/i.test(item.content));
  assert(memory, 'memory should exist');
  return { kind: memory.kind, content: memory.content };
});

await step('memory recall uses the saved preference', async () => {
  const reply = (await send('What do you know about me?')).data;
  assert.match(reply.content, /oat milk/i);
});

await step('forget requires approval, then deletes after approval', async () => {
  const mark = events.length;
  const pending = await send('Please forget oat milk', [202]);
  const approval = pending.data[0];
  assert.equal(approval.toolName, 'ForgetMemory');
  assert(eventsSince(mark).some(event => event.name === 'tool.approval_required'));
  assert((await request('GET', '/memory')).data.some(item => /oat milk/i.test(item.content)),
    'memory must survive until approved');
  const decided = (await request('POST', `/approvals/${approval.id}/decision`, { approved: true })).data;
  assert.match(decided.content, /forgotten/);
  assert(!(await request('GET', '/memory')).data.some(item => /oat milk/i.test(item.content)));
  return { approvalId: approval.id };
});

await step('rejected approval leaves data untouched', async () => {
  await send('Remember that I like jazz');
  const pending = await send('Forget jazz', [202]);
  const decided = (await request('POST', `/approvals/${pending.data[0].id}/decision`, { approved: false })).data;
  assert.match(decided.content, /didn't do that/);
  assert((await request('GET', '/memory')).data.some(item => /jazz/i.test(item.content)));
});

await step('clock tool converts time zones', async () => {
  const reply = (await send('What time is it in Tokyo?')).data;
  assert.match(reply.content, /Asia\/Tokyo/);
});

await step('background task runs through Temporal to completion', async () => {
  const reply = (await send('Research the best espresso grinders in the background')).data;
  assert.match(reply.content, /background task/);
  const task = await waitFor('/tasks', items => items.some(item => item.status === 'completed'), 90);
  const completed = task.find(item => item.status === 'completed');
  return { status: completed.status, summary: completed.summary?.slice(0, 60) };
});

await step('conversation history persists every turn', async () => {
  const details = (await request('GET', `/conversations/${conversation.id}`)).data;
  assert(details.messages.length >= 16, `expected at least 16 messages, got ${details.messages.length}`);
  return { messages: details.messages.length };
});

await hub.stop();
const failed = results.filter(result => result.status === 'failed');
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exitCode = failed.length ? 1 : 0;
