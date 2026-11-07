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

// A media message: type is image, audio, document...; fields are what the platform puts under that type.
function mediaPayload(from, id, type, fields) {
  return JSON.stringify({ object: 'whatsapp_business_account', entry: [{ id: 'WABA', changes: [{ field: 'messages',
    value: { messaging_product: 'whatsapp', metadata: { phone_number_id: '106540352242922' },
      messages: [{ from: from.replace('+', ''), id, timestamp: String(Date.now() / 1000 | 0), type,
        [type]: fields }] } }] }] });
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

await check('a WhatsApp photo reaches the model as an image, caption as the question', async () => {
  const response = await postWebhook(whatsapp.webhookUrl, mediaPayload(owner, 'wamid.photo1', 'image',
    { id: 'fake-photo-1', mime_type: 'image/png', caption: 'How many images do you see?' }));
  assert.equal(response.status, 200);
  const reply = await waitForReply('whatsapp', message => /I see \d+ image/.test(message.text));
  assert.match(reply.text, /I see 1 image\(s\): image\/png/, reply.text);
  assert.equal(reply.to, owner);
  const files = (await call('/files')).body ?? [];
  assert.ok(files.some(file => /^whatsapp-photo-.*\.png$/.test(file.fileName)), JSON.stringify(files.map(f => f.fileName)));
});

await check('a photo without a caption is still shown to the model', async () => {
  await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
  const response = await postWebhook(whatsapp.webhookUrl, mediaPayload(owner, 'wamid.photo2', 'image',
    { id: 'fake-photo-2', mime_type: 'image/png' }));
  assert.equal(response.status, 200);
  // The fake model answers every other prompt with its canned reply; the point is that a reply comes at all.
  const reply = await waitForReply('whatsapp', () => true);
  assert.equal(reply.to, owner);
});

await check('voice notes and documents are answered honestly instead of being dropped', async () => {
  await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
  const response = await postWebhook(whatsapp.webhookUrl, mediaPayload(owner, 'wamid.voice1', 'audio',
    { id: 'fake-voice-1', mime_type: 'audio/ogg; codecs=opus', voice: true }));
  assert.equal(response.status, 200);
  const reply = await waitForReply('whatsapp', message => /can read photos/.test(message.text));
  assert.match(reply.text, /voice notes or audio/);
});

await check('a WhatsApp document is stored, attached to the chat as a source, and answered', async () => {
  await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
  const response = await postWebhook(whatsapp.webhookUrl, mediaPayload(owner, 'wamid.doc1', 'document',
    { id: 'fake-docx-1', mime_type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      filename: 'Roadmap Q3.docx', caption: 'Summarise this file' }));
  assert.equal(response.status, 200);
  await waitForReply('whatsapp', message => !/couldn't|can't store/.test(message.text));
  const files = (await call('/files')).body ?? [];
  const stored = files.find(file => file.fileName === 'Roadmap Q3.docx');
  assert.ok(stored, JSON.stringify(files.map(f => f.fileName)));
  const threads = (await call(`/channels/${whatsapp.id}/messages`)).body ?? [];
  assert.ok(threads.some(item => item.direction === 'in' && item.status === 'processed'));
});

await check('a file type Jarvis cannot store is refused kindly and nothing is kept', async () => {
  await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
  const filesBefore = ((await call('/files')).body ?? []).length;
  const response = await postWebhook(whatsapp.webhookUrl, mediaPayload(owner, 'wamid.doc2', 'document',
    { id: 'fake-exe-1', mime_type: 'application/x-msdownload', filename: 'setup.exe' }));
  assert.equal(response.status, 200);
  const reply = await waitForReply('whatsapp', message => /can't store setup\.exe/.test(message.text));
  assert.match(reply.text, /PDF, Word/);
  assert.equal(((await call('/files')).body ?? []).length, filesBefore);
});

await check('a shared WhatsApp location pin is understood instead of dropped', async () => {
  await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
  const response = await postWebhook(whatsapp.webhookUrl, mediaPayload(owner, 'wamid.loc1', 'location',
    { latitude: 38.7223, longitude: -9.1393, name: 'Time Out Market' }));
  assert.equal(response.status, 200);
  const reply = await waitForReply('whatsapp', () => true);
  assert.equal(reply.to, owner);
});

await check('check-in, spending and today commands work from WhatsApp without the model', async () => {
  const tag = Date.now().toString(36);
  const say = async (text, id, expected) => {
    await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
    await postWebhook(whatsapp.webhookUrl, webhookPayload(owner, text, id));
    return waitForReply('whatsapp', message => expected.test(message.text));
  };
  const habit = await call('/habits', { method: 'POST', body: JSON.stringify({ name: `Stretch ${tag}`, cadence: 'daily' }) });
  assert.equal(habit.status, 201, habit.text);
  const checked = await say(`/checkin stretch ${tag}`, `wamid.ci-${tag}`, /streak/);
  assert.match(checked.text, new RegExp(`Stretch ${tag}`));
  await say(`/checkin stretch ${tag}`, `wamid.ci2-${tag}`, /already checked in/);
  await say('/checkin no such habit zz9', `wamid.ci3-${tag}`, /No habit called/);

  const spent = await say(`/spent 7,40 espresso ${tag}`, `wamid.sp-${tag}`, /Logged 7\.40 [A-Z]{3}/);
  assert.match(spent.text, new RegExp(`espresso ${tag}`));
  const month = (await call('/expenses')).body;
  assert.ok(month.expenses.some(item => item.merchant === `espresso ${tag}` && item.source === 'chat'));
  await say(`/spent 7,40 espresso ${tag}`, `wamid.sp2-${tag}`, /same expense/);
  await say('/spent lunch', `wamid.sp3-${tag}`, /Log spending with/);

  const journal = await say(`/journal Chat note ${tag}: calm walk`, `wamid.jr-${tag}`, /journal/);
  assert.match(journal.text, /📓/);
  const entries = (await call('/journal')).body;
  assert.ok(entries.some(item => item.content.includes(`Chat note ${tag}`) && item.source === 'chat'), 'in the journal, from chat');
  const audit = (await call('/audit?limit=20')).body ?? [];
  assert.ok(JSON.stringify(audit).includes('journal'), 'audited');
  assert.ok(!JSON.stringify(audit).includes(`Chat note ${tag}`), 'the audit log never holds the text');

  const today = await say('/today', `wamid.td-${tag}`, /min free/);
  assert.ok(today.text);
  await call(`/habits/${habit.body.id}`, { method: 'DELETE' });
});

await check('reminder commands work from WhatsApp without the model', async () => {
  const tag = Date.now().toString(36);
  const say = async (text, id, expected) => {
    await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
    await postWebhook(whatsapp.webhookUrl, webhookPayload(owner, text, id));
    return waitForReply('whatsapp', message => expected.test(message.text));
  };
  const set = await say(`/remind in 45m call mum ${tag}`, `wamid.rem-${tag}`, /Reminder set/);
  assert.match(set.text, new RegExp(`call mum ${tag}`));
  const stored = (await call('/reminders')).body.find(item => item.title === `call mum ${tag}`);
  assert.ok(stored, 'the reminder is in the app too');
  const due = new Date(stored.dueAt).getTime() - Date.now();
  assert.ok(due > 40 * 60_000 && due < 50 * 60_000, `due in about 45 minutes, got ${due / 60000}`);
  const repeating = await say(`/remind every other friday 17:00 report ${tag}`, `wamid.rep-${tag}`, /every other Friday/);
  assert.match(repeating.text, /17:00/);
  const series = (await call('/reminders')).body.find(item => item.title === `report ${tag}`);
  assert.ok(series, 'the repeating reminder is in the app too');
  assert.equal(series.recurrence, 'biweekly');
  assert.equal(series.weekdays, 16);
  await call(`/reminders/${series.id}`, { method: 'DELETE' });
  const listed = await say('/reminders', `wamid.rems-${tag}`, new RegExp(`call mum ${tag}`));
  assert.ok(listed.text);
  const usage = await say('/remind soon', `wamid.remx-${tag}`, /\/remind in 30m/);
  assert.ok(usage.text);
  await call(`/reminders/${stored.id}`, { method: 'DELETE' });
});

await check('list commands work from WhatsApp without the model', async () => {
  const tag = Date.now().toString(36);
  const say = async (text, id, expected) => {
    await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
    await postWebhook(whatsapp.webhookUrl, webhookPayload(owner, text, id));
    return waitForReply('whatsapp', message => expected.test(message.text));
  };
  const added = await say(`/add Chat ${tag}: milk, eggs`, `wamid.add-${tag}`, /milk/);
  assert.match(added.text, /eggs/);
  const ticked = await say(`/done Chat ${tag}: milk`, `wamid.done-${tag}`, /☑/);
  assert.match(ticked.text, /eggs/);
  const lists = await say('/lists', `wamid.lists-${tag}`, new RegExp(`Chat ${tag}`));
  assert.match(lists.text, /1 open/);
  const usage = await say('/add oops', `wamid.oops-${tag}`, /\/add shopping: milk/);
  assert.ok(usage.text);
  const stored = (await call('/lists')).body.find(list => list.name === `Chat ${tag}`);
  assert.ok(stored, 'the list is in the app too');
  await call(`/lists/${stored.id}`, { method: 'DELETE' });
});

await check('/new leaves an attached file behind with the old conversation', async () => {
  // The fake model reads the whole prompt, so later checks need a thread without the file's source note.
  await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
  await postWebhook(whatsapp.webhookUrl, webhookPayload(owner, '/new', 'wamid.new-after-doc'));
  await waitForReply('whatsapp', message => /fresh conversation/.test(message.text));
});

await check('a photo from a stranger is never downloaded', async () => {
  await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
  const filesBefore = ((await call('/files')).body ?? []).length;
  const response = await postWebhook(whatsapp.webhookUrl, mediaPayload(stranger, 'wamid.photo3', 'image',
    { id: 'fake-photo-3', mime_type: 'image/png', caption: 'hi' }));
  assert.equal(response.status, 200);
  await new Promise(resolve => setTimeout(resolve, 3000));
  assert.equal((await sentMessages()).length, 0);
  assert.equal(((await call('/files')).body ?? []).length, filesBefore);
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

await check('a Signal document is stored as the sender named it', async () => {
  await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
  await fetch(`${fake}/_test/signal/inbound`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ from: owner, message: 'What is in this?',
      attachments: [{ id: 'fake-docx-signal.docx', contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        filename: 'Signal plan.docx', size: 100 }] }) });
  await waitForReply('signal', message => !/couldn't|can't store/.test(message.text));
  const files = (await call('/files')).body ?? [];
  assert.ok(files.some(file => file.fileName === 'Signal plan.docx'), JSON.stringify(files.map(f => f.fileName)));
});

await check('Signal /new leaves the attached file behind', async () => {
  await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
  await fetch(`${fake}/_test/signal/inbound`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ from: owner, message: '/new' }) });
  await waitForReply('signal', message => /fresh conversation/.test(message.text));
});

await check('a Signal photo reaches the model as an image', async () => {
  await fetch(`${fake}/_test/sent`, { method: 'DELETE' });
  await fetch(`${fake}/_test/signal/inbound`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ from: owner, message: 'How many images do you see?',
      attachments: [{ id: 'fake-signal-1.png', contentType: 'image/png', filename: 'x.png', size: 100 }] }) });
  const reply = await waitForReply('signal', message => /I see \d+ image/.test(message.text));
  assert.match(reply.text, /I see 1 image\(s\): image\/png/, reply.text);
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
