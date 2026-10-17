#!/usr/bin/env node
// Personal app builder: ideas and a contract-first prompt (forbids outside changes by default), and the review
// routes for self-built apps behave for unknown ids. Writing an app needs the model, so the activation gate itself
// is covered by unit tests.
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

const unknown = '00000000-0000-0000-0000-000000000000';

console.log('Ideas...');
const ideas = await request('GET', '/custom-mcp-servers/starters');
assert.equal(ideas.status, 200, ideas.text);
assert.deepEqual(ideas.data.map((i) => i.id), ['tracker', 'lookup', 'helper', 'calculator']);

console.log('Composing the prompt...');
const composed = await request('POST', '/custom-mcp-servers/starters/compose', {
  purpose: 'track my water intake',
  inputs: 'glasses per day',
  result: 'a weekly total',
});
assert.equal(composed.status, 200, composed.text);
assert.ok(composed.data.prompt.includes('track my water intake'));
assert.ok(composed.data.prompt.includes('only read and calculate'));
assert.ok(composed.data.prompt.includes('Do not switch it on: I will review the code first.'));
const effectful = await request('POST', '/custom-mcp-servers/starters/compose', {
  purpose: 'post to my blog',
  changesOutside: true,
});
assert.ok(effectful.data.prompt.includes('ask me first'));
assert.equal((await request('POST', '/custom-mcp-servers/starters/compose', { purpose: '  ' })).status, 400);

console.log('Review routes...');
assert.equal((await request('GET', `/custom-mcp-servers/${unknown}/review`)).status, 404);
const list = await request('GET', '/custom-mcp-servers');
assert.equal(list.status, 200);
for (const app of list.data) {
  const review = await request('GET', `/custom-mcp-servers/${app.id}/review`);
  assert.equal(review.status, 200, review.text);
  assert.ok(review.data.limits.some((l) => l.includes('not a guarantee')));
}
const noHash = await request('POST', `/custom-mcp-servers/${unknown}/activate`, {});
assert.ok([400, 409].includes(noHash.status), noHash.text);
console.log('custom_app_builder_flow ok');
