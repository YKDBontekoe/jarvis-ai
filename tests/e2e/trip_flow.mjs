#!/usr/bin/env node
// Trips: a trip keeps dates, options with dated and sourced quotes, an itinerary and a checklist; an option
// without a source is "not quoted", an old quote is "outdated"; one option per kind can be chosen; the budget
// counts only chosen prices; changing dates shows the difference first and un-verifies shifted items; nothing
// is booked; unknown trips are 404.
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

const day = (offset) => new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);
const start = day(30);
const end = day(33);

console.log('Creating a trip...');
const created = await request('POST', '/trips', {
  title: 'Lisbon e2e', destination: 'Lisbon', start, end, travelers: 2, abroad: true, budget: 1500, currency: 'EUR',
});
assert.equal(created.status, 201, created.text);
const id = created.data.trip.id;
assert.ok(created.data.unknowns.length > 0, 'unknowns are listed, not hidden');
assert.ok(created.data.checklist.some((c) => /government|visa|entry/i.test(`${c.text} ${c.note ?? ''}`)), 'entry paperwork is a pointer, not a promise');

console.log('Adding options with and without a source...');
const flight = await request('POST', `/trips/${id}/options`, {
  kind: 'transport', title: 'TAP 08:15', price: 240, currency: 'EUR', quotedOn: day(-1), forStart: start, forEnd: end,
  sourceUrl: 'https://airline.example/offer',
});
assert.equal(flight.status, 200, flight.text);
const stay = await request('POST', `/trips/${id}/options`, { kind: 'stay', title: 'Hotel Alfama', price: 410, currency: 'EUR' });
assert.equal(stay.status, 200, stay.text);
const old = await request('POST', `/trips/${id}/options`, {
  kind: 'stay', title: 'Old quote', price: 300, currency: 'EUR', quotedOn: day(-20), forStart: start, forEnd: end,
  sourceUrl: 'https://hotel.example/old',
});
const byTitle = (view, title) => view.options.find((o) => o.option.title === title);
assert.equal(byTitle(old.data, 'TAP 08:15').quote, 'current');
assert.equal(byTitle(old.data, 'Hotel Alfama').quote, 'unquoted');
assert.equal(byTitle(old.data, 'Old quote').quote, 'outdated');

console.log('Choosing counts toward the budget; one per kind...');
const flightId = byTitle(old.data, 'TAP 08:15').option.id;
const chosen = await request('PUT', `/trips/${id}/options/${flightId}/chosen`, { chosen: true });
assert.equal(chosen.status, 200, chosen.text);
assert.equal(chosen.data.budget.chosen, 240);
const stayA = byTitle(old.data, 'Hotel Alfama').option.id;
const stayB = byTitle(old.data, 'Old quote').option.id;
await request('PUT', `/trips/${id}/options/${stayA}/chosen`, { chosen: true });
const swapped = await request('PUT', `/trips/${id}/options/${stayB}/chosen`, { chosen: true });
assert.equal(swapped.data.options.filter((o) => o.option.kind === 'stay' && o.option.chosen).length, 1);

console.log('Itinerary and checklist...');
const item = await request('POST', `/trips/${id}/items`, { date: start, start: '08:15', end: '11:00', title: 'Flight', kind: 'travel' });
assert.equal(item.status, 200, item.text);
assert.ok(item.data.holds.length > 0 && item.data.holds.every((h) => h.title), 'holds are proposals');
const entry = item.data.checklist[0];
const ticked = await request('PUT', `/trips/${id}/checklist/${entry.id}`, { done: true });
assert.equal(ticked.data.checklist.find((c) => c.id === entry.id).done, true);

console.log('Changing dates previews first...');
const newStart = day(40);
const newEnd = day(43);
const preview = await request('POST', `/trips/${id}/dates/preview`, { start: newStart, end: newEnd });
assert.equal(preview.status, 200, preview.text);
const unchanged = await request('GET', `/trips/${id}`);
assert.equal(unchanged.data.trip.start, start, 'a preview changes nothing');
const applied = await request('PUT', `/trips/${id}/dates`, { start: newStart, end: newEnd });
assert.equal(applied.status, 200, applied.text);
assert.equal(applied.data.trip.start, newStart);
assert.equal(byTitle(applied.data, 'TAP 08:15').quote, 'other_dates', 'quotes for the old dates are flagged');

console.log('Refusals and 404s...');
const bad = await request('POST', '/trips', { title: 'x', destination: 'y', start: end, end: start });
assert.equal(bad.status, 400, bad.text);
assert.equal((await request('GET', '/trips/00000000-0000-0000-0000-000000000000')).status, 404);
assert.equal((await request('DELETE', `/trips/${id}`)).status, 204);
assert.equal((await request('GET', `/trips/${id}`)).status, 404);
console.log('trip flow OK');
