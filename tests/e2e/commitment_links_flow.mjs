#!/usr/bin/env node
// Promises can be linked to a project and a person by the owner (never by name guessing), a linked promise shows
// under that person even with a different counterparty name, and an overdue owed-to-me promise gets a draft nudge
// that snooze / dismiss silence. Nothing is sent.
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
const day = (offset) => new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);
const person = await request('POST', '/api/v1/people', { name: `Rosa Visser ${tag}` });
const project = await request('POST', '/api/v1/projects', { name: `Kitchen ${tag}` });
assert.equal(person.status, 201, JSON.stringify(person.data));
assert.equal(project.status, 201, JSON.stringify(project.data));

const promise = await request('POST', '/api/v1/commitments', {
  direction: 'owed_to_me',
  counterparty: 'Ro',
  description: `Send the tile quote ${tag}`,
  dueOn: day(-6),
});
assert.equal(promise.status, 201, JSON.stringify(promise.data));
const id = promise.data.id;

console.log('Before linking, a nickname does not match the person...');
let view = await request('GET', `/api/v1/people/${person.data.id}/follow-through`);
assert.ok(!view.data.promises.some((item) => item.id === id));

console.log('Linking to my project and person...');
const linked = await request('PATCH', `/api/v1/commitments/${id}`, { projectId: project.data.id, personId: person.data.id });
assert.equal(linked.status, 200, JSON.stringify(linked.data));
assert.equal(linked.data.projectId, project.data.id);
assert.equal(linked.data.personId, person.data.id);
view = await request('GET', `/api/v1/people/${person.data.id}/follow-through`);
assert.ok(view.data.promises.some((item) => item.id === id), 'the linked promise shows under the person');

console.log('Foreign or missing ids are refused...');
assert.equal((await request('PATCH', `/api/v1/commitments/${id}`, { projectId: crypto.randomUUID() })).status, 400);
assert.equal((await request('PATCH', `/api/v1/commitments/${id}`, { personId: crypto.randomUUID() })).status, 400);

console.log('A draft nudge exists for the overdue promise...');
let nudges = await request('GET', '/api/v1/commitments/follow-ups');
let nudge = nudges.data.find((item) => item.commitmentId === id);
assert.ok(nudge, JSON.stringify(nudges.data));
assert.equal(nudge.daysOverdue, 6);
assert.ok(nudge.draft.startsWith('Hi Ro,'));
nudges = await request('GET', '/api/v1/commitments/follow-ups?language=nl');
assert.ok(nudges.data.find((item) => item.commitmentId === id).draft.startsWith('Hoi Ro,'));

console.log('Snooze and dismiss silence it...');
assert.equal((await request('POST', `/api/v1/commitments/${id}/follow-up/snooze`, { days: 0 })).status, 400);
assert.equal((await request('POST', `/api/v1/commitments/${id}/follow-up/snooze`, { days: 7 })).status, 204);
nudges = await request('GET', '/api/v1/commitments/follow-ups');
assert.ok(!nudges.data.some((item) => item.commitmentId === id));
assert.equal((await request('POST', `/api/v1/commitments/${id}/follow-up/dismiss`)).status, 204);
assert.equal((await request('POST', `/api/v1/commitments/${crypto.randomUUID()}/follow-up/dismiss`)).status, 404);

console.log('Unlinking keeps the other link...');
const cleared = await request('PATCH', `/api/v1/commitments/${id}`, { clearProject: true });
assert.equal(cleared.data.projectId, null);
assert.equal(cleared.data.personId, person.data.id);

await request('DELETE', `/api/v1/commitments/${id}`);
await request('DELETE', `/api/v1/people/${person.data.id}`);
await request('DELETE', `/api/v1/projects/${project.data.id}`);
console.log('OK commitment_links_flow');
