import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, readdirSync, existsSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

const fixture = JSON.parse(readFileSync('artifacts/verification/identity-fixture.json'));
const base = 'http://localhost:5082/api/v1';
const reportPath = 'artifacts/verification/extended-flow.json';
const selected = process.env.JARVIS_EXTENDED_CASES?.split(',');
const checks = selected && existsSync(reportPath) ? JSON.parse(readFileSync(reportPath)).checks : [];
let access, expiresAt = 0;
async function token(username = fixture.username) {
  if (username === fixture.username && expiresAt > Date.now() + 30000) return access;
  const response = await fetch(fixture.issuer + '/protocol/openid-connect/token', {
    method: 'POST', body: new URLSearchParams({ grant_type: 'password', client_id: fixture.clientId,
      username, password: fixture.password, scope: 'openid profile' }),
  });
  assert.equal(response.status, 200);
  const result = await response.json();
  if (username === fixture.username) { access = result.access_token; expiresAt = Date.now() + result.expires_in * 1000; }
  return result.access_token;
}
async function request(method, path, data, statuses = [200]) {
  const response = await fetch(base + path, { method, headers: { Authorization: 'Bearer ' + await token(),
    ...(data ? { 'Content-Type': 'application/json' } : {}) },
    body: data ? JSON.stringify(data) : undefined, signal: AbortSignal.timeout(240000) });
  const text = await response.text();
  assert(statuses.includes(response.status), `${method} ${path}: ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}
async function wait(path, predicate, seconds = 180) {
  let result;
  for (const until = Date.now() + seconds * 1000; Date.now() < until;) {
    result = await request('GET', path);
    if (predicate(result)) return result;
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw Error('Timed out: ' + path + ' ' + JSON.stringify(result));
}
function mcpCalls() {
  return execFileSync('docker', ['exec', 'jarvis-verification-jarvis-api-1', 'node', '-e',
    "try{process.stdout.write(require('fs').readFileSync('/tmp/jarvis-verification-mcp-calls.jsonl','utf8'))}catch{}"],
    { encoding: 'utf8' }).trim().split('\n').filter(Boolean).map(JSON.parse);
}
async function record(name, action) {
  if (selected && !selected.includes(name)) return;
  const oldIndex = checks.findIndex(x => x.name === name);
  const previous = oldIndex >= 0 ? checks.splice(oldIndex, 1)[0] : null;
  const started = Date.now();
  try { checks.push({ name, status: 'passed', evidence: await action() }); }
  catch (error) { checks.push({ name, status: 'failed', error: error.message }); }
  checks.at(-1).durationSeconds = (Date.now() - started) / 1000;
  if (previous) checks.at(-1).previousAttempts = [...(previous.previousAttempts ?? []),
    { status: previous.status, error: previous.error, durationSeconds: previous.durationSeconds }];
  writeFileSync(reportPath, JSON.stringify({ environment: 'Production', checks }, null, 2) + '\n');
  console.log(checks.at(-1).status.toUpperCase() + ': ' + name + (checks.at(-1).error ? ': ' + checks.at(-1).error : ''));
}
async function conversation(title) { return request('POST', '/conversations', { title }, [201]); }
async function propose(conversationId, prompt, name) {
  const result = await request('POST', `/conversations/${conversationId}/messages`, { content: prompt }, [202]);
  assert.equal(result.length, 1);
  assert.equal(result[0].toolName, name);
  return result[0];
}
async function approve(item) { return request('POST', `/approvals/${item.id}/decision`, { approved: true }); }

await request('PUT', '/integrations/verification/credentials/token', { value: 'synthetic-mcp-fixture-token' }, [200, 204]);
await record('authenticated-signalr-stream-and-owner-isolation', async () => {
  const item = await conversation('Authenticated SignalR verification');
  const hub = new HubConnectionBuilder().withUrl('http://localhost:5082/hubs/events', { accessTokenFactory: token })
    .configureLogging(LogLevel.Error).build();
  const events = [];
  for (const name of ['message.delta', 'message.completed', 'agent.completed']) hub.on(name, value => { events.push({ name, value }); });
  try {
    await hub.start();
    await hub.invoke('JoinConversation', item.id);
    const result = await request('POST', `/conversations/${item.id}/messages`, { content: 'Reply with exactly this text: AUTHENTICATED_STREAM_VERIFIED' });
    assert.equal(result.content.trim(), 'AUTHENTICATED_STREAM_VERIFIED');
    assert(events.some(e => e.name === 'message.delta'));
    assert(events.some(e => e.name === 'message.completed'));
    const otherHub = new HubConnectionBuilder().withUrl('http://localhost:5082/hubs/events', {
      accessTokenFactory: () => token('jarvis-test-b'),
    }).configureLogging(LogLevel.Error).build();
    try {
      await otherHub.start();
      await assert.rejects(otherHub.invoke('JoinConversation', item.id), /Conversation not found/);
    } finally { await otherHub.stop(); }
    return { conversationId: item.id, deltas: events.filter(e => e.name === 'message.delta').length,
      completionReceived: true, crossOwnerJoinRejected: true };
  } finally { await hub.stop(); }
});
await record('background-tool-approval-across-worker-and-api', async () => {
  const before = mcpCalls().length;
  const item = await request('POST', '/tasks', { title: 'Background MCP approval verification',
    prompt: "Create a GitHub issue titled 'Background approval verification' in the configured verification/example repository." }, [201]);
  const waiting = await wait('/tasks/' + item.id, x => ['needs_approval', 'failed', 'completed'].includes(x.status));
  assert.equal(waiting.status, 'needs_approval');
  assert.equal(mcpCalls().length, before, 'Unapproved background write executed');
  const approvals = await request('GET', '/approvals');
  const approval = approvals.find(x => x.conversationId === item.conversationId);
  assert(approval && approval.toolName === 'github_create_issue');
  const result = await approve(approval);
  const completed = await wait('/tasks/' + item.id, x => x.status === 'completed');
  assert.equal(mcpCalls().slice(before).filter(x => x.tool === 'github_create_issue').length, 1);
  assert(/created|42/i.test(result.content));
  await wait('/notifications', values => values.some(x => x.sourceId === item.id && x.type === 'task.completed'));
  return { taskId: item.id, approvalId: approval.id, status: completed.status, externalWrites: 1, notificationDelivered: true };
});
await record('agent-manages-mcp-registration-update-removal', async () => {
  const item = await conversation('MCP self-management verification');
  const added = await propose(item.id, "I already inspected this verification MCP registration fixture and selected its exact tool. Register it as Verification Registry at https://example.com/mcp with only search_docs enabled. This registration is a fixture; do not connect to or discover the endpoint. Use the registration tool now.", 'AddMcpServer');
  assert(!(await request('GET', '/mcp-servers')).some(x => x.name === 'Verification Registry'));
  await approve(added);
  const server = (await request('GET', '/mcp-servers')).find(x => x.name === 'Verification Registry');
  assert(server);
  assert.deepEqual(server.allowedTools, ['search_docs']);
  try {
    const updated = await propose(item.id, 'Rename Verification Registry to Verification Registry Updated. Preserve its endpoint and allowed tools.', 'UpdateMcpServer');
    await approve(updated);
    const current = (await request('GET', '/mcp-servers')).find(x => x.id === server.id);
    assert.equal(current.name, 'Verification Registry Updated');
    assert.equal(current.endpoint, server.endpoint);
    assert.deepEqual(current.allowedTools, server.allowedTools);
    const removed = await propose(item.id, 'Remove the MCP server Verification Registry Updated.', 'RemoveMcpServer');
    await approve(removed);
    assert(!(await request('GET', '/mcp-servers')).some(x => x.id === server.id));
    return { serverId: server.id, registrationApproved: true, updateApproved: true, removalApproved: true };
  } finally {
    if ((await request('GET', '/mcp-servers')).some(x => x.id === server.id))
      await request('DELETE', '/mcp-servers/' + server.id, undefined, [204]);
  }
});
await record('coding-approval-and-isolated-worktree', async () => {
  const item = await conversation('Coding verification');
  const codingRoot = 'artifacts/verification/coding';
  const before = existsSync(codingRoot + '/worktrees/verification') ? readdirSync(codingRoot + '/worktrees/verification') : [];
  const approval = await propose(item.id, 'Use the configured coding repository named verification. Create a file verified.txt containing exactly CODING_FLOW_VERIFIED. Leave it uncommitted. Do not run tests.', 'RunCodingTask');
  const pendingFiles = existsSync(codingRoot + '/worktrees/verification') ? readdirSync(codingRoot + '/worktrees/verification') : [];
  assert.deepEqual(pendingFiles, before, 'Coding ran before approval');
  const result = await approve(approval);
  const fresh = readdirSync(codingRoot + '/worktrees/verification').filter(name => !before.includes(name) && !name.endsWith('.txt'));
  assert.equal(fresh.length, 1, result.content);
  const path = codingRoot + '/worktrees/verification/' + fresh[0] + '/verified.txt';
  assert.equal(readFileSync(path, 'utf8').trim(), 'CODING_FLOW_VERIFIED');
  assert(!existsSync(codingRoot + '/fixture/verified.txt'), 'Source checkout was modified');
  const audit = (await request('GET', '/audit')).find(x => x.action === 'coding_task.completed' && x.success);
  assert(audit, result.content);
  return { approvalId: approval.id, worktreePath: path, sourceCheckoutUnchanged: true, auditEventId: audit.id };
});
await record('temporal-condition-watch-delivery', async () => {
  const item = await request('POST', '/watches', { title: 'Public repository metric verification',
    url: 'https://api.github.com/repos/openai/codex', jsonPath: 'stargazers_count', comparison: 'above',
    threshold: 0, intervalMinutes: 5 }, [201]);
  const completed = await wait('/watches/' + item.id, x => ['triggered', 'failed'].includes(x.status));
  assert.equal(completed.status, 'triggered');
  await wait('/notifications', values => values.some(x => x.sourceId === item.id));
  return { watchId: item.id, observedPublicValue: completed.lastValue, notificationDelivered: true };
});
await record('malware-rejection-before-file-storage', async () => {
  const body = new FormData();
  const signature = 'X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*';
  body.append('file', new Blob([signature], { type: 'text/plain' }), 'synthetic-antivirus-test.txt');
  const response = await fetch(base + '/files', { method: 'POST', body, headers: { Authorization: 'Bearer ' + await token() } });
  assert.equal(response.status, 422);
  assert(!(await request('GET', '/files')).some(x => x.fileName === 'synthetic-antivirus-test.txt'));
  return { status: response.status, fileStored: false };
});
process.exitCode = checks.some(x => x.status === 'failed') ? 1 : 0;
