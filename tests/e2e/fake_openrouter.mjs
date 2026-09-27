#!/usr/bin/env node
// Deterministic OpenAI-compatible stand-in for OpenRouter used by local full-flow tests.
// Point OpenRouter__BaseUrl at http://localhost:5199/api/v1. It serves /models, streaming and
// non-streaming /chat/completions with native tool calls, and hashed bag-of-words /embeddings.
import http from 'node:http';
import crypto from 'node:crypto';
import { extractGraph, reflect } from './fake_reflection.mjs';

const PORT = Number(process.env.FAKE_OPENROUTER_PORT ?? 5199);
const DIMENSIONS = 256;
const CONTEXT_PREFIXES = [
  'Current time reference:', 'Stored personal memory references', 'Active durable tasks',
  'Connected devices', 'An unrelated task', 'Learned persona', 'Available skills', 'Knowledge graph',
  'Remote agents', 'Generative UI', 'Browser agent',
];
const MODELS = [
  { id: 'anthropic/claude-sonnet-4.5', name: 'Anthropic: Claude Sonnet 4.5', context_length: 200000,
    pricing: { prompt: '0.000003', completion: '0.000015' }, supported_parameters: ['tools', 'temperature'],
    architecture: { input_modalities: ['text', 'image'] } },
  { id: 'google/gemini-2.5-flash', name: 'Google: Gemini 2.5 Flash', context_length: 1048576,
    pricing: { prompt: '0.0000003', completion: '0.0000025' }, supported_parameters: ['tools'],
    architecture: { input_modalities: ['text', 'image'] } },
  { id: 'openai/text-embedding-3-small', name: 'OpenAI: Text Embedding 3 Small', context_length: 8192,
    pricing: { prompt: '0.00000002' }, supported_parameters: [], architecture: { input_modalities: ['text'] } },
];

const server = http.createServer(async (request, response) => {
  try {
    const url = new URL(request.url, `http://localhost:${PORT}`);
    if (request.method === 'GET' && url.pathname === '/api/v1/models') return json(response, 200, { data: MODELS });
    if (request.method !== 'POST') return json(response, 404, { error: { message: 'Not found' } });
    if (!/^Bearer sk-or-/.test(request.headers.authorization ?? ''))
      return json(response, 401, { error: { message: 'Invalid OpenRouter API key.' } });
    const body = JSON.parse(await readBody(request));
    if (url.pathname === '/api/v1/embeddings') return embeddings(response, body);
    if (url.pathname === '/api/v1/chat/completions') return chat(response, body);
    return json(response, 404, { error: { message: 'Not found' } });
  } catch (error) {
    json(response, 500, { error: { message: String(error?.message ?? error) } });
  }
});
server.listen(PORT, () => console.log(`fake OpenRouter listening on ${PORT}`));

function embeddings(response, body) {
  const inputs = Array.isArray(body.input) ? body.input : [body.input];
  json(response, 200, {
    object: 'list', model: body.model,
    data: inputs.map((text, index) => {
      const vector = embed(String(text));
      return { object: 'embedding', index, embedding: body.encoding_format === 'base64'
        ? Buffer.from(new Float32Array(vector).buffer).toString('base64') : vector };
    }),
    usage: { prompt_tokens: Math.ceil(inputs.join(' ').length / 4), total_tokens: Math.ceil(inputs.join(' ').length / 4) },
  });
}

// Hashes lower-cased word stems into a fixed vector so paraphrases that share stems land close together.
function embed(text) {
  const vector = new Array(DIMENSIONS).fill(0);
  for (const word of text.toLowerCase().match(/[a-z0-9]+/g) ?? []) {
    const stem = word.slice(0, 5);
    const hash = crypto.createHash('sha1').update(stem).digest();
    vector[hash.readUInt16BE(0) % DIMENSIONS] += 1;
    vector[hash.readUInt16BE(2) % DIMENSIONS] += 0.5;
  }
  const norm = Math.sqrt(vector.reduce((sum, value) => sum + value * value, 0)) || 1;
  return vector.map(value => value / norm);
}

