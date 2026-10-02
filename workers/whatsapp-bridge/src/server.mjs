import { createServer } from 'node:http';
import { mkdir, readdir, rm } from 'node:fs/promises';
import { timingSafeEqual } from 'node:crypto';
import path from 'node:path';
import makeWASocket, {
  Browsers,
  DisconnectReason,
  fetchLatestBaileysVersion,
  makeCacheableSignalKeyStore,
  useMultiFileAuthState,
} from '@whiskeysockets/baileys';
import * as Sentry from '@sentry/node';
import pino from 'pino';
import QRCode from 'qrcode';
import { Inbox, RecentIds, inboundFrom, jidFromPhone, phoneFromJid, safeSessionId } from './lib.mjs';

const PORT = Number(process.env.PORT ?? 3000);
const DATA_DIR = process.env.DATA_DIR ?? '/data';
const TOKEN = process.env.BRIDGE_TOKEN ?? '';
const SENTRY_DSN = process.env.SENTRY_DSN ?? '';

if (SENTRY_DSN) {
  Sentry.init({
    dsn: SENTRY_DSN,
    environment: process.env.SENTRY_ENVIRONMENT || 'production',
    release: process.env.SENTRY_RELEASE || undefined,
    sendDefaultPii: false,
    tracesSampleRate: 0,
    enableLogs: true,
    integrations: [
      Sentry.pinoIntegration({
        log: { levels: ['warn', 'error'] },
        error: { levels: [] },
      }),
    ],
    beforeSend(event) {
      const headers = event.request?.headers;
      if (headers) {
        for (const key of Object.keys(headers)) {
          if (/^(authorization|cookie|set-cookie|x-api-key)$/i.test(key)) headers[key] = '[redacted]';
        }
      }
      return event;
    },
  });
}

const log = pino({ level: process.env.LOG_LEVEL ?? 'info' });

/** @type {Map<string, Session>} */
const sessions = new Map();

class Session {
  constructor(id) {
    this.id = id;
    this.dir = path.join(DATA_DIR, id);
    this.state = 'connecting'; // connecting | qr | open | logged_out
    this.qr = null;
    this.phone = null;
    this.socket = null;
    this.inbox = new Inbox();
    this.sent = new RecentIds();
    this.stopped = false;
    this.attempt = 0;
    this.timer = null;
  }

  async start() {
    if (this.stopped) return;
    await mkdir(this.dir, { recursive: true });
    const { state, saveCreds } = await useMultiFileAuthState(this.dir);
    const { version } = await fetchLatestBaileysVersion().catch(() => ({ version: undefined }));
    const quiet = pino({ level: 'silent' });
    const socket = makeWASocket({
      version,
      auth: { creds: state.creds, keys: makeCacheableSignalKeyStore(state.keys, quiet) },
      browser: Browsers.appropriate('Jarvis'),
      logger: quiet,
      markOnlineOnConnect: false,
      syncFullHistory: false,
    });
    this.socket = socket;
    socket.ev.on('creds.update', saveCreds);
    socket.ev.on('connection.update', (update) => void this.onConnection(update));
    socket.ev.on('messages.upsert', ({ messages, type }) => {
      if (type !== 'notify') return;
      for (const entry of messages) {
        const inbound = inboundFrom(entry, {
          selfPhone: this.phone,
          sentIds: this.sent,
          resolvePhone: (lid) => socket.signalRepository?.lidMapping?.getPNForLIDSync?.(lid) ?? null,
        });
        if (inbound) this.inbox.push(inbound);
      }
    });
  }

  async onConnection({ connection, lastDisconnect, qr }) {
    if (qr) {
      this.state = 'qr';
      this.qr = await QRCode.toDataURL(qr, { margin: 1, width: 320 });
    }
    if (connection === 'open') {
      this.state = 'open';
      this.qr = null;
      this.attempt = 0;
      this.phone = phoneFromJid(this.socket?.user?.id);
      log.info({ session: this.id, phone: this.phone ? 'linked' : 'unknown' }, 'whatsapp session open');
    }
    if (connection === 'close') {
      const code = lastDisconnect?.error?.output?.statusCode;
      if (this.stopped) return;
      if (code === DisconnectReason.loggedOut) {
        this.state = 'logged_out';
        this.qr = null;
        log.warn({ session: this.id }, 'whatsapp session was logged out from the phone');
        await rm(this.dir, { recursive: true, force: true });
        return;
      }
      // restartRequired (515) happens right after a successful scan; reconnect immediately.
      if (this.state !== 'qr') this.state = 'connecting';
      const delay = code === DisconnectReason.restartRequired ? 0 : Math.min(30_000, 1_000 * 2 ** this.attempt++);
      this.timer = setTimeout(() => void this.start().catch((e) => log.error(e, 'restart failed')), delay);
    }
  }

