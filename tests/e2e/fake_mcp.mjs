// Deterministic MCP fixture. It records calls locally and never contacts a service.
import readline from 'node:readline';
import { appendFileSync } from 'node:fs';

const tools = [
  { name: 'github_create_issue', description: 'Create an issue in the configured verification/example repository.',
    inputSchema: { type: 'object', properties: { title: { type: 'string' }, repository: { type: 'string' } }, required: ['title'] } },
  { name: 'search_docs', description: 'Find the maintenance window in the documentation.',
    inputSchema: { type: 'object', properties: { query: { type: 'string' } }, required: ['query'] } },
];

const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
input.on('line', line => {
  let request;
  try { request = JSON.parse(line); } catch { return; }
  if (request.id === undefined) return;
  let result;
  if (request.method === 'initialize') {
    result = { protocolVersion: request.params.protocolVersion, capabilities: { tools: {} },
      serverInfo: { name: 'jarvis-verification-fixture', version: '1.0.0' } };
  } else if (request.method === 'ping') {
    result = {};
  } else if (request.method === 'tools/list') {
    result = { tools };
  } else if (request.method === 'tools/call') {
    appendFileSync('/tmp/jarvis-verification-mcp-calls.jsonl', JSON.stringify({
      tool: request.params.name, arguments: request.params.arguments, timestamp: new Date().toISOString(),
    }) + '\n');
    const value = request.params.name === 'search_docs'
      ? { title: 'Maintenance windows', url: 'https://docs.example.net/maintenance',
          text: 'Maintenance is scheduled for Sunday from 02:00 to 03:00 UTC.' }
      : { number: 42, title: request.params.arguments.title, repository: 'verification/example',
          url: 'https://github.example.net/verification/example/issues/42', credentialWasProvided: Boolean(process.env.TEST_TOKEN) };
    result = { content: [{ type: 'text', text: JSON.stringify(value) }], isError: false };
  } else {
    process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: request.id, error: { code: -32601, message: 'Method not found' } }) + '\n');
    return;
  }
  process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: request.id, result }) + '\n');
});
