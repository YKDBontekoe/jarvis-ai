#!/usr/bin/env node
// Deterministic stand-in for `codex app-server --stdio` used for local full-flow testing
// without a ChatGPT OAuth session. Point Codex__ExecutablePath at this file. It speaks the
// same JSON-RPC subset as CodexCliChatClient, streams structured output in small deltas, and
// plans multi-step Jarvis tool calls from the prompt text. It never contacts a network service.
import readline from 'node:readline';
import { reflect } from './fake_reflection.mjs';

const MODEL = 'jarvis-fixture';
const STREAM_DELAY_MS = Number(process.env.FAKE_CODEX_DELAY_MS ?? 18);
const CONTEXT_PREFIXES = [
  'Current time reference:',
  'Stored personal memory references',
  'Active durable tasks',
  'Active condition watches',
  'An unrelated task',
  'Available skills',
  'Learned persona',
  'Knowledge graph',
  'Connected devices',
];

const send = message => process.stdout.write(JSON.stringify(message) + '\n');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
let queue = Promise.resolve();

input.on('line', line => {
  let request;
  try { request = JSON.parse(line); } catch { return; }
  queue = queue.then(() => handle(request)).catch(error => {
    if (request.id !== undefined) send({ id: request.id, error: { message: String(error?.message ?? error) } });
  });
});
input.on('close', () => queue.finally(() => process.exit(0)));

async function handle(request) {
  switch (request.method) {
    case 'initialize':
      return send({ id: request.id, result: { userAgent: 'jarvis-fixture/1.0' } });
    case 'model/list':
      return send({ id: request.id, result: { data: [
        { id: MODEL, model: MODEL, inputModalities: ['text', 'image'], isDefault: true },
      ], nextCursor: null } });
    case 'thread/start':
      return send({ id: request.id, result: { thread: { id: 'thread-fixture' } } });
    case 'turn/start':
      send({ id: request.id, result: { turn: { id: 'turn-fixture', status: 'inProgress' } } });
      return runTurn(request.params);
    default:
      if (request.id !== undefined) send({ id: request.id, result: {} });
  }
}

async function runTurn(params) {
  const prompt = params.input.find(item => item.type === 'text')?.text ?? '';
  const output = JSON.stringify(plan(prompt));
  for (let index = 0; index < output.length;) {
    const size = 3 + Math.floor(Math.random() * 6);
    send({ method: 'item/agentMessage/delta', params: { delta: output.slice(index, index + size) } });
    index += size;
    if (STREAM_DELAY_MS > 0) await sleep(STREAM_DELAY_MS);
  }
  send({ method: 'thread/tokenUsage/updated', params: { tokenUsage: { last: {
    inputTokens: Math.ceil(prompt.length / 4), outputTokens: Math.ceil(output.length / 4),
    cachedInputTokens: 0, reasoningOutputTokens: 0 } } } });
  send({ method: 'turn/completed', params: { turn: { id: 'turn-fixture', status: 'completed' } } });
}

const text = value => ({ type: 'text', text: value, name: '', argumentsJson: '' });
const call = (name, args) => ({ type: 'tool_call', text: '', name, argumentsJson: JSON.stringify(args) });

