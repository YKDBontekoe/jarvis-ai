#!/usr/bin/env node
// What-if scenarios: exact decimal arithmetic with the working shown, mixed currencies refused, evaluating
// changes nothing, saving writes one Library report (and a decision only when asked, checked before anything is
// written), and budgets stay untouched.
import assert from 'node:assert/strict';

const base = process.env.JARVIS_BASE_URL ?? 'http://localhost:5082';

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
const side = (monthly, once = 0, currency = 'EUR') => ({ monthlyCost: monthly, months: 24, oneTimeCost: once, currency });
const scenario = (now, next, title = `Phone plan ${tag}`) => ({
  kind: 'savings',
  title,
  savingsBaseline: now,
  savingsAlternative: next,
});

const budgetsBefore = JSON.stringify((await request('GET', '/api/v1/finance/overview')).data.budgets);

console.log('Exact arithmetic with the working shown...');
const evaluated = await request('POST', '/api/v1/scenarios/evaluate', scenario(side(12.99), side(9.49, 15)));
assert.equal(evaluated.status, 200, JSON.stringify(evaluated.data));
assert.equal(evaluated.data.baseline.primary, 311.76);
assert.equal(evaluated.data.alternative.primary, 242.76);
const total = evaluated.data.deltas.find((delta) => delta.label === 'Total over the period');
assert.equal(total.value, 69);
assert.ok(total.explanation.includes('(12.99 × 24 + 0.00) − (9.49 × 24 + 15.00) = 69.00'));
assert.equal(evaluated.data.deltas.find((delta) => delta.label === 'Months to break even').value, 5);

console.log('Mistakes are refused...');
assert.equal((await request('POST', '/api/v1/scenarios/evaluate', scenario(side(10), side(9, 0, 'USD')))).status, 400);
assert.equal((await request('POST', '/api/v1/scenarios/evaluate', scenario(side(-1), side(9)))).status, 400);
assert.equal((await request('POST', '/api/v1/scenarios/evaluate', { kind: 'magic' })).status, 400);

console.log('A time scenario...');
const time = await request('POST', '/api/v1/scenarios/evaluate', {
  kind: 'time',
  timeBaseline: { hoursPerWeek: 5, taskHours: 40, bufferPercent: 20 },
  timeAlternative: { hoursPerWeek: 8, taskHours: 40, bufferPercent: 20 },
});
assert.equal(time.data.baseline.primary, 9.6);
assert.equal(time.data.alternative.primary, 6);

console.log('Saving writes one Library report and no decision unless asked...');
const decisionsBefore = (await request('GET', '/api/v1/decisions')).data.length;
const saved = await request('POST', '/api/v1/scenarios/save', { scenario: scenario(side(12.99), side(9.49, 15)) });
assert.equal(saved.status, 200, JSON.stringify(saved.data));
assert.ok(saved.data.libraryItemId);
assert.equal(saved.data.decisionId ?? null, null);
const item = await request('GET', `/api/v1/library/${saved.data.libraryItemId}`);
assert.equal(item.status, 200);
assert.ok(JSON.stringify(item.data).includes('changed nothing in your budgets'));
assert.equal((await request('GET', '/api/v1/decisions')).data.length, decisionsBefore);

console.log('A half-specified decision saves nothing...');
const libraryBefore = (await request('GET', '/api/v1/library')).data.length;
const half = await request('POST', '/api/v1/scenarios/save', {
  scenario: scenario(side(10), side(8)),
  decisionProbability: 0.7,
});
assert.equal(half.status, 400);
assert.equal((await request('GET', '/api/v1/library')).data.length, libraryBefore);

console.log('A decision when asked...');
const review = new Date(Date.now() + 90 * 86400000).toISOString().slice(0, 10);
const withDecision = await request('POST', '/api/v1/scenarios/save', {
  scenario: scenario(side(10), side(8), `Decide ${tag}`),
  decisionProbability: 0.7,
  decisionReviewOn: review,
});
assert.equal(withDecision.status, 200, JSON.stringify(withDecision.data));
assert.ok(withDecision.data.decisionId);

console.log('Budgets were untouched...');
assert.equal(JSON.stringify((await request('GET', '/api/v1/finance/overview')).data.budgets), budgetsBefore);

await request('DELETE', `/api/v1/library/${saved.data.libraryItemId}`);
await request('DELETE', `/api/v1/library/${withDecision.data.libraryItemId}`);
await request('DELETE', `/api/v1/decisions/${withDecision.data.decisionId}`);
console.log('OK scenario_flow');
