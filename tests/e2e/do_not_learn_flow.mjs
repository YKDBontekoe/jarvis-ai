#!/usr/bin/env node
// Never learn about: the owner's topic list is validated, cleaned, de-duplicated and private; it can be cleared.
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

console.log('Empty to start...');
await request('PUT', '/memories/do-not-learn', { topics: [] });
assert.deepEqual((await request('GET', '/memories/do-not-learn')).data.topics, []);

console.log('Validation...');
assert.equal((await request('PUT', '/memories/do-not-learn', { topics: ['x'.repeat(61)] })).status, 400);
assert.equal((await request('PUT', '/memories/do-not-learn', { topics: Array.from({ length: 31 }, (_, i) => `topic ${i}`) })).status, 400);

console.log('Saving cleans and de-duplicates...');
const saved = await request('PUT', '/memories/do-not-learn', { topics: ['  My   health ', 'my health', '', 'Money worries'] });
assert.equal(saved.status, 200, saved.text);
assert.deepEqual(saved.data.topics, ['My health', 'Money worries']);
assert.deepEqual((await request('GET', '/memories/do-not-learn')).data.topics, ['My health', 'Money worries']);

console.log('Clearing...');
assert.deepEqual((await request('PUT', '/memories/do-not-learn', { topics: [] })).data.topics, []);
console.log('do not learn flow OK');