function chat(response, body) {
  const reply = plan(body);
  const id = 'gen-' + crypto.randomUUID();
  const created = Math.floor(Date.now() / 1000);
  const usage = { prompt_tokens: 42, completion_tokens: 17, total_tokens: 59 };
  if (!body.stream) {
    return json(response, 200, {
      id, object: 'chat.completion', created, model: body.model,
      choices: [{ index: 0, finish_reason: reply.toolCall ? 'tool_calls' : 'stop', message: reply.toolCall
        ? { role: 'assistant', content: null, tool_calls: [toolCall(reply.toolCall)] }
        : { role: 'assistant', content: reply.text } }],
      usage,
    });
  }
  response.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache' });
  const chunk = (delta, finish = null) => response.write('data: ' + JSON.stringify({
    id, object: 'chat.completion.chunk', created, model: body.model,
    choices: [{ index: 0, delta, finish_reason: finish }],
  }) + '\n\n');
  chunk({ role: 'assistant', content: '' });
  if (reply.toolCall) {
    chunk({ tool_calls: [{ index: 0, ...toolCall(reply.toolCall) }] });
    chunk({}, 'tool_calls');
  } else {
    for (const piece of reply.text.match(/.{1,12}/gs) ?? []) chunk({ content: piece });
    chunk({}, 'stop');
  }
  response.write('data: ' + JSON.stringify({ id, object: 'chat.completion.chunk', created, model: body.model,
    choices: [], usage }) + '\n\n');
  response.end('data: [DONE]\n\n');
}

const toolCall = ({ name, args }) => ({ id: 'call_' + crypto.randomUUID().replaceAll('-', '').slice(0, 20),
  type: 'function', function: { name, arguments: JSON.stringify(args) } });

function plan(body) {
  const messages = body.messages ?? [];
  const system = messages.filter(message => message.role === 'system').map(text).join('\n');
  const all = messages.map(text).join('\n');
  const tools = new Set((body.tools ?? []).map(tool => tool.function?.name));
  const lastUser = [...messages].reverse().find(message => message.role === 'user' &&
    !CONTEXT_PREFIXES.some(prefix => text(message).startsWith(prefix)));
  const request = text(lastUser);
  const lower = request.toLowerCase();
  const trailingTools = [];
  for (let index = messages.length - 1; index >= 0 && messages[index].role === 'tool'; index--)
    trailingTools.unshift(text(messages[index]));

  if (/Reply with the single word OK/.test(request)) return { text: 'OK' };
  if (/Extract at most three useful long-term memories/.test(system + all)) return { text: '[]' };
  if (/Reorder saved-memory candidates/.test(system + all)) return { text: '[]' };
  if (/You maintain a temporal knowledge graph/.test(system)) return { text: JSON.stringify(extractGraph(request)) };
  if (/You are Jarvis reflecting on recent work with your user/.test(system))
    return { text: JSON.stringify(reflect(request)) };

  if (trailingTools.length > 0) {
    const result = trailingTools.at(-1);
    if (/rejected/i.test(result)) return { text: "Understood — I didn't do that." };
    return { text: `Done. ${result.slice(0, 280)}` };
  }
  if (/\bremind me\b/.test(lower) && tools.has('CreateReminder')) {
    const dueAt = new Date(Date.now() + 10 * 60_000).toISOString();
    return { toolCall: { name: 'CreateReminder', args: { title: request.replace(/.*remind me (to )?/i, '').replace(/ in .*$/, ''), dueAt } } };
  }
  if (/\bwhat model\b|\bwhich model\b/.test(lower)) return { text: `I'm running on **${body.model}** through OpenRouter.` };
  return { text: `**${body.model}** via OpenRouter: ${request.slice(0, 400)}` };
}

function text(message) {
  if (!message) return '';
  if (typeof message.content === 'string') return message.content;
  if (Array.isArray(message.content)) return message.content.map(part => part.text ?? '').join('\n');
  return '';
}

function json(response, status, value) {
  response.writeHead(status, { 'Content-Type': 'application/json' });
  response.end(JSON.stringify(value));
}

function readBody(request) {
  return new Promise((resolve, reject) => {
    let data = '';
    request.on('data', part => { data += part; });
    request.on('end', () => resolve(data || '{}'));
    request.on('error', reject);
  });
}
