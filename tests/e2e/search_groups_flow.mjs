#!/usr/bin/env node
// Search groups and filters: projects, trips and journal entries are found by a unique word with typed routes; the
// kinds filter narrows to one group; a date window keeps dated hits inside it and drops hits outside; bad dates are
// refused.
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

const tag = `zq${Date.now().toString(36)}`;
const day = (offset) => new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);

console.log('Seeding a project, a trip and a journal entry...');
const project = await request('POST', '/projects', { name: `Garden ${tag}` });
assert.ok([200, 201].includes(project.status), project.text);
const trip = await request('POST', '/trips', { title: `Trip ${tag}`, destination: 'Lisbon', start: day(30), end: day(33) });
assert.equal(trip.status, 201, trip.text);
const journal = await request('POST', '/journal', { entryDate: day(-1), content: `Walked the canal ${tag}` });
assert.ok([200, 201].includes(journal.status), journal.text);

console.log('Each is found with a typed route...');
const found = await request('GET', `/search?query=${tag}`);
assert.equal(found.status, 200, found.text);
const byKind = (kind) => found.data.results.find((r) => r.kind === kind);
assert.equal(byKind('project').route.parameters.projectId, project.data.id);
assert.equal(byKind('trip').route.parameters.tripId, trip.data.trip.id);
assert.equal(byKind('journal').route.kind, 'journal');
assert.match(byKind('journal').route.parameters.date, /^\d{4}-\d{2}-\d{2}$/);

console.log('The kinds filter narrows...');
const only = await request('GET', `/search?query=${tag}&kinds=trip`);
assert.deepEqual([...new Set(only.data.results.map((r) => r.kind))], ['trip']);
assert.equal((await request('GET', `/search?query=${tag}&kinds=nope`)).status, 400);

console.log('The date window keeps dated hits inside it...');
const recent = await request('GET', `/search?query=${tag}&from=${day(-3)}&to=${day(0)}`);
assert.ok(recent.data.results.some((r) => r.kind === 'journal'), 'the entry from yesterday is inside');
assert.ok(!recent.data.results.some((r) => r.kind === 'trip'), 'the trip starts in a month');
const future = await request('GET', `/search?query=${tag}&from=${day(20)}`);
assert.ok(future.data.results.some((r) => r.kind === 'trip'));
assert.ok(!future.data.results.some((r) => r.kind === 'journal'));
assert.equal((await request('GET', `/search?query=${tag}&from=yesterday`)).status, 400);
assert.equal((await request('GET', `/search?query=${tag}&from=${day(5)}&to=${day(1)}`)).status, 400);
console.log('search groups flow OK');
