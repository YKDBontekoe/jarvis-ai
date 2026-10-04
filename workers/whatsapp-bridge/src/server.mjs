import { createServer } from 'node:http';
import { mkdir, readFile, readdir, rename, rm, writeFile } from 'node:fs/promises';
import { timingSafeEqual } from 'node:crypto';
import path from 'node:path';
import makeWASocket, {
  Browsers,
  DisconnectReason,
  downloadMediaMessage,
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
  MediaBin,
  RecentIds,
  PictureCache,
  acceptMediaBytes,
  canonicalJid,
  chatIdOf,
  messageChatId,
  inboundFrom,
  isChatId,
  isGroupJid,
  jidFromChatId,
  mediaOf,
  normalizeWatchList,
  observedFrom,
  phoneFromJid,
  pictureUrlAllowed,
  safeSessionId,
  thumbnailBytes,
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
    this.media = new MediaBin();
    this.chats = new ChatBook();
    this.pictures = new PictureCache();
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
    const remember = (jid, name) => {
      const chatId = chatIdOf(jid);
      if (chatId) this.chats.name(chatId, name);
    };
    socket.ev.on('messages.upsert', ({ messages, type }) => {
      void this.ingest(messages, type).catch((error) =>
        log.warn({ session: this.id, errorType: error?.name }, 'could not read a WhatsApp message'));
    });
    socket.ev.on('messaging-history.set', ({ chats, contacts, messages }) => {
      void (async () => {
        await this.rememberDirectory(contacts, chats);
        await this.ingest(messages, 'history');
      })().catch((error) => log.warn({ session: this.id, errorType: error?.name }, 'could not import WhatsApp history'));
    });
    socket.ev.on('chats.upsert', (chats) => chats.forEach((chat) => this.addChat(chat)));
    socket.ev.on('chats.update', (chats) => chats.forEach((chat) => this.addChat(chat, () => null, false)));
    socket.ev.on('contacts.upsert', (contacts) => contacts.forEach((contact) => this.addContact(contact)));
    socket.ev.on('contacts.update', (contacts) => contacts.forEach((contact) => this.addContact(contact)));
    socket.ev.on('groups.upsert', (groups) => groups.forEach((group) => remember(group.id, group.subject)));
    socket.ev.on('groups.update', (groups) => groups.forEach((group) => remember(group.id, group.subject)));
  }

  async ingest(messages, type) {
    let forwarded = 0;
    const recent = Math.floor(Date.now() / 1000) - APPEND_WINDOW_SECONDS;
    const jids = [];
    for (const entry of messages ?? []) {
      const key = entry?.key ?? {};
      jids.push(key.remoteJid, key.remoteJidAlt, key.participant, key.participantAlt, key.participantPn);
    }
    jids.push(...this.watched);
    const resolvePhone = await phonesFor(this.socket, jids);
    for (const entry of messages ?? []) {
      const timestamp = toNumber(entry.messageTimestamp);
      const chatId = messageChatId(entry?.key, resolvePhone);
      if (chatId) {
        const personName = !entry.key.fromMe && !isGroupJid(chatId) ? entry.pushName : null;
        this.chats.touch(chatId, { name: personName, timestamp, key: entry.key });
      }
      // 'append' carries messages sent from the phone while this device was catching up; keep only recent ones.
      if (type !== 'history' && type !== 'notify' && !(type === 'append' && timestamp >= recent)) continue;
      const observed = observedFrom(entry, { selfPhone: this.phone, watched: this.watched, resolvePhone });
      if (observed) {
        observed.historical = type === 'history';
        await this.attachMedia(entry, observed);
        if (!this.watched.has(observed.chatId)) continue;
        const dropped = this.observed.push(observed);
        forwarded++;
        if (dropped) this.media.drop(dropped);
      }
      if (type !== 'notify') continue;
      const inbound = inboundFrom(entry, { selfPhone: this.phone, sentIds: this.sent, resolvePhone });
      if (inbound) this.inbox.push(inbound);
    }
    if (messages?.length) log.info({ session: this.id, type, received: messages.length, forwarded }, 'processed WhatsApp message batch');
  }

  async rememberDirectory(contacts, chats) {
    const jids = [];
    for (const contact of contacts ?? []) jids.push(contact?.phoneNumber, contact?.id, contact?.lid);
    for (const chat of chats ?? []) jids.push(chat?.id, chat?.lidJid, chat?.pnJid, chat?.accountLid);
    const resolvePhone = await phonesFor(this.socket, jids);
    for (const contact of contacts ?? []) this.addContact(contact, resolvePhone);
    for (const chat of chats ?? []) this.addChat(chat, resolvePhone);
  }

  async requestHistory(chatId, before) {
    if (!isChatId(chatId) || !this.watched.has(chatId)) throw new HttpError(403, 'Turn on read along for this chat first.');
    if (this.state !== 'open') throw new HttpError(409, 'WhatsApp is not connected.');
    const resolvePhone = await phonesFor(this.socket, [jidFromChatId(chatId)]);
    const directoryId = chatIdOf(jidFromChatId(chatId), resolvePhone);
    const anchor = before ?? this.chats.chats.get(chatId)?.anchor ?? this.chats.chats.get(directoryId)?.anchor;
    if (!anchor?.id || !/^[A-Za-z0-9_-]{6,80}$/.test(anchor.id) || !Number.isFinite(anchor.timestamp) || anchor.timestamp <= 0)
      throw new HttpError(409, 'The phone has not supplied a message for this chat yet. Send or receive a message, then retry history.');
    await this.socket.fetchMessageHistory(100, { remoteJid: jidFromChatId(chatId), id: anchor.id,
      fromMe: Boolean(anchor.fromMe) }, Math.floor(anchor.timestamp * 1000));
    return { state: 'requested' };
  }

  addChat(chat, resolvePhone = () => null, create = true) {
    const chatId = chatIdOf(chat?.id, resolvePhone);
    if (!chatId || chatId === this.phone) return;
    const name = chat?.name ?? chat?.subject ?? chat?.displayName ?? null;
    if (create || this.chats.chats.has(chatId))
      this.chats.touch(chatId, { name,
        timestamp: toNumber(chat.messages?.[0]?.message?.messageTimestamp) || toNumber(chat.conversationTimestamp),
        key: chat.messages?.[0]?.message?.key });
    else if (name) this.chats.name(chatId, name);
  }

  addContact(contact, resolvePhone = () => null) {
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
    const dropped = this.observed.items.filter((item) => !this.watched.has(item.chatId)).map((item) => item.id);
    this.observed.items = this.observed.items.filter((item) => this.watched.has(item.chatId));
    this.media.drop(dropped);
    await writeJsonFile(path.join(this.dir, WATCH_FILE), { chats: [...this.watched] });
    return this.watched.size;
  }

  async loadGroupNames() {
    try {
      const groups = await this.socket?.groupFetchAllParticipating?.();
      for (const group of Object.values(groups ?? {})) {
        const chatId = chatIdOf(group.id);
        this.chats.name(chatId, group.subject);
        if (chatId && !this.chats.chats.has(chatId)) this.chats.touch(chatId, { timestamp: toNumber(group.subjectTime) || toNumber(group.creation) });
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
        this.pictures = new PictureCache();
        this.watched = new Set();
        this.observed = new Inbox(2_000);
        this.media = new MediaBin();
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

  /** Downloads a photo, sticker, voice note or document, or keeps a video's jpeg preview. */
  async attachMedia(entry, observed) {
    const spec = mediaOf(entry?.message);
    if (!spec?.download || !observed.media) return;
    const file = spec.download === 'thumbnail'
      ? thumbnailBytes(entry.message, spec.kind)
      : await this.downloadMedia(entry, spec) ?? (spec.kind === 'image' ? thumbnailBytes(entry.message, 'image') : null);
    if (file && this.media.put(observed.id, file)) {
      observed.media.hasContent = true;
      observed.media.mime = file.type;
    }
  }

  async downloadMedia(entry, spec) {
    if (this.state !== 'open' || typeof this.socket?.updateMediaMessage !== 'function') return null;
    try {
      const buffer = await Promise.race([
        downloadMediaMessage(entry, 'buffer', {}, {
          logger: pino({ level: 'silent' }),
          reuploadRequest: (message) => this.socket.updateMediaMessage(message),
        }),
        new Promise((_, reject) => setTimeout(() => reject(new Error('timeout')), 15_000)),
      ]);
      return acceptMediaBytes(spec.kind, spec.mime, buffer);
    } catch (error) {
      log.debug({ session: this.id, err: error?.message }, 'could not download WhatsApp media');
      return null;
    }
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

  /** Preview profile picture for a chat or participant, or null when WhatsApp has none. */
  async picture(chatId) {
    if (!isChatId(chatId)) throw new HttpError(400, 'Invalid chat.');
    const cached = this.pictures.get(chatId);
    if (cached !== undefined) return cached;
    if (this.state !== 'open' || typeof this.socket?.profilePictureUrl !== 'function') {
      return null;
    }
    let url;
    try {
      url = await this.socket.profilePictureUrl(jidFromChatId(chatId), 'preview');
    } catch {
      url = undefined;
    }
    if (!pictureUrlAllowed(url)) {
      this.pictures.set(chatId, null);
      return null;
    }
    let response;
    try {
      response = await fetch(url, { signal: AbortSignal.timeout(8_000) });
    } catch {
      return null;
    }
    const type = String(response.headers.get('content-type') ?? '').split(';')[0].trim().toLowerCase();
    if (!response.ok || !['image/jpeg', 'image/png', 'image/webp'].includes(type)) {
      this.pictures.set(chatId, null);
      return null;
    }
    const bytes = Buffer.from(await response.arrayBuffer());
    if (bytes.length === 0 || bytes.length > 300_000) {
      this.pictures.set(chatId, null);
      return null;
    }
    const image = { bytes, type };
    this.pictures.set(chatId, image);
    return image;
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

class Picture {
  constructor(bytes, type) {
    this.bytes = bytes;
    this.type = type;
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
  if (action === 'picture' && request.method === 'GET') {
    const subject = url.searchParams.get('jid');
    const image = await session.picture(isChatId(subject) ? subject : chatIdOf(subject));
    if (!image) throw new HttpError(404, 'No picture.');
    return new Picture(image.bytes, image.type);
  }
  if (action === 'chats' && request.method === 'GET') return { chats: session.chats.list().filter((chat) => chat.id !== session.phone) };
  if (action === 'watch' && request.method === 'GET') return { chats: [...session.watched] };
  if (action === 'history' && request.method === 'POST') {
    const body = await readJson(request);
    return session.requestHistory(body.chatId, body.before);
  }
  if (action === 'watch' && request.method === 'PUT') {
    const body = await readJson(request);
    return { watched: await session.setWatched(body.chats) };
  }
  if (action === 'observed' && request.method === 'GET' && !parts[3]) return { messages: session.observed.list() };
  if (action === 'observed' && parts[3] === 'ack' && request.method === 'POST') {
    const body = await readJson(request);
    const ids = Array.isArray(body.ids) ? body.ids.map(String) : [];
    session.observed.ack(ids);
    session.media.drop(ids);
    return { ok: true };
  }
  if (action === 'media' && request.method === 'GET' && parts[3]) {
    if (!/^[A-Za-z0-9_-]{6,80}$/.test(parts[3])) throw new HttpError(400, 'Invalid message.');
    const file = session.media.get(decodeURIComponent(parts[3]));
    if (!file) throw new HttpError(404, 'No media.');
    return new Picture(file.bytes, file.type);
  }
  throw new HttpError(404, 'Not found.');
}

const CHATS_FILE = 'jarvis-chats.json';
const WATCH_FILE = 'jarvis-watch.json';
const APPEND_WINDOW_SECONDS = 48 * 3600;

/** Resolves @lid jids to phone jids. Baileys 7 only offers the async mapping. */
async function phonesFor(socket, jids) {
  const found = new Map();
  const lids = new Set();
  for (const jid of jids) {
    const canonical = canonicalJid(jid);
    if (canonical?.endsWith('@lid')) lids.add(canonical);
  }
  await Promise.all([...lids].map(async (lid) => {
    try {
      const pn = await socket?.signalRepository?.lidMapping?.getPNForLID?.(lid);
      if (typeof pn === 'string' && pn.length > 0) found.set(lid, pn);
    } catch {
      // The mapping store is empty until the phone finishes its first sync.
    }
  }));
  return (lid) => found.get(canonicalJid(lid) ?? '') ?? null;
}

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
    const result = await route(request);
    if (result instanceof Picture) {
      response.writeHead(200, {
        'content-type': result.type,
        'cache-control': 'private, max-age=3600',
        'content-length': result.bytes.length,
      });
      response.end(result.bytes);
      return;
    }
    reply(200, result);
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
