// The sandbox's control process: supervises Chromium and the Playwright MCP server, and serves the desktop MCP tools
// (computer_*), /reset and /health on port 8932.
import { spawn } from 'node:child_process';
import { timingSafeEqual } from 'node:crypto';
import { mkdir, readdir, rm } from 'node:fs/promises';
import { createServer } from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StreamableHTTPServerTransport } from '@modelcontextprotocol/sdk/server/streamableHttp.js';
import { z } from 'zod';
import * as desktop from './desktop.mjs';

const home = process.env.HOME ?? '/home/node';
const workDirectory = path.join(home, 'work');
const token = process.env.COMPUTER_SANDBOX_TOKEN ?? '';
const proxy = process.env.BROWSER_PROXY ?? '';
const port = Number.parseInt(process.env.PORT ?? '8932', 10);
const appRoot = path.dirname(path.dirname(fileURLToPath(import.meta.url)));

// ---- Supervised children: Chromium (with DevTools on loopback) and Playwright MCP attached to it. ----

const children = new Map();
let stopping = false;

function chromiumArgs() {
  const args = [
    '--no-sandbox', '--no-first-run', '--no-default-browser-check', '--disable-dev-shm-usage',
    '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=9222',
    `--user-data-dir=${path.join(home, '.chromium')}`,
    `--window-size=${desktop.screen.width},${desktop.screen.height}`, '--window-position=0,0', '--start-maximized',
    '--disable-features=Translate,MediaRouter', '--password-store=basic',
    // No Google background traffic (sync, updates, metrics), and no --no-sandbox banner over the page.
    '--disable-background-networking', '--disable-component-update', '--disable-sync', '--disable-default-apps',
    '--metrics-recording-only', '--test-type',
  ];
  if (proxy) args.push(`--proxy-server=${proxy}`);
  args.push('about:blank');
  return args;
}

function playwrightArgs() {
  const args = [path.join(appRoot, 'node_modules', '@playwright', 'mcp', 'cli.js'),
    '--cdp-endpoint', 'http://127.0.0.1:9222', '--port', '8931', '--host', '0.0.0.0'];
  if (process.env.PLAYWRIGHT_ALLOWED_HOSTS) args.push('--allowed-hosts', process.env.PLAYWRIGHT_ALLOWED_HOSTS);
  return args;
}

const programs = {
  chromium: () => ['chromium', chromiumArgs()],
  playwright: () => [process.execPath, playwrightArgs()],
};

function start(name) {
  const [file, args] = programs[name]();
  const child = spawn(file, args, { cwd: home, env: childEnvironment(), stdio: ['ignore', 'ignore', 'inherit'] });
  children.set(name, child);
  child.on('exit', () => {
    if (children.get(name) !== child || stopping) return;
    children.delete(name);
    setTimeout(() => { if (!stopping && !children.has(name)) start(name); }, 1_000);
  });
}

async function stop(name) {
  const child = children.get(name);
  if (!child) return;
  children.delete(name);
  const exited = new Promise((resolve) => child.once('exit', resolve));
  child.kill('SIGTERM');
  const timer = setTimeout(() => child.kill('SIGKILL'), 3_000);
  await exited;
  clearTimeout(timer);
}

