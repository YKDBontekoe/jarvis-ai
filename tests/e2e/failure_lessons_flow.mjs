#!/usr/bin/env node
// A tool that kept failing becomes a lesson, the lesson is shown in a later turn and recorded on its trace, and undoing
// it stops it being shown. Run against a disposable Development API on fake_codex_app_server.mjs.
// Failing turns are seeded straight into turn_traces (a real failing tool is not available in the fixture), so set
// JARVIS_E2E_PG_CONTAINER to the Postgres container, as ask_file_flow.mjs does.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';

const base = (process.env.JARVIS_API_URL ?? 'http://localhost:5082') + '/api/v1';
const container = process.env.JARVIS_E2E_PG_CONTAINER;
assert.ok(container, 'set JARVIS_E2E_PG_CONTAINER to the Postgres container name');
const tool = `FixtureSearch${randomUUID().slice(0, 6)}`;

async function request(method, path, body) {
  const response = await fetch(base + path, {
    method, headers: body ? { 'Content-Type': 'application/json' } : {},
    body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(240_000),
  });
  const text = await response.text();
  assert(response.ok, `${method} ${path}: ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

const password = execFileSync('docker', ['exec', container, 'printenv', 'POSTGRES_PASSWORD']).toString().trim();
const sql = query => execFileSync('docker', ['exec', '-e', `PGPASSWORD=${password}`, container, 'psql', '-U', 'postgres',
  '-d', 'jarvis', '-At', '-c', query]).toString().trim();

const conversation = await request('POST', '/conversations', { title: 'Lesson fixture' });
const owner = sql(`select "OwnerId" from conversations where "Id" = '${conversation.id}'`);
assert.match(owner, /^[0-9a-f-]{36}$/, `owner of the conversation: ${owner}`);

// The first model call after a cold start can outlast the drafter's timeout, which stores nothing; warm it up.
await request('POST', `/conversations/${conversation.id}/messages`, { content: 'Hello Jarvis' });

const failures = [30, 26, 22];
for (const hours of failures)
  sql(`insert into turn_traces ("Id", owner_id, conversation_id, kind, memory_ids_json, skills_json, tools_json, ` +
    `lesson_ids_json, total_ms, outcome, created_at) values ('${randomUUID()}', '${owner}', '${conversation.id}', ` +
    `'interactive', '[]', '[]', '[{"tool":"${tool}","outcome":"failed","ms":40}]', '[]', 200, 'completed', ` +
    `now() - interval '${hours} hours')`);

try {
  console.log('Three failing turns become one lesson, put in force at once...');
  const mined = await request('POST', '/improvements/refresh');
  assert.equal(mined.lessons, 1, `first refresh: ${JSON.stringify(mined)}`);
  const listed = await request('GET', '/improvements');
  const lesson = listed.find(item => item.kind === 'lesson' && item.title.includes(tool));
  assert.ok(lesson, `no lesson for ${tool}: ${JSON.stringify(listed).slice(0, 300)}`);
  assert.equal(lesson.status, 'applied');
  assert.equal(lesson.canUndo, true);

  console.log('A second refresh in the same month adds nothing...');
  const again = await request('POST', '/improvements/refresh');
  assert.equal(again.lessons, 0, `second refresh: ${JSON.stringify(again)}`);

  console.log('A later turn is shown the lesson and its trace records the id...');
  await request('POST', `/conversations/${conversation.id}/messages`, { content: 'Hello Jarvis, are you there?' });
  const shown = () => Number(sql(`select count(*) from turn_traces where conversation_id = '${conversation.id}' ` +
    `and lesson_ids_json @> '["${lesson.id}"]'::jsonb`));
  for (let wait = 0; wait < 20 && shown() === 0; wait++) await new Promise(resolve => setTimeout(resolve, 500));
  const afterFirst = shown();
  assert.equal(afterFirst, 1, `expected exactly one trace that showed the lesson, saw ${afterFirst}`);

  console.log('Undoing the lesson stops it being shown...');
  await request('POST', `/improvements/${lesson.id}/undo`);
  await request('POST', `/conversations/${conversation.id}/messages`, { content: 'And one more hello' });
  await new Promise(resolve => setTimeout(resolve, 1500));
  const afterUndo = shown();
  assert.equal(afterUndo, 1, `the undone lesson must not appear on a new trace, saw ${afterUndo}`);
  const after = await request('GET', '/improvements');
  assert.ok(!after.some(item => item.id === lesson.id && item.canUndo), 'an undone lesson cannot be undone again');

  console.log(JSON.stringify({ passed: true, tool, lesson: lesson.title.slice(0, 80) }));
} finally {
  sql(`delete from turn_traces where conversation_id = '${conversation.id}'`);
  // A run that stopped half way must not leave a lesson in force for the next flow.
  sql(`delete from improvement_proposals where kind = 'lesson' and fingerprint like 'lesson:tool:${tool}:%'`);
}
