#!/usr/bin/env node
// Comparisons: deterministic ranking with unknowns left out, edits recompute it, save to the Library, and choosing
// logs one decision (and only one) without buying anything.
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

const tag = Date.now().toString(36);
const definition = {
  title: `Boiler ${tag}`,
  question: 'Which boiler?',
  criteria: [
    { id: 'price', name: 'Price', weight: 8 },
    { id: 'noise', name: 'Noise', weight: 2 },
  ],
  options: [
    { id: 'alpha', name: 'Alpha', cells: { price: { score: 9, note: '€1,900', source: 'quote-a.pdf', asOf: '2026-10-01' } } },
    { id: 'beta', name: 'Beta', cells: { price: { score: 5 }, noise: { score: 9 } } },
  ],
};

console.log('Creating and ranking...');
const created = await request('POST', '/comparisons', { definition });
assert.equal(created.status, 201, JSON.stringify(created.data));
const id = created.data.id;
let rows = created.data.result.rows;
assert.deepEqual(rows.map((row) => row.optionId), ['alpha', 'beta']);
assert.equal(rows[0].score, 9);          // only price is known for Alpha: unknown noise is left out, not zero
assert.equal(rows[0].coverage, 0.8);
assert.deepEqual(rows[0].unknownCriteria, ['Noise']);
assert.equal(rows[1].score, 5.8);        // (8*5 + 2*9) / 10
assert.equal(created.data.result.leaderId, 'alpha');

console.log('Recomputing after a cell edit...');
const edited = await request('PUT', `/comparisons/${id}/cell`, {
  option: 'Beta', criterion: 'price', cell: { score: 10, note: '€1,500', source: 'quote-b.pdf', asOf: '2026-10-02' },
});
assert.equal(edited.status, 200, JSON.stringify(edited.data));
assert.equal(edited.data.result.leaderId, 'beta');
assert.equal((await request('PUT', `/comparisons/${id}/cell`, { option: 'Gamma', criterion: 'price', cell: { score: 1 } })).status, 400);
assert.equal((await request('PUT', `/comparisons/${id}/cell`, { option: 'Beta', criterion: 'price', cell: { score: 11 } })).status, 400);

console.log('A deterministic result: same input, same ranking...');
const again = await request('GET', `/comparisons/${id}`);
assert.deepEqual(again.data.result, edited.data.result);

console.log('Saving to the Library...');
const saved = await request('POST', `/comparisons/${id}/library`);
assert.equal(saved.status, 200, JSON.stringify(saved.data));
const item = await request('GET', `/library/${saved.data.libraryItemId}`);
assert.equal(item.status, 200);
assert.match(item.data.content, /Noise: unknown/);
assert.match(item.data.content, /never counted as zero/);

console.log('Choosing logs one decision, repeating it creates nothing new...');
const today = new Date();
const review = new Date(today.getTime() + 30 * 86400000).toISOString().slice(0, 10);
const decided = await request('POST', `/comparisons/${id}/decide`, { optionId: 'beta', probability: 0.7, reviewOn: review });
assert.equal(decided.status, 200, JSON.stringify(decided.data));
assert.equal(decided.data.created, true);
const repeat = await request('POST', `/comparisons/${id}/decide`, { optionId: 'beta', probability: 0.7, reviewOn: review });
assert.equal(repeat.data.created, false);
assert.equal(repeat.data.decisionId, decided.data.decisionId);
assert.equal((await request('POST', `/comparisons/${id}/decide`, { optionId: 'alpha', probability: 0.7, reviewOn: review })).status, 409);
const decision = await request('GET', `/decisions/${decided.data.decisionId}`);
assert.equal(decision.status, 200);
assert.match(decision.data.title, /Choose Beta/);
assert.match(decision.data.context, /Still unknown: Noise|scored/);
assert.equal((await request('POST', `/comparisons/${id}/decide`, { optionId: 'beta', probability: 0.7, reviewOn: '2020-01-01' })).status, 200); // already decided: same decision

console.log('An edit cannot drop the decision link, and a bad shape is a 400...');
const renamed = await request('PUT', `/comparisons/${id}`, { definition: { ...definition, title: `Boiler ${tag} v2` } });
assert.equal(renamed.status, 200);
assert.equal(renamed.data.definition.decisionId, decided.data.decisionId);
assert.equal((await request('POST', '/comparisons', { definition: { ...definition, title: ' ' } })).status, 400);
assert.equal((await request('GET', '/comparisons/00000000-0000-7000-8000-000000000000')).status, 404);

const list = await request('GET', '/comparisons');
// The rename above sent the original definition, so Beta's price went back to 5 and Alpha leads again.
assert.ok(list.data.find((item) => item.id === id && item.decided && item.leader === 'Alpha'));

await request('DELETE', `/decisions/${decided.data.decisionId}`);
await request('DELETE', `/library/${saved.data.libraryItemId}`);
await request('DELETE', `/comparisons/${id}`);
console.log('comparison_flow: OK');
