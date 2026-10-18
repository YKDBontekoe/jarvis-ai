#!/usr/bin/env node
// Language, currency and the home zone: choices are validated and private; a trip takes the base currency unless it
// names one; "today" for promises follows the chosen home zone (two zones 25 hours apart give different days).
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
const dayIn = (zone) => new Intl.DateTimeFormat('en-CA', { timeZone: zone }).format(new Date());
const behind = 'Pacific/Pago_Pago';
const ahead = 'Pacific/Kiritimati';
assert.notEqual(dayIn(behind), dayIn(ahead), 'the two zones are on different days');

console.log('Language and currency...');
assert.equal((await request('PUT', '/preferences/locale', { language: 'klingon' })).status, 400);
assert.equal((await request('PUT', '/preferences/locale', { currency: 'XYZ' })).status, 400);
const saved = await request('PUT', '/preferences/locale', { language: 'nl', currency: 'chf' });
assert.equal(saved.status, 200, saved.text);
assert.equal(saved.data.language, 'nl');
assert.equal(saved.data.baseCurrency, 'CHF');

console.log('A trip takes the base currency unless it names one...');
const start = new Date(Date.now() + 40 * 86400000).toISOString().slice(0, 10);
const end = new Date(Date.now() + 43 * 86400000).toISOString().slice(0, 10);
const trip = await request('POST', '/trips', { title: `Trip ${tag}`, destination: 'Bern', start, end });
assert.equal(trip.status, 201, trip.text);
assert.equal(trip.data.trip.currency, 'CHF');
const named = await request('POST', '/trips', { title: `Named ${tag}`, destination: 'Oslo', start, end, currency: 'NOK' });
assert.equal(named.data.trip.currency, 'NOK');
await request('DELETE', `/trips/${trip.data.trip.id}`);
await request('DELETE', `/trips/${named.data.trip.id}`);

console.log('Promises follow the chosen home zone...');
const promise = await request('POST', '/commitments', { direction: 'i_owe', counterparty: 'Rosa', description: `Zone ${tag}`, dueOn: dayIn(ahead) });
assert.equal(promise.status, 201, promise.text);
const inQueue = async () => (await request('GET', '/attention')).data.items.some((i) => i.id === `commitment:${promise.data.id}`);
assert.equal((await request('PUT', '/preferences/home-zone', { timeZoneId: behind })).status, 200);
assert.equal(await inQueue(), false, 'in the zone behind, that day has not started');
assert.equal((await request('PUT', '/preferences/home-zone', { timeZoneId: ahead })).status, 200);
assert.equal(await inQueue(), true, 'in the zone ahead, it is due today');

await request('PATCH', `/commitments/${promise.data.id}`, { status: 'done' });
await request('PUT', '/preferences/home-zone', { timeZoneId: '' });
await request('PUT', '/preferences/locale', { language: '', currency: '' });
console.log('locale flow OK');
