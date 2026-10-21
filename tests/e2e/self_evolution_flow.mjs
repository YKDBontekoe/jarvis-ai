#!/usr/bin/env node
// A capability Jarvis lacks in two conversations is reported as a bare label, mined into a proposal, picked up by the
// unattended builder once the owner allows it (the fixture model judges it not feasible, since the runner has no
// network here), and accepting it records that the owner wants it. Run against the smoke stack; set
// JARVIS_E2E_PG_CONTAINER so the run can clean up the proposal it made.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';

const base = (process.env.JARVIS_API_URL ?? 'http://localhost:5082') + '/api/v1';
const container = process.env.JARVIS_E2E_PG_CONTAINER;
assert.ok(container, 'set JARVIS_E2E_PG_CONTAINER to the Postgres container name');
const fingerprint = 'gap:live-train-departures';

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
const cleanUp = () => {
  sql(`delete from improvement_proposals where fingerprint = '${fingerprint}'`);
  sql(`delete from learning_signals where kind = 'capability_gap' and category = 'live-train-departures'`);
};

const original = await request('GET', '/settings/learning');
cleanUp();
try {
  console.log('Asking for something no tool can do, in two conversations...');
  const conversations = [];
  for (const title of ['Trains on Monday', 'Trains on Friday']) {
    const conversation = await request('POST', '/conversations', { title });
    conversations.push(conversation.id);
    const reply = await request('POST', `/conversations/${conversation.id}/messages`,
      { content: 'Can you show me live train departures from Utrecht?' });
    assert.match(JSON.stringify(reply), /can't look up live train departures/i);
  }

  const reported = () => Number(sql(`select count(*) from learning_signals where kind = 'capability_gap' ` +
    `and category = 'live-train-departures' and conversation_id in ('${conversations.join("','")}')`));
  for (let wait = 0; wait < 20 && reported() < 2; wait++) await new Promise(resolve => setTimeout(resolve, 500));
  assert.equal(reported(), 2, 'one gap signal per conversation');
  const stored = sql(`select coalesce(tool, '') || '|' || category from learning_signals where kind = 'capability_gap' ` +
    `and conversation_id = '${conversations[0]}'`);
  assert.equal(stored, '|live-train-departures', 'only the label is kept, never the request');

  console.log('The nightly miner offers it...');
  const mined = await request('POST', '/improvements/refresh');
  assert.equal(mined.capabilities, 1, JSON.stringify(mined));
  let gap = (await request('GET', '/improvements')).find(item => item.kind === 'capability');
  assert.ok(gap, 'a capability proposal');
  assert.equal(gap.title, 'Jarvis keeps lacking: live train departures');
  assert.equal(gap.acceptLabel, 'I want this');

  console.log('Home lists it under Needs you...');
  const waiting = (await request('GET', '/home')).learning?.waiting ?? [];
  const row = waiting.find(item => item.id === gap.id);
  assert.ok(row, `the gap on Home: ${JSON.stringify(waiting)}`);
  assert.equal(row.title, 'Jarvis keeps lacking live train departures');
  assert.equal(row.acceptLabel, 'I want this');

  console.log('Nothing is built while the owner has not allowed it...');
  assert.equal((await request('POST', '/improvements/evolve')).built, 0);
  assert.equal(sql(`select payload_json::jsonb ->> 'attempts' from improvement_proposals where fingerprint = '${fingerprint}'`), '0');

  console.log('With autonomous evolution on, the builder takes the gap...');
  await request('PUT', '/settings/learning', { ...original, autonomousEvolution: true });
  assert.equal((await request('POST', '/improvements/evolve')).built, 0);
  const payload = JSON.parse(sql(`select payload_json from improvement_proposals where fingerprint = '${fingerprint}'`));
  assert.equal(payload.buildStatus, 'not_feasible', JSON.stringify(payload));
  assert.equal(payload.attempts, 1);
  gap = (await request('GET', '/improvements')).find(item => item.kind === 'capability');
  assert.match(gap.evidence, /could not build an app for it/);

  console.log('Accepting records that the owner wants it...');
  const accepted = await request('POST', `/improvements/${gap.id}/accept`);
  assert.equal(accepted.status, 'accepted');

  console.log(JSON.stringify({ passed: true, evidence: gap.evidence.slice(0, 120) }));
} finally {
  await request('PUT', '/settings/learning', original);
  cleanUp();
}
