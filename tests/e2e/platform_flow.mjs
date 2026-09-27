// End-to-end checks for generative UI, Agent2Agent, device settings, and voice
// options against a Development Jarvis API. Optional RenderUi coverage needs the
// fake Codex app-server (see README).
import assert from 'node:assert/strict';

const api = process.env.JARVIS_API ?? 'http://localhost:5082/api/v1';
const origin = process.env.JARVIS_ORIGIN ?? 'http://localhost:5082';
let passed = 0;

async function call(path, options = {}) {
  const response = await fetch((path.startsWith('http') ? path : api + path), {
    ...options,
    headers: { 'Content-Type': 'application/json', ...(options.headers ?? {}) },
  });
  const text = await response.text();
  return { status: response.status, body: text ? safeJson(text) : null, text };
}

function safeJson(text) {
  try { return JSON.parse(text); } catch { return text; }
}

async function check(name, run) {
  await run();
  passed++;
  console.log(`PASS ${name}`);
}

await check('agent card is public and points at /a2a', async () => {
  const card = await call(`${origin}/.well-known/agent-card.json`);
  assert.equal(card.status, 200, card.text);
  assert.equal(card.body.name, 'Jarvis');
  assert.match(card.body.url, /\/a2a$/);
  assert.ok(card.body.authentication.schemes.includes('bearer'));
});

await check('inbound A2A tokens are hashed and only shown once', async () => {
  const created = await call('/a2a/tokens', { method: 'POST', body: JSON.stringify({ name: 'Home lab' }) });
  assert.equal(created.status, 201, created.text);
  assert.match(created.body.token, /^jarvis-a2a-/);
  const listed = await call('/a2a/tokens');
  assert.equal(listed.status, 200, listed.text);
  const match = listed.body.find(token => token.id === created.body.id);
  assert.ok(match);
  assert.ok(!match.token);
  assert.ok(!listed.text.includes(created.body.token));

  const unauthorized = await fetch(`${origin}/a2a`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ jsonrpc: '2.0', id: '1', method: 'message/send', params: { message: { role: 'user', parts: [{ kind: 'text', text: 'hi' }] } } }),
  });
  assert.equal(unauthorized.status, 401);

  const missing = await fetch(`${origin}/a2a`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: 'Bearer jarvis-a2a-missing' },
    body: JSON.stringify({ jsonrpc: '2.0', id: '1', method: 'agent/getAuthenticatedExtendedCard' }),
  });
  assert.equal(missing.status, 401);

  const card = await fetch(`${origin}/a2a`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${created.body.token}` },
    body: JSON.stringify({ jsonrpc: '2.0', id: 'card', method: 'agent/getAuthenticatedExtendedCard' }),
  });
  assert.equal(card.status, 200, await card.clone().text());
  const payload = await card.json();
  assert.equal(payload.result.name, 'Jarvis');

  const removed = await call(`/a2a/tokens/${created.body.id}`, { method: 'DELETE' });
  assert.equal(removed.status, 204, removed.text);
});

await check('remote agents require https except localhost', async () => {
  const bad = await call('/agents', { method: 'POST', body: JSON.stringify({
    name: 'Insecure', url: 'http://assistant.example/a2a', enabled: true }) });
  assert.equal(bad.status, 400, bad.text);
  const created = await call('/agents', { method: 'POST', body: JSON.stringify({
    name: 'Travel', url: 'http://localhost:9999/a2a', enabled: true, token: 'peer-token' }) });
  assert.equal(created.status, 201, created.text);
  assert.equal(created.body.hasToken, true);
  assert.ok(!created.text.includes('peer-token'));
  const listed = await call('/agents');
  assert.ok(listed.body.some(agent => agent.id === created.body.id));
  const removed = await call(`/agents/${created.body.id}`, { method: 'DELETE' });
  assert.equal(removed.status, 204, removed.text);
});

await check('device capability toggles persist', async () => {
  const original = await call('/settings/devices');
  assert.equal(original.status, 200, original.text);
  const saved = await call('/settings/devices', { method: 'PUT', body: JSON.stringify({
    location: true, battery: true, clipboard: false, openUrl: true, notify: true }) });
  assert.equal(saved.status, 200, saved.text);
  assert.equal(saved.body.location, true);
  assert.equal(saved.body.clipboard, false);
  await call('/settings/devices', { method: 'PUT', body: JSON.stringify({
    location: original.body.location, battery: original.body.battery, clipboard: original.body.clipboard,
    openUrl: original.body.openUrl, notify: original.body.notify }) });
});

await check('voice captions and hands-free persist', async () => {
  const original = await call('/settings/voice');
  assert.equal(original.status, 200, original.text);
  const saved = await call('/settings/voice', { method: 'PUT', body: JSON.stringify({
    handsFree: false, captions: true }) });
  assert.equal(saved.status, 200, saved.text);
  assert.equal(saved.body.handsFree, false);
  assert.equal(saved.body.captions, true);
  await call('/settings/voice', { method: 'PUT', body: JSON.stringify({
    handsFree: original.body.handsFree !== false, captions: original.body.captions !== false }) });
});

await check('conversation surfaces and browser sessions start empty', async () => {
  const conversation = await call('/conversations', { method: 'POST', body: JSON.stringify({ title: 'Platform' }) });
  assert.ok([200, 201].includes(conversation.status), conversation.text);
  const surfaces = await call(`/conversations/${conversation.body.id}/surfaces`);
  assert.equal(surfaces.status, 200, surfaces.text);
  assert.deepEqual(surfaces.body, []);
  const sessions = await call(`/conversations/${conversation.body.id}/browser-sessions`);
  assert.equal(sessions.status, 200, sessions.text);
  assert.deepEqual(sessions.body, []);
});

if (process.env.JARVIS_PLATFORM_CHAT === '1') {
  await check('RenderUi writes a native choice card onto the conversation', async () => {
    const conversation = await call('/conversations', { method: 'POST', body: JSON.stringify({ title: 'Cards' }) });
    const sent = await call(`/conversations/${conversation.body.id}/messages`, {
      method: 'POST', body: JSON.stringify({ content: 'Show a choice card with travel options' }) });
    assert.ok([200, 202].includes(sent.status), sent.text);
    let surfaces;
    for (const until = Date.now() + 20000; Date.now() < until;) {
      surfaces = await call(`/conversations/${conversation.body.id}/surfaces`);
      if (surfaces.body?.length) break;
      await new Promise(resolve => setTimeout(resolve, 300));
    }
    assert.ok(surfaces.body?.length, `No UI surface appeared: ${JSON.stringify(surfaces.body)}`);
    assert.equal(surfaces.body[0].schema.kind, 'choice');
  });
}

console.log(`OK ${passed} checks`);
