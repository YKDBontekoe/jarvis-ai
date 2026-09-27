// End-to-end WhatsApp and Signal checks against a Development Jarvis API, the fake Codex model, and
// fake_channels.mjs. Run with the API configured for the fake channel endpoints (see README).
import assert from 'node:assert/strict';
import crypto from 'node:crypto';

const api = process.env.JARVIS_API ?? 'http://localhost:5082/api/v1';
const fake = process.env.FAKE_CHANNELS ?? 'http://localhost:5198';
const owner = '+31612345678';
const stranger = '+15550100999';
const appSecret = 'test-app-secret';
let passed = 0;

async function call(path, options = {}) {
  const response = await fetch(api + path, {
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

async function sentMessages() {
  return (await (await fetch(`${fake}/_test/sent`)).json());
}

async function waitForReply(channel, predicate, timeoutMs = 20000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    const match = (await sentMessages()).find(message => message.channel === channel && predicate(message));
    if (match) return match;
    await new Promise(resolve => setTimeout(resolve, 300));
  }
  throw new Error(`Timed out waiting for a ${channel} reply. Sent: ${JSON.stringify(await sentMessages())}`);
}

function webhookPayload(from, text, id) {
  return JSON.stringify({ object: 'whatsapp_business_account', entry: [{ id: 'WABA', changes: [{ field: 'messages',
    value: { messaging_product: 'whatsapp', metadata: { phone_number_id: '106540352242922' },
      messages: [{ from: from.replace('+', ''), id, timestamp: String(Date.now() / 1000 | 0), type: 'text',
        text: { body: text } }] } }] }] });
}

async function postWebhook(url, payload, secret = appSecret) {
  const signature = 'sha256=' + crypto.createHmac('sha256', secret).update(payload).digest('hex');
  return fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Hub-Signature-256': signature },
    body: payload });
}

await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
for (const existing of (await call('/channels')).body ?? [])
  await call(`/channels/${existing.id}`, { method: 'DELETE' });

let whatsapp;
await check('WhatsApp connection stores secrets without returning them', async () => {
  const created = await call('/channels', { method: 'POST', body: JSON.stringify({
    kind: 'whatsapp', displayName: 'My phone', account: '106540352242922', enabled: true,
    allowedSenders: ['+31 6 1234 5678'], forwardNotifications: true,
    secrets: { access_token: 'EAAtest-token', app_secret: appSecret, verify_token: 'verify-me' } }) });
  assert.equal(created.status, 201, created.text);
  whatsapp = created.body;
  assert.deepEqual(whatsapp.allowedSenders, [owner]);
  assert.deepEqual([...whatsapp.configuredSecrets].sort(), ['access_token', 'app_secret', 'verify_token']);
  assert.ok(!created.text.includes('EAAtest-token'));
  assert.match(whatsapp.webhookUrl, /\/api\/v1\/channels\/whatsapp\/[0-9a-f]{48}\/webhook$/);
});

await check('Meta webhook verification echoes the challenge only for the right token', async () => {
  const ok = await fetch(`${whatsapp.webhookUrl}?hub.mode=subscribe&hub.verify_token=verify-me&hub.challenge=42`);
  assert.equal(await ok.text(), '42');
  const bad = await fetch(`${whatsapp.webhookUrl}?hub.mode=subscribe&hub.verify_token=nope&hub.challenge=42`);
  assert.equal(bad.status, 403);
});

await check('unsigned or forged webhooks are rejected', async () => {
  const forged = await postWebhook(whatsapp.webhookUrl, webhookPayload(owner, 'hi', 'wamid.forged'), 'wrong');
  assert.equal(forged.status, 401);
});

