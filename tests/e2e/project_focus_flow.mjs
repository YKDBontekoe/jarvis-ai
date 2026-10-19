#!/usr/bin/env node
// Project outcome and next action: set, link a promise, see it finish, reject what is not linkable.
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
const project = await request('POST', '/api/v1/projects', { name: `Kitchen ${tag}` });
assert.equal(project.status, 201, JSON.stringify(project.data));
assert.equal(project.data.status, 'active');
const id = project.data.id;

const promise = await request('POST', '/api/v1/commitments', {
  direction: 'i_owe',
  counterparty: 'Rosa',
  description: `Send the tile quotes ${tag}`,
});
assert.equal(promise.status, 201, JSON.stringify(promise.data));

console.log('Setting outcome and linking the promise...');
const focus = await request('PUT', `/api/v1/projects/${id}/focus`, {
  outcome: 'Cook for ten by June',
  nextAction: { type: 'commitment', id: promise.data.id, note: 'Before Friday' },
});
assert.equal(focus.status, 200, JSON.stringify(focus.data));
assert.equal(focus.data.project.outcome, 'Cook for ten by June');
assert.equal(focus.data.nextAction.state, 'open');
assert.equal(focus.data.nextAction.title, promise.data.description);

console.log('The edit form does not wipe the outcome...');
const edited = await request('PUT', `/api/v1/projects/${id}`, { name: `Kitchen ${tag}!`, color: 'green' });
assert.equal(edited.status, 200);
let details = await request('GET', `/api/v1/projects/${id}`);
assert.equal(details.data.project.outcome, 'Cook for ten by June');
assert.equal(details.data.nextAction.note, 'Before Friday');

console.log('Finishing the promise marks the next step done...');
await request('PATCH', `/api/v1/commitments/${promise.data.id}`, { status: 'done' });
details = await request('GET', `/api/v1/projects/${id}`);
assert.equal(details.data.nextAction.state, 'done');

console.log('Only owned promises, decisions, reminders and tasks can be linked...');
const bad = await request('PUT', `/api/v1/projects/${id}/focus`, {
  nextAction: { type: 'expense', id: promise.data.id },
});
assert.equal(bad.status, 400);
const missing = await request('PUT', `/api/v1/projects/${id}/focus`, {
  nextAction: { type: 'commitment', id: '00000000-0000-7000-8000-000000000000' },
});
assert.equal(missing.status, 400);

console.log('Completing keeps the history...');
const done = await request('PUT', `/api/v1/projects/${id}/focus`, {
  outcome: 'Cook for ten by June',
  status: 'completed',
});
assert.equal(done.status, 200);
assert.equal(done.data.project.status, 'completed');
assert.equal(done.data.nextAction, null);

await request('DELETE', `/api/v1/commitments/${promise.data.id}`);
await request('DELETE', `/api/v1/projects/${id}`);
console.log('project_focus_flow: OK');
