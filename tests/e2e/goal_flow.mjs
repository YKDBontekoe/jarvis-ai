#!/usr/bin/env node
// Goals: one goal linked to a habit, a project next step and a promise; a check-in in the owner's words; a weekly
// plan that fits the minutes set aside and is only a proposal; accept / shrink / decline; scheduling an accepted
// step puts it on today's plan (promise sources keep their link); declined steps stay quiet; pause, reset season
// and the guard rails.
import assert from 'node:assert/strict';

const base = process.env.JARVIS_BASE_URL ?? 'http://localhost:5082';
const zone = 'Europe/Amsterdam';

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
const project = await request('POST', '/api/v1/projects', { name: `Marathon ${tag}` });
assert.equal(project.status, 201, JSON.stringify(project.data));
await request('PUT', `/api/v1/projects/${project.data.id}/focus`, {
  outcome: 'Finish a 10k',
  nextAction: { note: `Pick a training plan ${tag}` },
});
const habit = await request('POST', '/api/v1/habits', { name: `Run ${tag}`, cadence: 'weekly', targetPerWeek: 3 });
assert.equal(habit.status, 201, JSON.stringify(habit.data));
const promise = await request('POST', '/api/v1/commitments', {
  direction: 'i_owe',
  counterparty: 'Anna',
  description: `Book the race ${tag}`,
});

console.log('Creating a goal that links existing things...');
const created = await request('POST', '/api/v1/goals', {
  title: `Run a 10k ${tag}`,
  motivation: 'Feel fit',
  weeklyMinutes: 90,
  projectId: project.data.id,
  habitIds: [habit.data.id],
  commitmentIds: [promise.data.id],
});
assert.equal(created.status, 201, JSON.stringify(created.data));
const id = created.data.goal.id;
assert.equal(created.data.project.name, `Marathon ${tag}`);
assert.equal(created.data.habits.length, 1);
assert.equal(created.data.commitments.length, 1);

console.log('Links to things that are not mine are refused...');
const bad = await request('POST', '/api/v1/goals', { title: 'x', habitIds: [crypto.randomUUID()] });
assert.equal(bad.status, 400);

console.log('A check-in in my own words; a skipped one is never an error...');
const checkIn = await request('POST', `/api/v1/goals/${id}/check-ins`, {
  progress: 'Ran once',
  friction: 'Rainy week',
  feeling: 'slipping',
});
assert.equal(checkIn.status, 200, JSON.stringify(checkIn.data));
assert.equal(checkIn.data.progress.reflections[0].progress, 'Ran once');
assert.equal((await request('POST', `/api/v1/goals/${id}/check-ins`, { progress: ' ' })).status, 400);

console.log('The weekly plan is a proposal that fits 90 minutes...');
const plan = await request('POST', `/api/v1/goals/${id}/plan`);
assert.equal(plan.status, 200, JSON.stringify(plan.data));
const items = plan.data.goal.plan.items;
assert.ok(items.length >= 1 && items.length <= 3);
assert.ok(items.reduce((sum, item) => sum + item.minutes, 0) <= 90);
assert.ok(items.every((item) => item.status === 'proposed'));
let today = await request('GET', `/api/v1/planner/today?timeZone=${zone}`);
assert.ok(!today.data.items.some((item) => item.title.includes(tag)), 'proposing schedules nothing');

const step = items.find((item) => item.ref === `commitment:${promise.data.id}`);
assert.ok(step, JSON.stringify(items));
const act = (itemId, body, query = '') => request('POST', `/api/v1/goals/${id}/plan/items/${itemId}${query}`, body);

console.log('Scheduling before accepting is refused; accepting then scheduling lands on Today...');
assert.equal((await act(step.id, { action: 'schedule' })).status, 409);
assert.equal((await act(step.id, { action: 'accept' })).status, 200);
const reduced = await act(step.id, { action: 'reduce', minutes: 15 });
assert.equal(reduced.data.goal.plan.items.find((item) => item.id === step.id).minutes, 15);
assert.equal((await act(step.id, { action: 'reduce', minutes: 20 })).status, 400);
assert.equal((await act(step.id, { action: 'schedule' }, `?timeZone=${zone}`)).status, 200);
today = await request('GET', `/api/v1/planner/today?timeZone=${zone}`);
const planned = today.data.items.find((item) => item.sourceRef === `commitment:${promise.data.id}`);
assert.ok(planned, 'the step is on today with its promise link');
assert.equal(planned.minutes, 15);

console.log('A declined step is not proposed again...');
const other = items.find((item) => item.id !== step.id);
if (other) {
  assert.equal((await act(other.id, { action: 'decline' })).status, 200);
  const again = await request('POST', `/api/v1/goals/${id}/plan`);
  assert.ok(
    !again.data.goal.plan.items.some((item) => item.key === other.key && item.status === 'proposed'),
    'declined stays quiet',
  );
}

console.log('One experiment at a time, with observations...');
const experiment = await request('POST', `/api/v1/goals/${id}/experiments`, {
  change: 'Run before breakfast',
  expectedBenefit: 'More energy',
  durationDays: 14,
});
assert.equal(experiment.status, 200, JSON.stringify(experiment.data));
const experimentId = experiment.data.goal.experiments[0].id;
assert.equal(
  (await request('POST', `/api/v1/goals/${id}/experiments`, { change: 'b', expectedBenefit: 'c', durationDays: 7 })).status,
  409,
);
assert.equal((await request('POST', `/api/v1/goals/${id}/experiments/${experimentId}/notes`, { text: 'Slept badly' })).status, 200);
const finished = await request('POST', `/api/v1/goals/${id}/experiments/${experimentId}/finish`, { keep: true });
assert.equal(finished.data.goal.experiments[0].status, 'kept');

console.log('Pause, reset the season, archive: history is kept...');
const paused = await request('PATCH', `/api/v1/goals/${id}`, { status: 'paused' });
assert.equal(paused.data.goal.status, 'paused');
assert.equal((await request('POST', `/api/v1/goals/${id}/plan`)).status, 409);
const reset = await request('POST', `/api/v1/goals/${id}/reset-season`);
assert.equal(reset.data.goal.season, 2);
assert.equal(reset.data.goal.checkIns.length, 1);
assert.equal(reset.data.progress.reflections.length, 0);
await request('PATCH', `/api/v1/goals/${id}`, { status: 'archived' });
const list = await request('GET', '/api/v1/goals');
assert.ok(!list.data.some((goal) => goal.id === id), 'archived goals leave the list');
const all = await request('GET', '/api/v1/goals?includeArchived=true');
assert.ok(all.data.some((goal) => goal.id === id));

assert.equal((await request('GET', `/api/v1/goals/${crypto.randomUUID()}`)).status, 404);
await request('DELETE', `/api/v1/goals/${id}`);
await request('DELETE', `/api/v1/planner/today/items/${planned.id}?timeZone=${zone}`);
await request('DELETE', `/api/v1/commitments/${promise.data.id}`);
await request('DELETE', `/api/v1/habits/${habit.data.id}`);
await request('DELETE', `/api/v1/projects/${project.data.id}`);
console.log('OK goal_flow');
