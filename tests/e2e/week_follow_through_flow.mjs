#!/usr/bin/env node
// Weekly review follow-through: counts reconcile to the records, reading creates nothing, accepting one suggestion
// changes exactly one record on the chosen day, and a dismissed suggestion stays away.
import assert from 'node:assert/strict';

const base = process.env.JARVIS_BASE_URL ?? 'http://localhost:5082';
const api = `${base}/api/v1`;

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
  return { status: response.status, data };
}

const tag = `zq${Date.now().toString(36)}`;
const iso = (offset) => new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);

console.log('Seeding...');
const before = await request('GET', '/reviews/weekly/follow-through');
assert.equal(before.status, 200);
const overdue = await request('POST', '/commitments', {
  direction: 'i_owe', counterparty: 'Rosa', description: `Send ${tag} tiles`, dueOn: iso(-9),
});
assert.equal(overdue.status, 201, JSON.stringify(overdue.data));
const finished = await request('POST', '/commitments', {
  direction: 'i_owe', counterparty: 'Tom', description: `Return ${tag} drill`,
});
await request('PATCH', `/commitments/${finished.data.id}`, { status: 'done' });
const project = await request('POST', '/projects', { name: `Kitchen ${tag}` });
await request('PUT', `/projects/${project.data.id}/focus`, { outcome: 'Cook for ten' });

console.log('The counts reconcile and reading creates nothing...');
const commitmentsBefore = (await request('GET', '/commitments')).data.length;
const week = await request('GET', '/reviews/weekly/follow-through');
assert.equal(week.data.fulfilled, before.data.fulfilled + 1);
assert.equal(week.data.overdue, before.data.overdue + 1);
assert.equal(week.data.stale, before.data.stale + 1);
assert.ok(week.data.fulfilledItems.concat(week.data.staleItems).some((item) => item.title.includes(tag)));
assert.ok(week.data.projectsNeedingStep.some((item) => item.title === `Kitchen ${tag}`));
assert.equal((await request('GET', '/commitments')).data.length, commitmentsBefore);
const plan = week.data.suggestions.find((s) => s.targetId === overdue.data.id);
const step = week.data.suggestions.find((s) => s.targetId === project.data.id);
assert.ok(plan || step, 'at least one suggestion is offered');
assert.ok(week.data.suggestions.length <= 3);

console.log('Accepting a plan moves one promise to the chosen day...');
const day = iso(3);
assert.equal((await request('POST', '/reviews/weekly/follow-through/accept', {
  kind: 'plan_commitment', targetId: overdue.data.id })).status, 400);
assert.equal((await request('POST', '/reviews/weekly/follow-through/accept', {
  kind: 'plan_commitment', targetId: overdue.data.id, on: iso(-1) })).status, 400);
const accepted = await request('POST', '/reviews/weekly/follow-through/accept', {
  kind: 'plan_commitment', targetId: overdue.data.id, on: day });
assert.equal(accepted.status, 200, JSON.stringify(accepted.data));
assert.equal(accepted.data.ref, `commitment:${overdue.data.id}`);
const moved = (await request('GET', '/commitments')).data.find((c) => c.id === overdue.data.id);
assert.equal(moved.dueOn, day);
assert.equal(moved.hasReminder, true);
assert.equal((await request('GET', '/commitments')).data.length, commitmentsBefore, 'no new record');
const again = await request('POST', '/reviews/weekly/follow-through/accept', {
  kind: 'plan_commitment', targetId: overdue.data.id, on: day });
assert.equal(again.status, 200);

console.log('Accepting a project step keeps the outcome...');
assert.equal((await request('POST', '/reviews/weekly/follow-through/accept', {
  kind: 'project_next_step', targetId: project.data.id })).status, 400);
const stepped = await request('POST', '/reviews/weekly/follow-through/accept', {
  kind: 'project_next_step', targetId: project.data.id, note: 'Call the plumber' });
assert.equal(stepped.status, 200, JSON.stringify(stepped.data));
const details = await request('GET', `/projects/${project.data.id}`);
assert.equal(details.data.project.outcome, 'Cook for ten');
assert.equal(details.data.nextAction.note, 'Call the plumber');

console.log('A dismissed suggestion stays away...');
const second = await request('POST', '/commitments', {
  direction: 'i_owe', counterparty: 'Rosa', description: `Call ${tag} plumber`, dueOn: iso(-5),
});
const withSecond = await request('GET', '/reviews/weekly/follow-through');
assert.ok(withSecond.data.suggestions.some((s) => s.targetId === second.data.id) ||
          withSecond.data.suggestions.length === 3, 'offered or crowded out by the limit');
const dismissed = await request('POST', '/reviews/weekly/follow-through/dismiss', {
  kind: 'plan_commitment', targetId: second.data.id });
assert.equal(dismissed.status, 204);
const after = await request('GET', '/reviews/weekly/follow-through');
assert.ok(!after.data.suggestions.some((s) => s.targetId === second.data.id));
assert.equal((await request('POST', '/reviews/weekly/follow-through/dismiss', { kind: 'bogus', targetId: second.data.id })).status, 400);
assert.equal((await request('POST', '/reviews/weekly/follow-through/accept', {
  kind: 'plan_commitment', targetId: '00000000-0000-7000-8000-000000000000', on: day })).status, 404);

for (const id of [overdue.data.id, finished.data.id, second.data.id]) await request('DELETE', `/commitments/${id}`);
await request('DELETE', `/projects/${project.data.id}`);
console.log('week_follow_through_flow: OK');
