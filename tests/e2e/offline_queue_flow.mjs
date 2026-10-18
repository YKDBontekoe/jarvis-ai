#!/usr/bin/env node
// What the offline queue relies on: replaying a queued change is safe. Completing a reminder twice and checking in
// the same habit day twice each end in one outcome, an undo replays as the desired state, and a deleted target is a
// clear 404 (the app shows it as "could not apply", never as done).
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
const today = new Date().toISOString().slice(0, 10);

console.log('Reminder complete is idempotent...');
const reminder = await request('POST', '/reminders', {
  title: `Queue check ${tag}`,
  dueAt: new Date(Date.now() + 86400000).toISOString(),
});
assert.ok([200, 201].includes(reminder.status), reminder.text);
const first = await request('POST', `/reminders/${reminder.data.id}/complete`);
const second = await request('POST', `/reminders/${reminder.data.id}/complete`);
assert.equal(first.status, 200, first.text);
assert.equal(second.status, 200, second.text);
assert.equal(first.data.status, second.data.status);

console.log('Habit check-in replays as the desired state...');
const habit = await request('POST', '/habits', { name: `Queue walk ${tag}`, icon: 'walk', cadence: 'daily' });
assert.ok([200, 201].includes(habit.status), habit.text);
const habitId = habit.data.id;
const check1 = await request('POST', `/habits/${habitId}/check-ins`, { date: today, done: true });
const check2 = await request('POST', `/habits/${habitId}/check-ins`, { date: today, done: true });
assert.equal(check1.status, 200, check1.text);
assert.equal(check2.status, 200, check2.text);
assert.equal(check1.data.stats.totalCheckIns, 1);
assert.equal(check2.data.stats.totalCheckIns, 1, 'the second replay did not count twice');
assert.equal(check2.data.stats.doneToday, true);
const undo = await request('POST', `/habits/${habitId}/check-ins`, { date: today, done: false });
assert.equal(undo.status, 200, undo.text);
assert.equal(undo.data.stats.doneToday, false);
assert.equal(undo.data.stats.totalCheckIns, 0);

console.log('A deleted target is a clear 404...');
const cancelled = await request('POST', '/reminders', {
  title: `Queue cancelled ${tag}`,
  dueAt: new Date(Date.now() + 86400000).toISOString(),
});
assert.ok([200, 204].includes((await request('DELETE', `/reminders/${cancelled.data.id}`)).status));
assert.equal((await request('POST', `/reminders/${cancelled.data.id}/complete`)).status, 404);
await request('DELETE', `/habits/${habitId}`);
assert.equal((await request('POST', `/habits/${habitId}/check-ins`, { date: today, done: true })).status, 404);
console.log('offline_queue_flow ok');
