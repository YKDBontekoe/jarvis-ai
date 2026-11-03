// Extended checks against the local verification stack (scripts/dev/smoke.sh up: the AppHost with
// JARVIS_FEATURES=verification, fake Codex and fake MCP). It covers what local_fixture_flow.mjs does not:
// bearer-authenticated SignalR and owner isolation, an MCP write from a background task that waits for approval,
// MCP self-management, an approved coding task in an isolated snapshot, condition-watch delivery through Temporal,
// and antivirus rejection on upload. Run it with `scripts/dev/smoke.sh test extended_flow.mjs`.
//
// The stack runs in Development, where an anonymous request is the development owner. Every check here instead
// signs in as two accounts registered for this run, so the bearer path and owner isolation are what is tested.
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

const origin = process.env.JARVIS_API_URL ?? 'http://localhost:5082';
const base = origin + '/api/v1';
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
// The fake MCP server runs as a child of the local API and worker processes and appends each call here.
const mcpCallLog = process.env.JARVIS_VERIFICATION_MCP_CALLS ?? '/tmp/jarvis-verification-mcp-calls.jsonl';
// The AppHost verification feature registers this checkout as the coding repository named "verification".
const codingFixture = join(repoRoot, 'artifacts/verification/coding/fixture');
const codingWorktrees = join(repoRoot, 'artifacts/verification/coding/.jarvis-worktrees/verification');
const selected = process.env.JARVIS_EXTENDED_CASES?.split(',');
const results = [];

// Two throwaway accounts per run: the data volume persists between runs, so fixed emails would collide.
const run = randomBytes(4).toString('hex');
const password = randomBytes(18).toString('base64url') + 'Aa1';
const accounts = { a: `extended-a-${run}@example.invalid`, b: `extended-b-${run}@example.invalid` };
const tokens = {};
async function token(account = 'a') {
  if (tokens[account]) return tokens[account];
  const response = await fetch(base + '/auth/register', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: accounts[account], password }),
  });
  assert.equal(response.status, 200, `register ${account}: ${response.status} ${await response.clone().text()}`);
  tokens[account] = (await response.json()).accessToken;
  return tokens[account];
}

