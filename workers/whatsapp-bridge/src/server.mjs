import { createServer } from 'node:http';
import { mkdir, readFile, readdir, rename, rm, writeFile } from 'node:fs/promises';
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
import {
  ChatBook,
  Inbox,
  RecentIds,
  chatIdOf,
  inboundFrom,
  isGroupJid,
  jidFromChatId,
  normalizeWatchList,
  observedFrom,
  phoneFromJid,
  safeSessionId,
} from './lib.mjs';

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
    // Read along: chats the owner turned on in Jarvis, their buffered messages, and the chat list for the picker.
    this.watched = new Set();
    this.observed = new Inbox(2_000);
    this.chats = new ChatBook();
    this.loaded = false;
    this.saveTimer = null;
    this.stopped = false;
    this.attempt = 0;
    this.timer = null;
  }

  async start() {
    if (this.stopped) return;
    await mkdir(this.dir, { recursive: true });
    if (!this.loaded) {
      this.loaded = true;
      this.chats.load(await readJsonFile(path.join(this.dir, CHATS_FILE)));
      this.watched = normalizeWatchList((await readJsonFile(path.join(this.dir, WATCH_FILE)))?.chats);
      this.saveTimer = setInterval(() => void this.saveChats(), 10_000);
    }
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
    const resolvePhone = (lid) => socket.signalRepository?.lidMapping?.getPNForLIDSync?.(lid) ?? null;
    socket.ev.on('messages.upsert', ({ messages, type }) => {
      const recent = Math.floor(Date.now() / 1000) - APPEND_WINDOW_SECONDS;
      for (const entry of messages) {
        const timestamp = toNumber(entry.messageTimestamp);
        const chatId = chatIdOf(entry?.key?.remoteJid, resolvePhone);
        if (chatId) {
          const personName = !entry.key.fromMe && !isGroupJid(chatId) ? entry.pushName : null;
          this.chats.touch(chatId, { name: personName, timestamp });
        }
        // 'append' carries messages sent from the phone while this device was catching up; keep only recent ones.
        if (type !== 'notify' && !(type === 'append' && timestamp >= recent)) continue;
        const observed = observedFrom(entry, { selfPhone: this.phone, watched: this.watched, resolvePhone });
        if (observed) this.observed.push(observed);
        if (type !== 'notify') continue;
        const inbound = inboundFrom(entry, { selfPhone: this.phone, sentIds: this.sent, resolvePhone });
        if (inbound) this.inbox.push(inbound);
      }
    });
    socket.ev.on('messaging-history.set', ({ chats, contacts }) => {
      for (const contact of contacts ?? []) this.addContact(contact, resolvePhone);
      for (const chat of chats ?? []) this.addChat(chat, resolvePhone);
    });
    socket.ev.on('chats.upsert', (chats) => chats.forEach((chat) => this.addChat(chat, resolvePhone)));
    socket.ev.on('chats.update', (chats) => chats.forEach((chat) => this.addChat(chat, resolvePhone, false)));
    socket.ev.on('contacts.upsert', (contacts) => contacts.forEach((contact) => this.addContact(contact, resolvePhone)));
    socket.ev.on('contacts.update', (contacts) => contacts.forEach((contact) => this.addContact(contact, resolvePhone)));
    socket.ev.on('groups.upsert', (groups) => groups.forEach((group) => this.chats.name(chatIdOf(group.id), group.subject)));
    socket.ev.on('groups.update', (groups) => groups.forEach((group) => this.chats.name(chatIdOf(group.id), group.subject)));
  }

  addChat(chat, resolvePhone, create = true) {
    const chatId = chatIdOf(chat?.id, resolvePhone);
    if (!chatId || chatId === this.phone) return;
    if (create || this.chats.chats.has(chatId))
      this.chats.touch(chatId, { name: chat.name ?? null, timestamp: toNumber(chat.conversationTimestamp) });
    else if (chat.name) this.chats.name(chatId, chat.name);
  }

  addContact(contact, resolvePhone) {
    const name = contact?.name ?? contact?.notify ?? contact?.verifiedName ?? null;
    if (!name) return;
    for (const jid of [contact.phoneNumber, contact.id, contact.lid]) {
      const chatId = chatIdOf(jid, resolvePhone);
      if (chatId && chatId !== this.phone) this.chats.name(chatId, name);
    }
  }

  async saveChats() {
    if (!this.chats.dirty || this.stopped) return;
    this.chats.dirty = false;
    await writeJsonFile(path.join(this.dir, CHATS_FILE), this.chats.toJSON())
      .catch((error) => log.warn({ session: this.id, err: error?.message }, 'could not save chat list'));
  }

  async setWatched(ids) {
    this.watched = normalizeWatchList(ids);
    // Drop buffered messages of chats that were turned off, so nothing from them reaches Jarvis afterwards.
    this.observed.items = this.observed.items.filter((item) => this.watched.has(item.chatId));
    await writeJsonFile(path.join(this.dir, WATCH_FILE), { chats: [...this.watched] });
    return this.watched.size;
  }

  async loadGroupNames() {
    try {
      const groups = await this.socket?.groupFetchAllParticipating?.();
      for (const group of Object.values(groups ?? {})) {
        this.chats.name(chatIdOf(group.id), group.subject);
        if (chatIdOf(group.id)) this.chats.touch(chatIdOf(group.id), { timestamp: toNumber(group.creation) });
      }
    } catch (error) {
      log.debug({ session: this.id, err: error?.message }, 'could not load group names');
    }
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
      void this.loadGroupNames();
    }
    if (connection === 'close') {
      const code = lastDisconnect?.error?.output?.statusCode;
      if (this.stopped) return;
      if (code === DisconnectReason.loggedOut) {
        this.state = 'logged_out';
        this.qr = null;
        log.warn({ session: this.id }, 'whatsapp session was logged out from the phone');
        clearInterval(this.saveTimer);
        this.chats = new ChatBook();
        this.watched = new Set();
        this.observed = new Inbox(2_000);
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

  async send(chatId, text) {
    if (this.state !== 'open' || !this.socket) throw new HttpError(409, 'WhatsApp is not connected.');
    let jid;
    try {
      jid = jidFromChatId(chatId);
    } catch {
      throw new HttpError(400, 'Invalid chat.');
    }
    const result = await this.socket.sendMessage(jid, { text });
    this.sent.add(result?.key?.id);
    return result?.key?.id ?? null;
  }

  async stop({ logout }) {
    if (!logout) await this.saveChats();
    this.stopped = true;
    clearTimeout(this.timer);
    clearInterval(this.saveTimer);
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
    let id;
    try {
      id = await session.send(body.chat ?? body.to, body.text);
    } catch (error) {
      if (error instanceof HttpError) throw error;
      throw new HttpError(502, error?.message ?? 'Send failed.');
    }
    return { ok: true, id };
  }
  if (action === 'chats' && request.method === 'GET') return { chats: session.chats.list().filter((chat) => chat.id !== session.phone) };
  if (action === 'watch' && request.method === 'GET') return { chats: [...session.watched] };
  if (action === 'watch' && request.method === 'PUT') {
    const body = await readJson(request);
    return { watched: await session.setWatched(body.chats) };
  }
  if (action === 'observed' && request.method === 'GET' && !parts[3]) return { messages: session.observed.list() };
  if (action === 'observed' && parts[3] === 'ack' && request.method === 'POST') {
    const body = await readJson(request);
    session.observed.ack(Array.isArray(body.ids) ? body.ids.map(String) : []);
    return { ok: true };
  }
  throw new HttpError(404, 'Not found.');
}

const CHATS_FILE = 'jarvis-chats.json';
const WATCH_FILE = 'jarvis-watch.json';
const APPEND_WINDOW_SECONDS = 48 * 3600;

/** Baileys timestamps can be numbers or protobuf Longs. */
function toNumber(value) {
  if (value == null) return 0;
  if (typeof value === 'number') return value;
  if (typeof value.toNumber === 'function') return value.toNumber();
  return Number(value) || 0;
}

async function readJsonFile(file) {
  try {
    return JSON.parse(await readFile(file, 'utf8'));
  } catch {
    return null;
  }
}

async function writeJsonFile(file, value) {
  const temp = `${file}.tmp`;
  await writeFile(temp, JSON.stringify(value));
  await rename(temp, file);
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