  snapshot() {
    return { state: this.state, qr: this.qr, phone: this.phone };
  }

  async send(to, text) {
    if (this.state !== 'open' || !this.socket) throw new HttpError(409, 'WhatsApp is not connected.');
    const result = await this.socket.sendMessage(jidFromPhone(to), { text });
    this.sent.add(result?.key?.id);
  }

  async stop({ logout }) {
    this.stopped = true;
    clearTimeout(this.timer);
    try {
      if (logout && this.state === 'open') await this.socket?.logout();
    } catch (error) {
      log.warn({ session: this.id, err: error?.message }, 'logout failed');
    }
    this.socket?.end?.(undefined);
    if (logout) await rm(this.dir, { recursive: true, force: true });
  }
}

class HttpError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

async function ensure(id) {
  let session = sessions.get(id);
  if (session && session.state !== 'logged_out') return session;
  if (session) await session.stop({ logout: false });
  session = new Session(id);
  sessions.set(id, session);
  await session.start();
  return session;
}

function authorized(request) {
  if (!TOKEN) return true;
  const header = request.headers.authorization ?? '';
  const given = Buffer.from(header.startsWith('Bearer ') ? header.slice(7) : '');
  const expected = Buffer.from(TOKEN);
  return given.length === expected.length && timingSafeEqual(given, expected);
}

async function readJson(request) {
  const chunks = [];
  let size = 0;
  for await (const chunk of request) {
    size += chunk.length;
    if (size > 100_000) throw new HttpError(413, 'Body too large.');
    chunks.push(chunk);
  }
  if (size === 0) return {};
  try {
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
  } catch {
    throw new HttpError(400, 'Invalid JSON.');
  }
}

async function route(request) {
  const url = new URL(request.url ?? '/', 'http://bridge');
  const parts = url.pathname.split('/').filter(Boolean);
  if (request.method === 'GET' && url.pathname === '/health') return { status: 'ok' };
  if (parts[0] !== 'sessions' || parts.length < 2) throw new HttpError(404, 'Not found.');
  const id = safeSessionId(parts[1]);
  if (!id) throw new HttpError(400, 'Invalid session id.');
  const action = parts[2];

  if (!action && request.method === 'PUT') return (await ensure(id)).snapshot();
  if (!action && request.method === 'GET') {
    const session = sessions.get(id);
    return session ? session.snapshot() : { state: 'none', qr: null, phone: null };
  }
  if (!action && request.method === 'DELETE') {
    const session = sessions.get(id);
    sessions.delete(id);
    if (session) await session.stop({ logout: true });
    else await rm(path.join(DATA_DIR, id), { recursive: true, force: true });
    return { deleted: true };
  }

  const session = sessions.get(id);
  if (!session) throw new HttpError(404, 'Unknown session.');
  if (action === 'messages' && request.method === 'GET') return { messages: session.inbox.list() };
  if (action === 'ack' && request.method === 'POST') {
    const body = await readJson(request);
    session.inbox.ack(Array.isArray(body.ids) ? body.ids.map(String) : []);
    return { ok: true };
  }
  if (action === 'send' && request.method === 'POST') {
    const body = await readJson(request);
    if (typeof body.text !== 'string' || body.text.length === 0) throw new HttpError(400, 'Text is required.');
    try {
      await session.send(body.to, body.text);
    } catch (error) {
      if (error instanceof HttpError) throw error;
      throw new HttpError(502, error?.message ?? 'Send failed.');
    }
    return { ok: true };
  }
  throw new HttpError(404, 'Not found.');
}

const server = createServer(async (request, response) => {
  const reply = (status, body) => {
    response.writeHead(status, { 'content-type': 'application/json', 'cache-control': 'no-store' });
    response.end(JSON.stringify(body));
  };
  try {
    if (request.url !== '/health' && !authorized(request)) return reply(401, { error: 'Unauthorized.' });
    reply(200, await route(request));
  } catch (error) {
    if (error instanceof HttpError) return reply(error.status, { error: error.message });
    log.error(error, 'request failed');
    if (SENTRY_DSN) Sentry.captureException(error);
    reply(500, { error: 'Internal error.' });
  }
});

async function resumeSessions() {
  await mkdir(DATA_DIR, { recursive: true });
  for (const entry of await readdir(DATA_DIR, { withFileTypes: true })) {
    if (!entry.isDirectory() || !safeSessionId(entry.name)) continue;
    await ensure(entry.name).catch((error) => log.error({ session: entry.name, err: error?.message }, 'resume failed'));
  }
}

server.listen(PORT, () => log.info({ port: PORT }, 'whatsapp bridge listening'));
void resumeSessions();

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => {
    server.close();
    for (const session of sessions.values()) void session.stop({ logout: false });
    setTimeout(() => process.exit(0), 500);
  });
}