await check('an allowlisted WhatsApp message runs a Jarvis tool and replies', async () => {
  const response = await postWebhook(whatsapp.webhookUrl,
    webhookPayload(owner, 'Remind me to stretch in 10 minutes', 'wamid.reminder'));
  assert.equal(response.status, 200);
  const reply = await waitForReply('whatsapp', message => /stretch/i.test(message.text));
  assert.equal(reply.to, owner);
  assert.match(reply.text, /\*stretch\*/, 'Markdown bold is converted to WhatsApp bold');
  const reminders = (await call('/reminders')).body;
  assert.ok(reminders.some(reminder => reminder.title === 'stretch'));
});

await check('duplicate webhook deliveries are processed once', async () => {
  const before = (await sentMessages()).length;
  await postWebhook(whatsapp.webhookUrl, webhookPayload(owner, 'Remind me to stretch in 10 minutes', 'wamid.reminder'));
  await new Promise(resolve => setTimeout(resolve, 2500));
  assert.equal((await sentMessages()).length, before);
});

await check('messages from strangers never reach the agent', async () => {
  await postWebhook(whatsapp.webhookUrl, webhookPayload(stranger, 'What do you know about me?', 'wamid.stranger'));
  await new Promise(resolve => setTimeout(resolve, 2500));
  assert.ok(!(await sentMessages()).some(message => message.to === stranger));
});

await check('approvals are answered with YES from the chat app', async () => {
  const remembered = await call('/memory', { method: 'POST', body: JSON.stringify({ kind: 'preference',
    content: 'I collect vintage theremins' }) });
  assert.equal(remembered.status, 201);
  await postWebhook(whatsapp.webhookUrl, webhookPayload(owner, 'Please forget vintage theremins', 'wamid.forget'));
  await waitForReply('whatsapp', message => /Reply YES to approve/.test(message.text));
  await postWebhook(whatsapp.webhookUrl, webhookPayload(owner, 'YES', 'wamid.yes'));
  await waitForReply('whatsapp', message => /forgotten/i.test(message.text));
  const memories = (await call('/memory')).body;
  assert.ok(!memories.some(memory => memory.id === remembered.body.id));
});

let signal;
await check('Signal links through signal-cli and answers polled messages', async () => {
  const status = (await call('/channels/signal/status')).body;
  assert.deepEqual(status, { configured: true, accounts: ['+31600000001'] });
  const qr = await fetch(`${api}/channels/signal/link`);
  assert.equal(qr.headers.get('content-type'), 'image/png');
  const created = await call('/channels', { method: 'POST', body: JSON.stringify({
    kind: 'signal', displayName: 'Signal', account: '+31600000001', enabled: true,
    allowedSenders: [owner], forwardNotifications: true }) });
  assert.equal(created.status, 201, created.text);
  signal = created.body;
  await fetch(`${fake}/_test/signal/inbound`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ from: owner, message: 'hello' }) });
  const reply = await waitForReply('signal', message => /I'm Jarvis/.test(message.text));
  assert.equal(reply.to, owner);
});

await check('/new starts a fresh conversation thread', async () => {
  await fetch(`${fake}/_test/signal/inbound`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ from: owner, message: '/new' }) });
  await waitForReply('signal', message => /fresh conversation/.test(message.text));
});

await check('notifications are forwarded to connected channels', async () => {
  const dueAt = new Date(Date.now() + 6000).toISOString();
  const created = await call('/reminders', { method: 'POST', body: JSON.stringify({ title: 'Take the bread out', dueAt }) });
  assert.equal(created.status, 201, created.text);
  const signalAlert = await waitForReply('signal', message => /Take the bread out/.test(message.text), 45000);
  assert.match(signalAlert.text, /🔔/);
  await waitForReply('whatsapp', message => /Take the bread out/.test(message.text), 20000);
});

await check('channel activity log shows both directions', async () => {
  const log = (await call(`/channels/${signal.id}/messages`)).body;
  assert.ok(log.some(item => item.direction === 'in' && item.status === 'processed'));
  assert.ok(log.some(item => item.direction === 'out' && item.status === 'sent'));
});

console.log(`\n${passed}/${passed} channel checks passed`);