function plan(prompt) {
  if (prompt.includes('Extract at most three useful long-term memories')) return text('[]');
  if (prompt.includes('Reorder saved-memory candidates')) return text('[]');

  if (prompt.includes('You are Jarvis reflecting on recent work with your user'))
    return text(JSON.stringify(reflect(parseConversation(prompt).request)));

  const conversation = parseConversation(prompt);
  const tools = new Set([...prompt.matchAll(/^- ([A-Za-z_]+): /gm)].map(match => match[1]));
  const request = conversation.request;
  const lower = request.toLowerCase();
  const results = conversation.results;
  const last = results.at(-1) ?? '';
  const has = name => tools.has(name);

  if (results.length > 0 && /rejected/i.test(last))
    return text("Understood — I didn't do that. Let me know if you'd like a different approach.");

  if (/\bremind me\b/.test(lower) && has('CreateReminder')) {
    if (results.length === 0) {
      const { title, dueAt } = parseReminder(request);
      return call('CreateReminder', { title, dueAt });
    }
    const due = last.match(/ for (\S+): /)?.[1];
    return text(`Done! I'll remind you to **${parseReminder(request).title}**` +
      (due ? ` at \`${new Date(due).toUTCString()}\`.` : '.') +
      '\n\nYou can see or cancel it any time under *Settings → Reminders*.');
  }

  if (/\bcancel\b.*\breminder\b/.test(lower) && has('CancelReminder')) {
    if (results.length === 0) return call('ListReminders', { includeFinished: false });
    if (results.length === 1) {
      const words = lower.match(/[a-z]{4,}/g)?.filter(word => !['cancel', 'reminder', 'about', 'please'].includes(word)) ?? [];
      const match = [...results[0].matchAll(/reminder ID ([0-9a-f-]{36}) due \S+: (.+)/g)]
        .find(item => words.some(word => item[2].toLowerCase().includes(word.slice(0, 5))));
      return match ? call('CancelReminder', { reminderId: match[1] }) :
        text("I couldn't find a reminder matching that.");
    }
    return text(`Done — I cancelled that reminder.`);
  }

  if (/\b(what|which).*(reminders|scheduled)\b|\blist .*reminders\b/.test(lower) && has('ListReminders')) {
    if (results.length === 0) return call('ListReminders', { includeFinished: false });
    const items = [...last.matchAll(/due (\S+): (.+?)(?:\n|$)/g)].map(match => ({ due: match[1], title: match[2] }));
    if (items.length === 0) return text("You don't have any upcoming reminders.");
    return text(`You have **${items.length}** upcoming reminder${items.length === 1 ? '' : 's'}:\n\n` +
      '| When (UTC) | Reminder |\n| --- | --- |\n' +
      items.map(item => `| ${new Date(item.due).toUTCString()} | ${item.title} |`).join('\n'));
  }

  if (/^(please )?remember\b|\bremember that\b/.test(lower) && has('Remember')) {
    if (results.length === 0) {
      const content = request.replace(/^(please )?remember( that)?\s*/i, '').replace(/\.$/, '');
      return call('Remember', { content: content.charAt(0).toUpperCase() + content.slice(1), kind: 'preference' });
    }
    return text(/already saved/.test(last) ? 'I already had that saved.' :
      "Got it — I'll remember that. You can review or edit it in **Memory**.");
  }

  if (/\bforget\b/.test(lower) && has('ForgetMemory')) {
    if (results.length === 0) return call('SearchMemory', { query: keywords(request.replace(/.*forget/i, '')) });
    const id = results[0].match(/memory ID ([0-9a-f-]{36})/i)?.[1];
    if (results.length === 1) return id ? call('ForgetMemory', { memoryId: id }) :
      text("I couldn't find a saved memory matching that.");
    return text("Done — I've forgotten that.");
  }

  if (/what do you (know|remember) about me/.test(lower) && has('SearchMemory')) {
    if (results.length === 0) return call('SearchMemory', { query: 'user preferences facts' });
    const items = [...last.matchAll(/\] (.+?)(?:\n|$)/g)].map(match => match[1]);
    return text(items.length ? "Here's what I have saved about you:\n\n" + items.map(item => `- ${item}`).join('\n')
      : "I don't have anything saved about you yet. Tell me what you'd like me to remember!");
  }

  if (/\b(save|keep) (this|that|it) as a skill\b|\bhere is how i like\b/.test(lower) && has('SaveSkill')) {
    if (results.length === 0) {
      const topic = request.match(/how i like (?:my )?(.+?)(?::|$)/i)?.[1] ?? 'weekly review';
      const name = keywords(topic).split(' ').slice(0, 3).join('-');
      const steps = request.split(/:\s*/).slice(1).join(': ') || 'Follow the steps the user described.';
      return call('SaveSkill', {
        name,
        description: `Use when the user asks for their ${topic.replace(/[.!]$/, '')}.`,
        instructions: steps.split(/,\s*|;\s*|\.\s+/).filter(Boolean)
          .map((step, index) => `${index + 1}. ${step.trim().replace(/\.$/, '')}.`).join('\n'),
        reason: 'The user described their preferred workflow.',
      });
    }
    return text(/saved and active|proposed/.test(last)
      ? `Got it — I saved that as a skill so I'll do it your way next time. ${last}`
      : last);
  }

  if (/^from now on\b|\balways (answer|reply)\b/.test(lower) && has('LearnPreference')) {
    if (results.length === 0) {
      const statement = request.replace(/^from now on,?\s*/i, '').replace(/^\w/, c => c.toUpperCase());
      const category = /dutch|english|language/i.test(request) ? 'language'
        : /short|brief|bullet|table|format/i.test(request) ? 'format' : 'workstyle';
      return call('LearnPreference', { category, statement, confidence: 0.95 });
    }
    return text(`Understood — I'll remember that. ${last}`);
  }

  if (/\buse (my|the) ([a-z-]+) skill\b/.test(lower) && has('LoadSkill')) {
    const name = lower.match(/\buse (?:my|the) ([a-z-]+) skill\b/)[1];
    if (results.length === 0) return call('LoadSkill', { name });
    return text(`Following your **${name}** skill:\n\n${last.split('\n\n').slice(1).join('\n\n') || last}`);
  }

  if (/\btime\b.*\bin\b/.test(lower) && has('GetCurrentTime')) {
    if (results.length === 0) return call('GetCurrentTime', { timeZoneId: guessZone(lower) });
    return text(`It's currently **${last.match(/: (\w+ \d{4}-\d{2}-\d{2} \d{2}:\d{2})/)?.[1] ?? 'unknown'}** in ${guessZone(lower)}.`);
  }

  if (/\b(research|background task|in the background)\b/.test(lower) && has('CreateTask')) {
    if (results.length === 0)
      return call('CreateTask', { title: request.slice(0, 60), instructions: request });
    return text("I've started that as a background task. I'll notify you when the results are ready — you can follow along in **Tasks**.");
  }

  if (conversation.executingTask)
    return text('## Result\n\nThe background task finished using the fixture model.\n\n- Finding one\n- Finding two');

  if (/^(hi|hello|hey)\b/.test(lower))
    return text("Hello! I'm Jarvis. I can set reminders, remember your preferences, search your files, watch public data for changes, and run longer research in the background. What can I do for you?");

  return text(`Here's a quick overview for **“${request.slice(0, 80)}”**:\n\n` +
    '1. I read your request and checked what I already know.\n2. I picked the most direct answer.\n3. I formatted it so it is easy to scan.\n\n' +
    '```python\nprint("Hello from Jarvis")\n```\n\n' +
    '> This reply comes from the deterministic Jarvis test model.');
}

