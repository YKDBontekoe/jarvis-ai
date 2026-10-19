#!/usr/bin/env node
// Goal-led app discovery: a plain goal finds the app that can do it, with capabilities from its real tools and the
// real next step; nothing claims to work before it is connected; unrelated goals are described honestly; once the
// calendar link is saved the goal is ready, can be tried and pinned, and the secret never appears in the answers.
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
  return { status: response.status, data, text };
}

// The pack's saved link cannot be removed through the API (pack providers are not deletable there), so a run on a
// stack that an earlier run already connected skips the "not connected yet" half instead of failing.
const before = (await request('GET', '/api/v1/apps/goals?q=calendar')).data.goals.find((goal) => goal.id === 'calendar');
const fresh = before.status === 'not_connected';

console.log('A goal in plain words finds the calendar and is not yet connected...');
let search = await request('GET', '/api/v1/apps/goals?q=help%20with%20my%20calendar');
assert.equal(search.status, 200, search.text);
let calendar = search.data.goals.find((goal) => goal.id === 'calendar');
assert.ok(calendar);
if (fresh) {
  assert.equal(calendar.status, 'not_connected');
  assert.equal(calendar.try, null);
}
assert.deepEqual(calendar.capabilities.reads, ['calendar events list']);
assert.deepEqual(calendar.capabilities.changes, ['calendar event create']);
assert.ok(calendar.nextStep.includes(fresh ? 'private link' : 'Connected'));
if (fresh) {
  assert.equal((await request('POST', '/api/v1/apps/goals/calendar/try')).status, 404);
  assert.equal((await request('PUT', '/api/v1/apps/goals/calendar/pin')).status, 400);
}

console.log('An unrelated goal is described honestly...');
search = await request('GET', '/api/v1/apps/goals?q=track%20my%20parcel%20deliveries');
assert.equal(search.data.unmatched, true);
assert.equal(search.data.goals.length, 0);
assert.ok(search.data.suggestion.includes('Nothing has been connected'));

console.log('Saving the calendar link through the existing setup makes it ready...');
const secretLink = 'https://calendar.google.com/calendar/ical/me%40example.com/private-secretvalue123/basic.ics';
const install = await request('POST', '/api/v1/integrations/packs/calendar', { icsUrl: secretLink });
assert.equal(install.status, 200, install.text);
search = await request('GET', '/api/v1/apps/goals?q=calendar');
calendar = search.data.goals.find((goal) => goal.id === 'calendar');
assert.equal(calendar.status, 'ready');
assert.equal(calendar.try.label, 'What is on today?');
assert.ok(!search.text.includes('secretvalue123'), 'the saved link never appears in the answer');

console.log('Trying reads today (an honest empty list is fine); pinning works and undoes...');
const tried = await request('POST', '/api/v1/apps/goals/calendar/try');
assert.equal(tried.status, 200, tried.text);
assert.equal(tried.data.kind, 'calendar_today');
assert.ok(Array.isArray(tried.data.events));
// In this sandbox the saved link cannot be fetched, which must read as a real next step, not an empty day.
assert.ok(tried.data.problem === null || tried.data.problem.includes('could not be read'));
assert.equal((await request('PUT', '/api/v1/apps/goals/calendar/pin')).status, 204);
search = await request('GET', '/api/v1/apps/goals?q=calendar');
assert.equal(search.data.goals[0].pinned, true);
assert.equal((await request('DELETE', '/api/v1/apps/goals/calendar/pin')).status, 204);
assert.equal((await request('DELETE', '/api/v1/apps/goals/calendar/pin')).status, 404);

console.log('Mail is described with its own capabilities and sign-in step...');
search = await request('GET', '/api/v1/apps/goals?q=email');
const mail = search.data.goals.find((goal) => goal.id === 'mail');
assert.deepEqual(mail.capabilities.prepares, ['draft email']);
assert.deepEqual(mail.capabilities.changes, []);
assert.ok(mail.capabilities.needs.includes('never sees your password'));
// A server whose name contains "Mail" counts as connected (the existing pack matching), so only check the shape.
assert.ok(['ready', 'not_connected'].includes(mail.status));

console.log('OK app_goals_flow');