async function request(method, path, data, statuses = [200], account = 'a') {
  const response = await fetch(base + path, {
    method,
    headers: { Authorization: 'Bearer ' + await token(account), ...(data ? { 'Content-Type': 'application/json' } : {}) },
    body: data ? JSON.stringify(data) : undefined, signal: AbortSignal.timeout(120_000),
  });
  const text = await response.text();
  assert(statuses.includes(response.status), `${method} ${path}: ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

async function wait(path, predicate, seconds = 120) {
  let last;
  for (const until = Date.now() + seconds * 1000; Date.now() < until;) {
    last = await request('GET', path);
    if (predicate(last)) return last;
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw Error(`Timed out waiting for ${path}: ${JSON.stringify(last).slice(0, 300)}`);
}

function mcpCalls() {
  if (!existsSync(mcpCallLog)) return [];
  return readFileSync(mcpCallLog, 'utf8').trim().split('\n').filter(Boolean).map(JSON.parse);
}

async function check(name, action) {
  if (selected && !selected.includes(name)) return;
  const started = Date.now();
  try {
    const evidence = await action();
    results.push({ name, status: 'passed', ms: Date.now() - started, evidence });
    console.log(`PASS ${name}` + (evidence ? ` — ${JSON.stringify(evidence).slice(0, 200)}` : ''));
  } catch (error) {
    results.push({ name, status: 'failed', error: error.message });
    console.log(`FAIL ${name}: ${error.message}`);
  }
}

const conversation = title => request('POST', '/conversations', { title }, [201]);
async function propose(conversationId, content, toolName) {
  const pending = await request('POST', `/conversations/${conversationId}/messages`, { content }, [202]);
  assert.equal(pending.length, 1, JSON.stringify(pending));
  assert.equal(pending[0].toolName, toolName);
  return pending[0];
}
const approve = item => request('POST', `/approvals/${item.id}/decision`, { approved: true });
const hubFor = account => new HubConnectionBuilder()
  .withUrl(origin + '/hubs/events', { accessTokenFactory: () => token(account) })
  .configureLogging(LogLevel.Error).build();

// The verification MCP server takes a credential; storing it lets the API and worker connect it for account A.
await request('PUT', '/integrations/verification/credentials/token', { value: 'synthetic-mcp-fixture-token' }, [200, 204]);

await check('authenticated-signalr-stream-and-owner-isolation', async () => {
  const item = await conversation('Authenticated SignalR verification');
  const hub = hubFor('a');
  const events = [];
  for (const name of ['message.delta', 'message.completed']) hub.on(name, value => { events.push({ name, value }); });
  await hub.start();
  try {
    await hub.invoke('JoinConversation', item.id);
    const reply = await request('POST', `/conversations/${item.id}/messages`, { content: 'Hello Jarvis' });
    assert.match(reply.content, /I'm Jarvis/);
    assert(events.some(event => event.name === 'message.delta'), 'no streamed deltas over the authenticated hub');
    assert(events.some(event => event.name === 'message.completed'), 'no completion over the authenticated hub');
  } finally { await hub.stop(); }

  const other = hubFor('b');
  await other.start();
  try {
    await assert.rejects(other.invoke('JoinConversation', item.id), /Conversation not found/);
  } finally { await other.stop(); }
  await request('GET', `/conversations/${item.id}`, undefined, [404], 'b');
  assert(!(await request('GET', '/conversations', undefined, [200], 'b')).some(x => x.id === item.id));
  // An anonymous request is the development owner here, which is a third owner.
  const anonymous = await fetch(`${base}/conversations/${item.id}`);
  assert.equal(anonymous.status, 404);
  return { conversationId: item.id, deltas: events.filter(e => e.name === 'message.delta').length,
    crossOwnerJoinRejected: true, crossOwnerReadRejected: true };
});

await check('background-tool-approval-across-worker-and-api', async () => {
  const before = mcpCalls().length;
  const item = await request('POST', '/tasks', { title: 'Background MCP approval verification',
    prompt: "Create a GitHub issue titled 'Background approval verification' in the configured verification/example repository." }, [201]);
  const waiting = await wait('/tasks/' + item.id, x => ['needs_approval', 'failed', 'completed'].includes(x.status));
  assert.equal(waiting.status, 'needs_approval', JSON.stringify(waiting));
  assert.equal(mcpCalls().length, before, 'The worker ran the MCP write before approval');
  const approval = (await request('GET', '/approvals')).find(x => x.conversationId === item.conversationId);
  assert(approval && approval.toolName === 'github_create_issue', JSON.stringify(approval));
  const decided = await approve(approval);
  const completed = await wait('/tasks/' + item.id, x => ['completed', 'failed'].includes(x.status));
  assert.equal(completed.status, 'completed', JSON.stringify(completed));
  const writes = mcpCalls().slice(before).filter(x => x.tool === 'github_create_issue');
  assert.equal(writes.length, 1);
  assert.equal(writes[0].arguments.title, 'Background approval verification');
  assert.match(decided.content, /Created issue #42/);
  await wait('/notifications', values => values.some(x => x.sourceId === item.id && x.type === 'task.completed'));
  return { taskId: item.id, approvalId: approval.id, externalWrites: writes.length, notificationDelivered: true };
});

await check('agent-manages-mcp-registration-update-removal', async () => {
  const item = await conversation('MCP self-management verification');
  const added = await propose(item.id,
    'Register the MCP server Verification Registry at https://example.com/mcp with only search_docs enabled.', 'AddMcpServer');
  assert(!(await request('GET', '/mcp-servers')).some(x => x.name === 'Verification Registry'), 'Registered before approval');
  await approve(added);
  const server = (await request('GET', '/mcp-servers')).find(x => x.name === 'Verification Registry');
  assert(server, 'Not registered after approval');
  try {
    assert.deepEqual(server.allowedTools, ['search_docs']);
    await approve(await propose(item.id,
      'Rename the MCP server Verification Registry to Verification Registry Updated.', 'UpdateMcpServer'));
    const current = (await request('GET', '/mcp-servers')).find(x => x.id === server.id);
    assert.equal(current.name, 'Verification Registry Updated');
    assert.equal(current.endpoint, server.endpoint);
    assert.deepEqual(current.allowedTools, server.allowedTools);
    await approve(await propose(item.id, 'Remove the MCP server Verification Registry Updated.', 'RemoveMcpServer'));
    assert(!(await request('GET', '/mcp-servers')).some(x => x.id === server.id), 'Still registered after removal');
    return { serverId: server.id, registrationApproved: true, updateApproved: true, removalApproved: true };
  } finally {
    if ((await request('GET', '/mcp-servers')).some(x => x.id === server.id))
      await request('DELETE', '/mcp-servers/' + server.id, undefined, [204]);
  }
});

await check('coding-approval-and-isolated-worktree', async () => {
  if (!existsSync(join(codingFixture, '.git'))) {
    mkdirSync(codingFixture, { recursive: true });
    writeFileSync(join(codingFixture, 'README.md'), 'Coding fixture for tests/e2e/extended_flow.mjs.\n');
    const git = args => execFileSync('git', args, { cwd: codingFixture, stdio: 'pipe' });
    git(['init', '--quiet']);
    git(['add', 'README.md']);
    git(['-c', 'user.name=Jarvis fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'commit.gpgsign=false',
      'commit', '--quiet', '-m', 'Fixture']);
  }
  const listWorktrees = () => existsSync(codingWorktrees)
    ? readdirSync(codingWorktrees).filter(name => !name.endsWith('.txt')) : [];
  const before = listWorktrees();
  const item = await conversation('Coding verification');
  const approval = await propose(item.id,
    'Use the configured coding repository named verification. Create a file verified.txt containing exactly CODING_FLOW_VERIFIED. Leave it uncommitted. Do not run tests.',
    'RunCodingTask');
  assert.deepEqual(listWorktrees(), before, 'Coding ran before approval');
  const decided = await approve(approval);
  const fresh = listWorktrees().filter(name => !before.includes(name));
  assert.equal(fresh.length, 1, decided.content);
  const file = join(codingWorktrees, fresh[0], 'verified.txt');
  assert.equal(readFileSync(file, 'utf8').trim(), 'CODING_FLOW_VERIFIED');
  assert(!existsSync(join(codingFixture, 'verified.txt')), 'The source checkout was modified');
  const status = execFileSync('git', ['status', '--short'], { cwd: join(codingWorktrees, fresh[0]), encoding: 'utf8' });
  assert.match(status, /\?\? verified\.txt/, 'The change should be left uncommitted');
  const run = (await request('GET', '/coding/runs')).find(x => x.worktreePath?.endsWith(fresh[0]));
  assert(run && run.status === 'completed', JSON.stringify(run));
  const audit = (await request('GET', '/audit')).find(x => x.action === 'coding_task.completed' && x.success);
  assert(audit, decided.content);
  return { approvalId: approval.id, runId: run.id, sourceCheckoutUnchanged: true, auditEventId: audit.id };
});

await check('temporal-condition-watch-delivery', async () => {
  // A device battery watch reads the owner's last telemetry snapshot, so Temporal runs the whole watch workflow
  // without the network. (JSON watches only fetch public HTTPS hosts, which a local fixture cannot be.)
  await request('POST', '/devices/telemetry', { batteryPercent: 12, charging: false }, [200, 204]);
  const item = await request('POST', '/watches', { title: 'Battery verification', url: '', jsonPath: '',
    kind: 'device_battery', comparison: 'below', threshold: 20, intervalMinutes: 5 }, [201]);
  const completed = await wait('/watches/' + item.id, x => ['triggered', 'failed'].includes(x.status));
  assert.equal(completed.status, 'triggered', JSON.stringify(completed));
  assert.equal(completed.lastValue, 12);
  await wait('/notifications', values => values.some(x => x.sourceId === item.id));
  return { watchId: item.id, observedValue: completed.lastValue, notificationDelivered: true };
});

await check('malware-rejection-before-file-storage', async () => {
  const body = new FormData();
  const signature = 'X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*';
  body.append('file', new Blob([signature], { type: 'text/plain' }), 'synthetic-antivirus-test.txt');
  const response = await fetch(base + '/files', { method: 'POST', body, headers: { Authorization: 'Bearer ' + await token() } });
  assert.equal(response.status, 422, await response.text());
  assert(!(await request('GET', '/files')).some(x => x.fileName === 'synthetic-antivirus-test.txt'));
  return { status: response.status, fileStored: false };
});

const failed = results.filter(result => result.status === 'failed');
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exitCode = failed.length ? 1 : 0;