function parseConversation(prompt) {
  const body = prompt.split('\nConversation:\n')[1]?.split('\nReturn only the JSON object')[0] ?? '';
  const blocks = [];
  for (const part of body.split(/\n\[(user|assistant|tool|system)\]\n/).slice(1).entries()) blocks.push(part[1]);
  const messages = [];
  for (let index = 0; index + 1 < blocks.length; index += 2)
    messages.push({ role: blocks[index], content: blocks[index + 1].trim() });
  let requestIndex = -1;
  messages.forEach((message, index) => {
    if (message.role === 'user' && !message.content.startsWith('Jarvis tool result:') &&
        !CONTEXT_PREFIXES.some(prefix => message.content.startsWith(prefix)) &&
        !message.content.startsWith('ToolApprovalResponseContent')) requestIndex = index;
  });
  const request = requestIndex >= 0 ? messages[requestIndex].content : '';
  const results = messages.slice(requestIndex + 1)
    .flatMap(message => message.content.split('\n').filter(line => line.startsWith('Jarvis tool result:')))
    .map(line => decodeResult(line.slice('Jarvis tool result:'.length).trim()));
  return { request, results, executingTask: prompt.includes('already scheduled background task') };
}

const STOP_WORDS = new Set(['the', 'that', 'about', 'which', 'what', 'where', 'who', 'you', 'your', 'and',
  'for', 'with', 'please', 'my', 'me', 'go', 'to', 'is', 'are', 'was', 'do', 'does', 'did', 'it', 'of', 'in']);

function keywords(value) {
  const words = value.toLowerCase().match(/[a-z0-9-]+/g)?.filter(word => word.length > 1 && !STOP_WORDS.has(word)) ?? [];
  return words.join(' ') || value.trim();
}

function decodeResult(value) {
  try {
    const decoded = JSON.parse(value);
    return typeof decoded === 'string' ? decoded : JSON.stringify(decoded);
  } catch {
    return value;
  }
}

function parseReminder(request) {
  const amount = request.match(/in (\d+|an?|one) (minute|min|hour|day)s?/i);
  const count = amount ? (/^\d+$/.test(amount[1]) ? Number(amount[1]) : 1) : 30;
  const unit = amount?.[2].toLowerCase().startsWith('h') ? 3_600_000 :
    amount?.[2].toLowerCase().startsWith('d') ? 86_400_000 : 60_000;
  const dueAt = new Date(Date.now() + count * unit).toISOString().replace(/\.\d{3}Z$/, 'Z');
  const title = request.replace(/.*remind me (to )?/i, '').replace(/\s*in (\d+|an?|one) \w+$/i, '')
    .replace(/[.?!]$/, '').trim() || 'your reminder';
  return { title, dueAt };
}

function guessZone(lower) {
  if (lower.includes('tokyo')) return 'Asia/Tokyo';
  if (lower.includes('new york')) return 'America/New_York';
  if (lower.includes('london')) return 'Europe/London';
  if (lower.includes('amsterdam')) return 'Europe/Amsterdam';
  return 'UTC';
}

