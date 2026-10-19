#!/usr/bin/env node
// Named automation templates: each lists what it will do and what success looks like, creating one only makes a
// draft (never on), and the dry run explains deterministically without doing anything.
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

const list = await request('GET', '/api/v1/automations/templates');
assert.equal(list.status, 200);
for (const id of ['meeting-prep', 'document-follow-up', 'bill-reminder', 'weekly-project-review']) {
  const template = list.data.find((item) => item.id === id);
  assert.ok(template, `${id} is offered`);
  assert.ok(template.successCriterion.includes('Success'));
  assert.equal(template.needsApproval, false);
  assert.ok(template.preview.length >= 1 && template.preview[0].title);
}
const docs = list.data.find((item) => item.id === 'document-follow-up');
assert.equal(docs.preview[0].title, 'Follow-up points for Example', 'event templates preview with an example event');

console.log('Creating from a template makes a draft, not an active rule...');
const created = await request('POST', '/api/v1/automations/templates/bill-reminder/create');
assert.equal(created.status, 201, JSON.stringify(created.data));
assert.notEqual(created.data.status, 'enabled');

console.log('The dry run is deterministic and does nothing...');
const sample = { kind: 'file_uploaded', title: 'Quote.pdf', detail: '12 KB' };
const first = await request('POST', `/api/v1/automations/${created.data.id}/simulate`, sample);
const second = await request('POST', `/api/v1/automations/${created.data.id}/simulate`, sample);
assert.equal(first.status, 200, JSON.stringify(first.data));
assert.deepEqual(first.data, second.data);
assert.equal(first.data.steps.length, 1);
assert.equal(first.data.approvalsNeeded, 0);

await request('DELETE', `/api/v1/automations/${created.data.id}`);
console.log('OK automation_templates_flow');
