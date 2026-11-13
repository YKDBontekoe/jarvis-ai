#!/usr/bin/env node
// The game: a habit with a weak weekday gets a bet against it, the week's quests are written once, a promise quest
// completes itself when the promise is kept, finishing a quest adds its points, calling off a bet voids it and Jarvis
// does not bet on the same thing again straight away, and unknown ids are 404s.
//
// The API only accepts check-ins for the last seven days, so the habit's nine weeks of history are written straight
// into Postgres: set JARVIS_E2E_PG_CONTAINER to the Postgres container (the first pgvector container by default).
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';

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
const name = `Exercise ${tag}`;

const container = process.env.JARVIS_E2E_PG_CONTAINER ??
  execFileSync('docker', ['ps', '--filter', 'ancestor=pgvector/pgvector:pg18', '--format', '{{.Names}}'])
    .toString().trim().split('\n')[0];
assert.ok(container, 'set JARVIS_E2E_PG_CONTAINER to the Postgres container name');
const password = execFileSync('docker', ['exec', container, 'printenv', 'POSTGRES_PASSWORD']).toString().trim();
const sql = (query) => execFileSync('docker', ['exec', '-e', `PGPASSWORD=${password}`, container, 'psql', '-U',
  'postgres', '-d', 'jarvis', '-At', '-v', 'ON_ERROR_STOP=1', '-c', query]).toString().trim();

console.log('Creating a daily habit done every day for nine weeks except Thursdays...');
const habit = await request('POST', '/api/v1/habits', { name, cadence: 'daily' });
assert.equal(habit.status, 201, JSON.stringify(habit.data));
const habitId = habit.data.id ?? habit.data.habit?.id;
assert.match(habitId ?? '', /^[0-9a-f-]{36}$/, JSON.stringify(habit.data));
sql(`update habits set created_at = now() - interval '70 days' where "Id" = '${habitId}'`);
sql(`insert into habit_check_ins ("Id", habit_id, owner_id, check_in_date, source, created_at)
     select gen_random_uuid(), h."Id", h.owner_id, d::date, 'app', now()
     from habits h, generate_series(current_date - 63, current_date - 1, interval '1 day') d
     where h."Id" = '${habitId}' and extract(isodow from d) <> 4
     on conflict do nothing`);

console.log('Adding an overdue promise...');
const promise = await request('POST', '/api/v1/commitments', {
  direction: 'i_owe',
  counterparty: 'Sam',
  description: `Return the drill ${tag}`,
});
assert.ok(promise.status < 300, JSON.stringify(promise.data));
const promiseId = promise.data.id ?? promise.data.value?.id;
assert.ok(promiseId, JSON.stringify(promise.data));

console.log('Reading the board...');
const first = await request('GET', '/api/v1/game');
assert.equal(first.status, 200, JSON.stringify(first.data));
const bet = first.data.bets.find((b) => b.status === 'open' && b.claim === `You’ll skip ${name} on Thursday`);
assert.ok(bet, `no Thursday bet in ${JSON.stringify(first.data.bets)}`);
assert.equal(bet.kind, 'habit_day');
assert.ok(bet.confidencePercent >= 90, JSON.stringify(bet));
assert.ok(bet.points >= 80, JSON.stringify(bet));
assert.match(bet.evidence, /of your last \d+ Thursdays/);
assert.ok(first.data.quests.length >= 1 && first.data.quests.length <= 4, JSON.stringify(first.data.quests));
assert.ok(first.data.score.level >= 1);
assert.ok(Array.isArray(first.data.ghost.metrics));
assert.ok(first.data.ghost.metrics.some((m) => m.key === 'habits'), JSON.stringify(first.data.ghost));

console.log('Reading it again writes nothing twice...');
const second = await request('GET', '/api/v1/game');
assert.deepEqual(
  second.data.quests.map((q) => q.id).sort(),
  first.data.quests.map((q) => q.id).sort(),
);
assert.equal(
  second.data.bets.filter((b) => b.status === 'open').length,
  first.data.bets.filter((b) => b.status === 'open').length,
);

const promiseQuest = second.data.quests.find((q) => q.subjectId === promiseId);
if (promiseQuest) {
  console.log('Keeping the promise completes its quest...');
  const kept = await request('PATCH', `/api/v1/commitments/${promiseId}`, { status: 'done' });
  assert.ok(kept.status < 300, JSON.stringify(kept.data));
  const after = await request('GET', '/api/v1/game');
  const quest = after.data.quests.find((q) => q.id === promiseQuest.id);
  assert.equal(quest.status, 'done', JSON.stringify(quest));
  assert.equal(after.data.score.points, second.data.score.points + promiseQuest.points);
} else {
  console.log('(The promise was not among this week’s quests; skipping its auto-complete check.)');
}

console.log('Finishing a quest by hand adds its points once...');
const before = await request('GET', '/api/v1/game');
const open = before.data.quests.find((q) => q.status === 'open');
if (open) {
  const done = await request('POST', `/api/v1/game/quests/${open.id}/complete`);
  assert.equal(done.status, 200, JSON.stringify(done.data));
  assert.equal(done.data.status, 'done');
  const again = await request('POST', `/api/v1/game/quests/${open.id}/complete`);
  assert.equal(again.status, 404, 'a finished quest cannot be finished twice');
  const after = await request('GET', '/api/v1/game');
  assert.equal(after.data.score.points, before.data.score.points + open.points);
  assert.equal(after.data.score.questsDone, before.data.score.questsDone + 1);
}

console.log('Calling off the bet voids it and Jarvis does not bet on it again right away...');
const off = await request('POST', `/api/v1/game/bets/${bet.id}/call-off`);
assert.equal(off.status, 200, JSON.stringify(off.data));
assert.equal(off.data.status, 'void');
const refreshed = await request('POST', '/api/v1/game/refresh');
assert.equal(refreshed.status, 200, JSON.stringify(refreshed.data));
const afterOff = await request('GET', '/api/v1/game');
assert.ok(
  !afterOff.data.bets.some((b) => b.status === 'open' && b.claim.includes(name)),
  JSON.stringify(afterOff.data.bets),
);
assert.equal((await request('POST', `/api/v1/game/bets/${bet.id}/call-off`)).status, 404);

console.log('Unknown ids are 404s...');
const missing = '019a0000-0000-7000-8000-000000000000';
assert.equal((await request('POST', `/api/v1/game/quests/${missing}/complete`)).status, 404);
assert.equal((await request('POST', `/api/v1/game/quests/${missing}/skip`)).status, 404);
assert.equal((await request('POST', `/api/v1/game/bets/${missing}/call-off`)).status, 404);

await request('DELETE', `/api/v1/habits/${habitId}`);
console.log('Game flow passed.');
