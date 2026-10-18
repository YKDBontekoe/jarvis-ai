#!/usr/bin/env node
// Notification delivery truth: a due reminder's notification explains what happened step by step. With no phone
// registered nothing is claimed as pushed; with one registered but push delivery not configured (as in this stack)
// the push is honestly "waiting", never "sent"; opening it records "read". The summary time is validated and
// clearable; another owner's id is a 404.
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

async function dueReminderNotification(title) {
  const created = await request('POST', '/reminders', { title, dueAt: new Date(Date.now() + 4000).toISOString() });
  assert.equal(created.status, 201, created.text);
  for (let i = 0; i < 45; i += 1) {
    const list = await request('GET', '/notifications');
    const hit = list.data.find((n) => n.type === 'reminder.due' && n.title.includes(title));
    if (hit) return hit;
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  assert.fail('the reminder notification never arrived');
}

console.log('With no phone registered...');
const first = await dueReminderNotification(`First ${tag}`);
const steps = (await request('GET', `/notifications/${first.id}/delivery`)).data.steps;
assert.deepEqual(steps.map((s) => s.state), ['created', 'not_pushed', 'unread']);
assert.match(steps[1].text, /No phone was registered/);

console.log('With a phone registered but push not configured...');
assert.equal((await request('PUT', '/push-devices', { token: `token-${tag}`, platform: 'ios' })).status, 200);
const second = await dueReminderNotification(`Second ${tag}`);
const waiting = (await request('GET', `/notifications/${second.id}/delivery`)).data.steps;
assert.equal(waiting[1].state, 'waiting', 'a push nobody sent is never called sent');
assert.equal(waiting[2].state, 'unread');

console.log('Opening it is recorded as read...');
assert.equal((await request('POST', `/notifications/${second.id}/read`)).status, 204);
const read = (await request('GET', `/notifications/${second.id}/delivery`)).data.steps;
assert.equal(read[2].state, 'read');
assert.ok(read[2].at);

console.log('Summary time and unknown ids...');
assert.equal((await request('PUT', '/notifications/digest', { time: '25:00' })).status, 400);
const set = await request('PUT', '/notifications/digest', { time: '08:30' });
assert.equal(set.data.time, '08:30');
assert.match(set.data.explanation, /once a day, at 08:30/);
assert.equal((await request('PUT', '/notifications/digest', { time: '' })).data.time, null);
assert.equal((await request('GET', '/notifications/00000000-0000-0000-0000-000000000000/delivery')).status, 404);
console.log('notification delivery flow OK');
