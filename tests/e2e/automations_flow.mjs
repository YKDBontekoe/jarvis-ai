#!/usr/bin/env node
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

const definition = {
  schemaVersion: 1,
  trigger: { kind: 'manual' },
  actions: [{ kind: 'notification', title: 'Automation demo', body: 'E2E test notification' }],
  limits: { cooldownMinutes: 0, maxActionsPerRun: 2 },
};

console.log('Creating automation draft...');
const created = await request('POST', '/api/v1/automations', {
  name: 'E2E demo automation',
  definition,
});
assert.equal(created.status, 201, `create failed: ${JSON.stringify(created.data)}`);
const id = created.data.id;
console.log('Created', id);

console.log('Validating definition...');
const valid = await request('POST', '/api/v1/automations/validate', { name: 'x', definition });
assert.equal(valid.status, 200);
assert.equal(valid.data.valid, true);

console.log('Enabling automation...');
const enabled = await request('POST', `/api/v1/automations/${id}/enable`);
assert.equal(enabled.status, 200);
assert.equal(enabled.data.status, 'enabled');

console.log('Test run...');
const testRun = await request('POST', `/api/v1/automations/${id}/test-run`);
assert.equal(testRun.status, 200, `test-run failed: ${JSON.stringify(testRun.data)}`);
const runId = testRun.data.id;
console.log('Started test run', runId);

let latest;
for (let attempt = 0; attempt < 20; attempt++) {
  await new Promise((resolve) => setTimeout(resolve, 2000));
  const runs = await request('GET', `/api/v1/automations/${id}/runs`);
  assert.equal(runs.status, 200);
  assert.ok(runs.data.length >= 1);
  latest = runs.data[0];
  console.log(`Run poll ${attempt + 1}:`, latest.status);
  if (latest.status === 'completed' || latest.status === 'failed') break;
}

console.log('Final run status:', latest.status, 'results:', latest.actionResultsJson);
assert.equal(latest.status, 'completed');

console.log('Listing notifications...');
const notifications = await request('GET', '/api/v1/notifications');
assert.equal(notifications.status, 200);
const hit = notifications.data.find((n) => n.title === 'Automation demo');
assert.ok(hit, 'expected notification from automation');

console.log('Automations E2E flow passed.');
