#!/usr/bin/env node
// A skill whose runs mostly went badly is redrafted as a proposal; accepting rewrites it, undo puts the old text back,
// and the skills API reports how its runs went. Run against a disposable Development API on fake_codex_app_server.mjs.
// Runs are seeded straight into turn_traces and skills (a real failing skill is not available in the fixture), so set
// JARVIS_E2E_PG_CONTAINER to the Postgres container, as ask_file_flow.mjs does.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';

const base = (process.env.JARVIS_API_URL ?? 'http://localhost:5082') + '/api/v1';
const container = process.env.JARVIS_E2E_PG_CONTAINER;
assert.ok(container, 'set JARVIS_E2E_PG_CONTAINER to the Postgres container name');
const name = `trip-planning-${randomUUID().slice(0, 6)}`;
const original = '1. Ask for dates and budget.';

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

const conversation = await request('POST', '/conversations', { title: 'Skill fixture' });
const owner = sql(`select "OwnerId" from conversations where "Id" = '${conversation.id}'`);
const skillId = randomUUID();
sql(`insert into skills (id, owner_id, name, description, instructions, source, status, is_locked, version, use_count, ` +
  `created_at, updated_at) values ('${skillId}', '${owner}', '${name}', 'Use when planning trips.', '${original}', ` +
  `'learned', 'active', false, 1, 0, now() - interval '5 days', now() - interval '5 days')`);
sql(`insert into skill_revisions ("Id", skill_id, version, description, instructions, source, created_at) values ` +
  `('${randomUUID()}', '${skillId}', 1, 'Use when planning trips.', '${original}', 'learned', now() - interval '5 days')`);

// Five runs that loaded the skill: one went well, four had a failing tool.
const runs = [[30, 'completed'], [29, 'failed'], [28, 'failed'], [27, 'failed'], [26, 'failed']];
for (const [hours, tool] of runs)
  sql(`insert into turn_traces ("Id", owner_id, conversation_id, kind, memory_ids_json, skills_json, tools_json, ` +
    `lesson_ids_json, total_ms, outcome, created_at) values ('${randomUUID()}', '${owner}', '${conversation.id}', ` +
    `'interactive', '[]', '["${name}"]', '[{"tool":"FixtureSearch","outcome":"${tool === 'completed' ? 'completed' : 'failed'}","ms":40}]', ` +
    `'[]', 200, 'completed', now() - interval '${hours} hours')`);

try {
  console.log('The nightly pass counts the runs and offers a redraft...');
  const mined = await request('POST', '/improvements/refresh');
  assert.equal(mined.refinements, 1, JSON.stringify(mined));
  const skill = (await request('GET', '/skills')).find(item => item.id === skillId);
  assert.deepEqual([skill.outcomeRuns, skill.outcomeSuccesses], [5, 1]);
  const listed = await request('GET', '/improvements');
  const proposal = listed.find(item => item.kind === 'skill_refine' && item.title.includes(name));
  assert.ok(proposal, `no refinement for ${name}: ${JSON.stringify(listed).slice(0, 300)}`);
  assert.equal(proposal.status, 'pending');
  assert.match(proposal.evidence, /1 of 5/);
  assert.match(proposal.evidence, /FixtureSearch ×4/);

  console.log('Nothing changed until the owner accepts...');
  assert.equal(sql(`select instructions from skills where id = '${skillId}'`), original);
  assert.equal((await request('POST', '/improvements/refresh')).refinements, 0);

  console.log('Accepting rewrites the skill and starts its counts over...');
  const accepted = await request('POST', `/improvements/${proposal.id}/accept`);
  assert.equal(accepted.status, 'accepted');
  const rewritten = (await request('GET', '/skills')).find(item => item.id === skillId);
  assert.equal(rewritten.version, 2);
  assert.equal(rewritten.status, 'active');
  assert.match(rewritten.instructions, /Check the inputs first/);
  assert.deepEqual([rewritten.outcomeRuns, rewritten.outcomeSuccesses], [0, 0]);

  console.log('Undo puts the old text back as a new revision...');
  await request('POST', `/improvements/${proposal.id}/undo`);
  const restored = (await request('GET', '/skills')).find(item => item.id === skillId);
  assert.equal(restored.instructions, original);
  assert.equal(restored.version, 3);

  console.log(JSON.stringify({ passed: true, skill: name, runs: skill.outcomeRuns, wentWell: skill.outcomeSuccesses }));
} finally {
  sql(`delete from turn_traces where conversation_id = '${conversation.id}'`);
  sql(`delete from skills where id = '${skillId}'`);
}
