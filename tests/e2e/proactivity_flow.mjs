import assert from 'node:assert/strict';

const base = (process.env.JARVIS_API_URL ?? 'http://localhost:5082').replace(/\/$/, '') + '/api/v1/proactivity/cases';
const suffix = crypto.randomUUID();
async function request(path = '', method = 'GET', body, expected = 200) {
  const response = await fetch(base + path, { method, headers: { 'content-type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text();
  assert.equal(response.status, expected, `${method} ${path}: ${text}`);
  return text ? JSON.parse(text) : null;
}
const draft = { kind: 'open_loop', title: 'Compare the offers', objective: `Compare the offers later ${suffix}`,
  explicit: false, enabled: false, sourceRefs: [], policy: { cadenceMinutes: 15, maxChecksPerDay: 2, quietStart: 0, quietEnd: 0 } };
let item = await request('', 'POST', draft);
assert.equal(item.status, 'suggested');
assert.equal((await request(`/${item.id}`)).id, item.id);
await request(`/${item.id}/commands`, 'POST', { command: 'enable', revision: item.revision }, 400);
item = await request(`/${item.id}/commands`, 'POST', { command: 'accept', revision: item.revision });
assert.equal(item.enabled, false);
const oldRevision = item.revision;
item = await request(`/${item.id}/commands`, 'POST', { command: 'enable', revision: item.revision });
await request(`/${item.id}/commands`, 'POST', { command: 'pause', revision: oldRevision }, 409);
item = await request(`/${item.id}/check`, 'POST');
assert.equal(item.checksToday, 1);
assert.equal(item.status, 'open');
assert.ok(item.lastCheckedAt);
const repeated = await request(`/${item.id}/check`, 'POST');
assert.equal(repeated.revision, item.revision);
item = await request(`/${item.id}/commands`, 'POST', { command: 'enable', revision: item.revision });
item = await request(`/${item.id}/check`, 'POST');
assert.equal(item.checksToday, 2);
item = await request(`/${item.id}/commands`, 'POST', { command: 'enable', revision: item.revision });
item = await request(`/${item.id}/check`, 'POST');
assert.equal(item.checksToday, 2, 'An exhausted daily budget must not run a third check');
assert.ok(Date.parse(item.nextCheckAt) > Date.now());
item = await request(`/${item.id}/commands`, 'POST', { command: 'pause', revision: item.revision });
assert.equal(item.nextCheckAt, null);
item = await request(`/${item.id}/commands`, 'POST', { command: 'resolve', revision: item.revision });
const duplicate = await request('', 'POST', draft);
assert.equal(duplicate.id, item.id);
assert.equal(duplicate.status, 'resolved');
assert.equal(duplicate.enabled, false);

let project = await request('', 'POST', { kind: 'project', title: 'A dependency plan', objective: `Prepare the comparison ${suffix}`,
  explicit: true, enabled: true, sourceRefs: [], policy: draft.policy,
  steps: [{ id: 'measure', title: 'Measure', dependsOn: [], minutes: 30 },
    { id: 'compare', title: 'Compare', dependsOn: ['measure'], minutes: 30 }] });
project = await request(`/${project.id}/commands`, 'POST', { command: 'done:measure', revision: project.revision });
project = await request(`/${project.id}/check`, 'POST');
assert.equal(project.nextAction, 'Compare');
assert.ok(project.receipts.length);
await request(`/${project.id}/commands`, 'POST', { command: 'stop', revision: project.revision });
console.log('Proactivity flow passed: durable suggestions, explicit enabling, checks, dependencies, conflicts, pause and terminal deduplication.');
