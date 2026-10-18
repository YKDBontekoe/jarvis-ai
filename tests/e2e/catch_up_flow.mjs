#!/usr/bin/env node
// Catch-up: the in-app recap of what a quiet mode held back is grouped by kind; approvals and problems can never
// be left out; other kinds can be left out and put back; unknown kinds and bad input are refused; clearing works.
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

console.log('Reading the catch-up...');
const base = await request('GET', '/modes/catch-up');
assert.equal(base.status, 200, base.text);
assert.ok(Array.isArray(base.data.groups));
assert.equal(typeof base.data.summary, 'string');

console.log('Locked kinds stay in...');
assert.equal((await request('PUT', '/modes/catch-up/domains', { domain: 'approvals', included: false })).status, 400);
assert.equal((await request('PUT', '/modes/catch-up/domains', { domain: 'problems', included: false })).status, 400);
assert.equal((await request('PUT', '/modes/catch-up/domains', { domain: 'nope', included: false })).status, 400);

console.log('Leaving a kind out and putting it back...');
const out = await request('PUT', '/modes/catch-up/domains', { domain: 'updates', included: false });
assert.equal(out.status, 200, out.text);
const group = out.data.groups.find((g) => g.domain === 'updates');
assert.ok(group === undefined || group.included === false);
const back = await request('PUT', '/modes/catch-up/domains', { domain: 'updates', included: true });
assert.equal(back.status, 200, back.text);
assert.ok(back.data.groups.every((g) => g.included));

console.log('Clearing...');
const cleared = await request('POST', '/modes/catch-up/clear');
assert.equal(cleared.status, 200, cleared.text);
assert.equal(cleared.data.total, 0);
console.log('catch-up flow OK');
