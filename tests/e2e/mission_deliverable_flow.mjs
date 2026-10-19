#!/usr/bin/env node
// Mission starters and deliverables: a starter turns a few answers into a goal that forbids outside actions, the
// planner makes a plan that waits for the owner, and the deliverable says honestly what exists, what was checked by
// code and what is missing. Saving to the Library is private and happens once.
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

const tag = Date.now().toString(36);

console.log('Listing starters...');
const templates = await request('GET', '/missions/templates');
assert.equal(templates.status, 200, templates.text);
assert.deepEqual(templates.data.map((t) => t.id), ['decision', 'brief', 'compare', 'meeting']);
assert.ok(templates.data.every((t) => t.questions.some((q) => q.required)));

console.log('Composing goals...');
const missing = await request('POST', '/missions/templates/decision/compose', { answers: { constraints: 'cheap' } });
assert.equal(missing.status, 400, missing.text);
assert.equal((await request('POST', '/missions/templates/novel/compose', { answers: {} })).status, 400);
const composed = await request('POST', '/missions/templates/decision/compose', {
  answers: { topic: `which kettle ${tag}`, criteria: 'speed', constraints: 'under 40 euro' },
});
assert.equal(composed.status, 200, composed.text);
assert.ok(composed.data.goal.includes(`which kettle ${tag}`));
assert.ok(composed.data.goal.includes('Do not send, post, buy, book or change anything outside Jarvis'));

console.log('Planning waits for the owner...');
const planned = await request('POST', '/missions', { goal: composed.data.goal, title: composed.data.title });
assert.equal(planned.status, 201, planned.text);
const id = planned.data.id;
assert.equal(planned.data.status, 'ready');

console.log('Reading the deliverable of a plan that has not run...');
const early = await request('GET', `/missions/${id}/deliverable`);
assert.equal(early.status, 200, early.text);
assert.equal(early.data.checks.find((c) => c.id === 'finished').ok, false);
assert.ok(early.data.reviewNote.includes('read it before you rely on it'));
assert.ok(early.data.effort.startsWith('Not started yet'), early.data.effort);
const refused = await request('POST', `/missions/${id}/deliverable/save`);
assert.equal(refused.status, 400, refused.text);

console.log('Other ids are unknown...');
assert.equal((await request('GET', '/missions/00000000-0000-0000-0000-000000000000/deliverable')).status, 404);

const removed = await request('DELETE', `/missions/${id}`);
assert.equal(removed.status, 204);
console.log('mission_deliverable_flow ok');
