#!/usr/bin/env node
// Home routines: devices are example fixtures and say so; a step naming an absent device is unavailable (never
// moved to another device); a lock is never run by a routine; taking an action out is remembered; a run is a
// simulation with per-action outcomes and "nothing changed"; only simulated reversible steps can be undone, once.
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

console.log('Overview...');
const overview = await request('GET', '/home-routines');
assert.equal(overview.status, 200, overview.text);
assert.deepEqual(overview.data.routines.map((r) => r.id), ['morning', 'focus', 'leave', 'evening']);
assert.ok(overview.data.devices.every((d) => d.source === 'fixture'));
assert.match(overview.data.notice, /not connected to a real home/);

console.log('Plans are honest...');
await request('PUT', '/home-routines/focus/choices', { removed: [] });
const focus = await request('GET', '/home-routines/focus/plan');
const lamp = focus.data.steps.find((s) => s.step.id === 'f-light');
assert.equal(lamp.status, 'unavailable');
assert.equal(lamp.device.name, 'Bedroom lamp');
assert.equal(focus.data.summary, '2 of 3 would run, 1 unavailable.');
await request('PUT', '/home-routines/leave/choices', { removed: [] });
const leave = await request('GET', '/home-routines/leave/plan');
assert.equal(leave.data.steps.find((s) => s.step.id === 'l-lock').status, 'needs_approval');

console.log('Taking an action out is remembered and validated...');
const edited = await request('PUT', '/home-routines/leave/choices', { removed: ['l-eco'] });
assert.equal(edited.data.steps.find((s) => s.step.id === 'l-eco').status, 'removed');
assert.equal((await request('PUT', '/home-routines/leave/choices', { removed: ['nope'] })).status, 400);
assert.equal((await request('PUT', '/home-routines/focus/choices', { devices: { 'f-eco': 'lock-front' } })).status, 400);

console.log('Running is a simulation...');
const run = await request('POST', '/home-routines/leave/run');
assert.equal(run.status, 200, run.text);
const outcome = (id) => run.data.steps.find((s) => s.stepId === id).outcome;
assert.equal(outcome('l-living'), 'simulated');
assert.equal(outcome('l-eco'), 'skipped');
assert.equal(outcome('l-lock'), 'not_run');
assert.match(run.data.summary, /Nothing changed in your home/);

console.log('Undo works once...');
const undone = await request('POST', `/home-routines/runs/${run.data.id}/reverse`);
assert.equal(undone.status, 200, undone.text);
assert.equal(undone.data.steps.find((s) => s.stepId === 'l-living').outcome, 'reversed');
assert.equal((await request('POST', `/home-routines/runs/${run.data.id}/reverse`)).status, 400);
const morning = await request('POST', '/home-routines/morning/run');
assert.ok(morning.data.steps.every((s) => s.outcome === 'read'));
assert.equal((await request('POST', `/home-routines/runs/${morning.data.id}/reverse`)).status, 400);

console.log('404s...');
assert.equal((await request('GET', '/home-routines/nothing/plan')).status, 404);
assert.equal((await request('POST', '/home-routines/nothing/run')).status, 404);
assert.equal((await request('POST', '/home-routines/runs/unknown/reverse')).status, 404);
console.log('home routine flow OK');