async function waitForDevTools() {
  for (let attempt = 0; attempt < 100; attempt++) {
    try {
      const response = await fetch('http://127.0.0.1:9222/json/version');
      if (response.ok) return;
    } catch { /* not up yet */ }
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
}

async function startDesktop() {
  await mkdir(workDirectory, { recursive: true });
  start('chromium');
  await waitForDevTools();
  start('playwright');
}

/** Closes everything the previous session left behind: browser profile, downloads, files and running programs. */
async function reset() {
  stopping = true;
  await stop('playwright');
  await stop('chromium');
  for (const entry of await readdir(home).catch(() => [])) await rm(path.join(home, entry), { recursive: true, force: true });
  stopping = false;
  await startDesktop();
}

function childEnvironment() {
  const env = { ...process.env, HOME: home, DISPLAY: process.env.DISPLAY ?? ':99' };
  delete env.COMPUTER_SANDBOX_TOKEN;
  if (proxy) Object.assign(env, { HTTP_PROXY: proxy, HTTPS_PROXY: proxy, http_proxy: proxy, https_proxy: proxy, NO_PROXY: '127.0.0.1,localhost' });
  return env;
}

// ---- Desktop MCP tools. ----

const point = { x: z.number().int().describe('Pixels from the left edge.'), y: z.number().int().describe('Pixels from the top edge.') };

async function observed(summary) {
  const shot = await desktop.screenshot();
  return { content: [{ type: 'text', text: summary }, { type: 'image', data: shot.data, mimeType: shot.mimeType }] };
}

function failure(error) {
  return { isError: true, content: [{ type: 'text', text: error instanceof Error ? error.message : String(error) }] };
}

function tool(handler) {
  return async (args) => {
    try { return await handler(args ?? {}); } catch (error) { return failure(error); }
  };
}

function buildServer() {
  const server = new McpServer({ name: 'jarvis-computer', version: '1.0.0' });
  const size = `${desktop.screen.width}x${desktop.screen.height}`;
  server.registerTool('computer_screenshot', {
    description: `Take a screenshot of the whole ${size} sandbox desktop. Use it to see the screen before choosing coordinates.`,
    inputSchema: {},
  }, tool(() => observed('Screenshot of the sandbox desktop.')));
  server.registerTool('computer_click', {
    description: 'Click at a screen position. Returns a fresh screenshot.',
    inputSchema: { ...point, button: z.enum(['left', 'middle', 'right']).optional() },
  }, tool(async ({ x, y, button }) => { await desktop.click(x, y, button); return observed(`Clicked ${button ?? 'left'} at ${x},${y}.`); }));
  server.registerTool('computer_double_click', {
    description: 'Double-click at a screen position. Returns a fresh screenshot.',
    inputSchema: point,
  }, tool(async ({ x, y }) => { await desktop.click(x, y, 'left', 2); return observed(`Double-clicked at ${x},${y}.`); }));
  server.registerTool('computer_move', {
    description: 'Move the mouse to a screen position (for hover menus). Returns a fresh screenshot.',
    inputSchema: point,
  }, tool(async ({ x, y }) => { await desktop.move(x, y); return observed(`Moved the mouse to ${x},${y}.`); }));
  server.registerTool('computer_drag', {
    description: 'Drag with the left button from one position to another. Returns a fresh screenshot.',
    inputSchema: { fromX: z.number().int(), fromY: z.number().int(), toX: z.number().int(), toY: z.number().int() },
  }, tool(async ({ fromX, fromY, toX, toY }) => {
    await desktop.drag(fromX, fromY, toX, toY);
    return observed(`Dragged from ${fromX},${fromY} to ${toX},${toY}.`);
  }));
  server.registerTool('computer_scroll', {
    description: 'Scroll the mouse wheel at a screen position. Returns a fresh screenshot.',
    inputSchema: { ...point, direction: z.enum(['up', 'down', 'left', 'right']), amount: z.number().int().min(1).max(20).optional() },
  }, tool(async ({ x, y, direction, amount }) => {
    await desktop.scroll(x, y, direction, amount ?? 3);
    return observed(`Scrolled ${direction} at ${x},${y}.`);
  }));
  server.registerTool('computer_type', {
    description: 'Type text into the focused field, as a keyboard would. Never type passwords or other secrets. Returns a fresh screenshot.',
    inputSchema: { text: z.string().min(1).max(desktop.MaxTypedText) },
  }, tool(async ({ text }) => { await desktop.type(text); return observed(`Typed ${text.length} characters.`); }));
  server.registerTool('computer_key', {
    description: 'Press keys or chords with xdotool names, for example "Return", "Escape", "ctrl+l", "ctrl+shift+t", "alt+F4". Separate presses with spaces. Returns a fresh screenshot.',
    inputSchema: { keys: z.string().min(1).max(100) },
  }, tool(async ({ keys }) => { await desktop.key(keys); return observed(`Pressed ${keys}.`); }));
  server.registerTool('computer_wait', {
    description: 'Wait up to 10 seconds for the screen to settle, then take a screenshot.',
    inputSchema: { seconds: z.number().min(0.5).max(10).optional() },
  }, tool(async ({ seconds }) => {
    await new Promise((resolve) => setTimeout(resolve, (seconds ?? 2) * 1000));
    return observed(`Waited ${seconds ?? 2} seconds.`);
  }));
  server.registerTool('computer_shell', {
    description: 'Run a bash command in the sandbox (Debian, unprivileged user, working directory ~/work, internet only through the filtering proxy). Output is capped at 16,000 characters. Nothing outside the sandbox is reachable.',
    inputSchema: { command: z.string().min(1).max(4_000), timeoutSeconds: z.number().int().min(1).max(120).optional() },
  }, tool(async ({ command, timeoutSeconds }) => {
    await mkdir(workDirectory, { recursive: true });
    const result = await desktop.shell(command, timeoutSeconds ?? 30, { cwd: workDirectory, env: childEnvironment() });
    const status = result.timedOut ? 'timed out' : `exit code ${result.exitCode}`;
    return { isError: !result.timedOut && result.exitCode === 0 ? undefined : true,
      content: [{ type: 'text', text: `Command finished (${status}).\n${result.output}` }] };
  }));
  return server;
}

// ---- HTTP. ----

function authorized(request) {
  if (!token) return true;
  const header = request.headers.authorization ?? '';
  const expected = Buffer.from(`Bearer ${token}`);
  const actual = Buffer.from(header);
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}

async function readJson(request) {
  const chunks = [];
  let length = 0;
  for await (const chunk of request) {
    length += chunk.length;
    if (length > 1024 * 1024) throw new Error('Request body too large.');
    chunks.push(chunk);
  }
  return chunks.length ? JSON.parse(Buffer.concat(chunks).toString('utf8')) : undefined;
}

function send(response, status, body) {
  response.writeHead(status, { 'content-type': 'application/json' });
  response.end(JSON.stringify(body));
}

let resetting = Promise.resolve();

const http = createServer(async (request, response) => {
  const url = new URL(request.url ?? '/', 'http://sandbox');
  try {
    if (url.pathname === '/health' && request.method === 'GET')
      return send(response, children.has('chromium') && children.has('playwright') ? 200 : 503, { status: 'ok' });
    if (!authorized(request)) return send(response, 401, { error: 'unauthorized' });
    if (url.pathname === '/reset' && request.method === 'POST') {
      resetting = resetting.then(reset, reset);
      await resetting;
      return send(response, 200, { status: 'reset' });
    }
    if (url.pathname === '/mcp') {
      if (request.method !== 'POST') return send(response, 405, { error: 'method not allowed' });
      const body = await readJson(request);
      const server = buildServer();
      const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined, enableJsonResponse: true });
      response.on('close', () => { transport.close(); server.close(); });
      await server.connect(transport);
      return await transport.handleRequest(request, response, body);
    }
    return send(response, 404, { error: 'not found' });
  } catch (error) {
    if (!response.headersSent) send(response, 500, { error: error instanceof Error ? error.message : 'error' });
  }
});

for (const signal of ['SIGTERM', 'SIGINT']) {
  process.on(signal, async () => {
    stopping = true;
    http.close();
    await Promise.all([...children.keys()].map(stop));
    process.exit(0);
  });
}

await startDesktop();
http.listen(port, '0.0.0.0');
