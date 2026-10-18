#!/usr/bin/env node
// Guided setup: steps come from what really exists (a reminder flips its step), optional steps can be skipped and
// brought back, required ones cannot be skipped, and the support report holds only versions and states.
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

const goal = (view, id) => view.goals.find((g) => g.id === id);

console.log('Reading setup...');
const first = await request('GET', '/setup');
assert.equal(first.status, 200, first.text);
assert.deepEqual(first.data.goals.map((g) => g.id), ['talk', 'reminder', 'calendar', 'source', 'brief']);
// Push delivery is not configured on this stack, and the server now says so instead of "cannot check".
const push = first.data.capabilities.find((c) => c.id === 'push');
assert.equal(push.status, 'unavailable');
assert.match(push.detail, /only appear in the app/);
for (const id of ['apps', 'scanner', 'storage']) assert.ok(first.data.capabilities.some((c) => c.id === id), `${id} is reported`);
assert.ok(['ready', 'unavailable'].includes(first.data.capabilities.find((c) => c.id === 'scanner').status));
assert.ok(['ready', 'unavailable'].includes(first.data.capabilities.find((c) => c.id === 'storage').status));
const model = first.data.capabilities.find((c) => c.id === 'model');
assert.ok(['ready', 'not_connected', 'needs_reauth'].includes(model.status), model.status);
assert.equal(first.data.total, 5);

console.log('A reminder completes its step...');
const tag = Date.now().toString(36);
const reminder = await request('POST', '/reminders', {
  title: `Setup check ${tag}`,
  dueAt: new Date(Date.now() + 86400000).toISOString(),
});
assert.ok([200, 201].includes(reminder.status), reminder.text);
const after = await request('GET', '/setup');
assert.equal(goal(after.data, 'reminder').done, true);
assert.ok(after.data.done >= first.data.done);

console.log('Skipping and bringing back...');
const skipped = await request('POST', '/setup/goals/calendar/decline');
assert.equal(skipped.status, 200, skipped.text);
if (!goal(skipped.data, 'calendar').done) assert.equal(goal(skipped.data, 'calendar').declined, true);
assert.notEqual(skipped.data.nextGoalId, 'calendar');
const required = await request('POST', '/setup/goals/talk/decline');
assert.equal(required.status, 400, required.text);
assert.equal((await request('POST', '/setup/goals/nonsense/decline')).status, 400);
const back = await request('POST', '/setup/goals/calendar/reopen');
assert.equal(goal(back.data, 'calendar').declined, false);

console.log('Reviewing the support report...');
const bundle = await request('GET', '/setup/support-bundle');
assert.equal(bundle.status, 200, bundle.text);
assert.ok(bundle.data.version);
assert.ok(bundle.data.goals.includes('reminder: done'));
assert.ok(!bundle.text.includes(tag), 'no user text in the report');
assert.ok(!/https?:\/\//.test(bundle.text), 'no addresses in the report');

const created = JSON.parse(reminder.text);
await request('DELETE', `/reminders/${created.id}`);
console.log('setup_flow ok');
