// Pure helpers for the bridge, kept free of I/O so they can be unit tested.

const USER_SERVER = 's.whatsapp.net';
const LID_SERVER = 'lid';

/** "31612345678:12@s.whatsapp.net" -> "+31612345678"; anything that is not a phone jid -> null. */
export function phoneFromJid(jid) {
  if (typeof jid !== 'string') return null;
  const [user, server] = jid.split('@');
  if (server !== USER_SERVER) return null;
  const digits = user.split(':')[0].replace(/\D/g, '');
  return digits.length >= 7 && digits.length <= 15 ? `+${digits}` : null;
}

/** "+31 6 1234 5678" -> "31612345678@s.whatsapp.net". */
export function jidFromPhone(phone) {
  const digits = String(phone ?? '').replace(/\D/g, '');
  if (digits.length < 7 || digits.length > 15) throw new Error('Invalid phone number.');
  return `${digits}@${USER_SERVER}`;
}

export function isLidJid(jid) {
  return typeof jid === 'string' && jid.endsWith(`@${LID_SERVER}`);
}

/**
 * Drops the device (`:12`) and domain-agent (`_1`) suffix Baileys leaves on a jid, and lowercases the server.
 * "31612345678:12@s.whatsapp.net" -> "31612345678@s.whatsapp.net".
 */
export function canonicalJid(jid) {
  if (typeof jid !== 'string') return null;
  const at = jid.lastIndexOf('@');
  if (at <= 0) return null;
  let user = jid.slice(0, at);
  const server = jid.slice(at + 1).toLowerCase();
  const device = user.indexOf(':');
  if (device > 0) user = user.slice(0, device);
  const agent = user.lastIndexOf('_');
  if (agent > 0 && /^\d+$/.test(user.slice(agent + 1))) user = user.slice(0, agent);
  return user && server ? `${user}@${server}` : null;
}

/** Plain text of a WhatsApp message, or null for media, reactions, protocol messages, etc. */
export function textOf(message) {
  if (!message || typeof message !== 'object') return null;
  const inner = message.ephemeralMessage?.message ?? message.viewOnceMessage?.message ?? message;
  const text = inner.conversation ?? inner.extendedTextMessage?.text ?? null;
  return typeof text === 'string' && text.trim().length > 0 ? text : null;
}

/**
 * Decides whether an upsert entry should reach Jarvis and who it is from.
 * - Group chats, broadcasts and status updates are ignored.
 * - Messages the user types in "Message yourself" arrive as fromMe; they are accepted, but only when
 *   they were not sent by this bridge (Jarvis' own replies land in the same chat).
 * - `resolvePhone` maps @lid identities to a phone jid when Baileys knows the mapping.
 */
export function inboundFrom(entry, { selfPhone, sentIds, resolvePhone = () => null }) {
  const key = entry?.key;
  if (!key?.remoteJid || !key.id) return null;
  if (sentIds?.has(key.id)) return null;
  const text = textOf(entry.message);
  if (text === null) return null;

  const chat = key.remoteJid;
  if (!chat.endsWith(`@${USER_SERVER}`) && !isLidJid(chat)) return null;

  const candidates = [chat, key.remoteJidAlt, key.senderPn, key.participantPn].filter(Boolean);
  let phone = null;
  for (const jid of candidates) phone ??= phoneFromJid(jid);
  if (phone === null && isLidJid(chat)) phone = phoneFromJid(resolvePhone(chat));
  if (phone === null) return null;

  if (key.fromMe && phone !== selfPhone) return null;
  return { id: key.id, from: phone, text, timestamp: Number(entry.messageTimestamp ?? 0) || Math.floor(Date.now() / 1000) };
}

/** Bounded queue that keeps unacknowledged inbound messages until Jarvis confirms them. */
export class Inbox {
  constructor(limit = 500) {
    this.limit = limit;
    this.items = [];
  }

  /** @returns {string[]} ids dropped because the queue was over its limit */
  push(message) {
    if (this.items.some((item) => item.id === message.id)) return [];
    this.items.push(message);
    if (this.items.length <= this.limit) return [];
    return this.items.splice(0, this.items.length - this.limit).map((item) => item.id);
  }

  list() {
    return [...this.items];
  }

  ack(ids) {
    const done = new Set(ids);
    this.items = this.items.filter((item) => !done.has(item.id));
  }
}

/** Remembers ids of messages the bridge sent, so echoes in the self chat are not treated as user input. */
export class RecentIds {
  constructor(limit = 200) {
    this.limit = limit;
    this.ids = new Set();
  }

  add(id) {
    if (!id) return;
    this.ids.add(id);
    if (this.ids.size > this.limit) this.ids.delete(this.ids.values().next().value);
  }

  has(id) {
    return this.ids.has(id);
  }
}

export function safeSessionId(value) {
  return /^[a-zA-Z0-9-]{8,64}$/.test(value ?? '') ? value : null;
}

// ---------------------------------------------------------------------------------------------------------------
// Personal assistant ("read along"): the owner turns chats on in Jarvis, and the bridge forwards only those chats.
// ---------------------------------------------------------------------------------------------------------------

const GROUP_SERVER = 'g.us';
const MAX_OBSERVED_TEXT = 8_000;
const GROUP_ID = /^[0-9-]{5,64}@g\.us$/;
const LID_ID = /^[0-9]{5,32}@lid$/;

export function isGroupJid(jid) {
  const canonical = canonicalJid(jid);
  return canonical !== null && canonical.endsWith(`@${GROUP_SERVER}`);
}

/**
 * The id Jarvis uses for a chat: "+31612345678" for a person (resolving @lid when possible), the group jid for a
 * group, or the @lid jid when the phone number is unknown. Broadcasts, status, newsletters and the like return null.
 */
export function chatIdOf(jid, resolvePhone = () => null) {
  const canonical = canonicalJid(jid);
  if (canonical === null) return null;
  if (canonical.endsWith(`@${GROUP_SERVER}`)) return GROUP_ID.test(canonical) ? canonical : null;
  const phone = phoneFromJid(canonical);
  if (phone) return phone;
  if (canonical.endsWith(`@${LID_SERVER}`)) {
    return phoneFromJid(resolvePhone(canonical)) ?? (LID_ID.test(canonical) ? canonical : null);
  }
  return null;
}

/**
 * The chat a message belongs to. A group id wins when WhatsApp puts the sender in `remoteJid` and the group in
 * `remoteJidAlt`, or the other way around. Direct chats keep the phone number, resolving an @lid when they can.
 */
export function messageChatId(key, resolvePhone = () => null) {
  if (!key) return null;
  const primary = chatIdOf(key.remoteJid, resolvePhone);
  const alt = chatIdOf(key.remoteJidAlt, resolvePhone);
  if (primary && isGroupJid(primary)) return primary;
  if (alt && isGroupJid(alt)) return alt;
  if (primary && isLidJid(primary) && alt) return alt;
  return primary ?? alt ?? null;
}

/** True for ids {@link chatIdOf} produces: "+<7-15 digits>", a group jid, or a bare @lid jid. */
export function isChatId(id) {
  if (typeof id !== 'string') return false;
  if (/^\+[1-9][0-9]{6,14}$/.test(id)) return true;
  return GROUP_ID.test(id) || LID_ID.test(id);
}

/** A phone number from a jid, or from the LID mapping when the jid is an @lid. */
function phoneOf(jid, resolvePhone) {
  const direct = phoneFromJid(jid);
  if (direct) return direct;
  if (typeof resolvePhone !== 'function' || !isLidJid(canonicalJid(jid))) return null;
  return phoneFromJid(resolvePhone(canonicalJid(jid)));
}

/** The WhatsApp jid to send to for a Jarvis chat id. */
export function jidFromChatId(chatId) {
  if (typeof chatId !== 'string') throw new Error('Invalid chat.');
  if (!isChatId(chatId)) throw new Error('Invalid chat.');
  return chatId.startsWith('+') ? jidFromPhone(chatId) : chatId;
}

const MEDIA_LABELS = [
  ['imageMessage', 'Photo'],
  ['videoMessage', 'Video'],
  ['audioMessage', 'Voice message'],
  ['documentMessage', 'Document'],
  ['documentWithCaptionMessage', 'Document'],
  ['stickerMessage', 'Sticker'],
  ['locationMessage', 'Location'],
  ['liveLocationMessage', 'Live location'],
  ['contactMessage', 'Contact card'],
  ['contactsArrayMessage', 'Contact cards'],
  ['pollCreationMessage', 'Poll'],
  ['pollCreationMessageV3', 'Poll'],
];

const MEDIA_KINDS = {
  imageMessage: 'image',
  videoMessage: 'video',
  audioMessage: 'audio',
  documentMessage: 'document',
  stickerMessage: 'sticker',
  locationMessage: 'location',
  liveLocationMessage: 'location',
  contactMessage: 'contact',
  contactsArrayMessage: 'contact',
  pollCreationMessage: 'poll',
  pollCreationMessageV3: 'poll',
};

const DOWNLOADABLE = new Set(['image', 'sticker', 'audio', 'document']);

/** Peels the wrappers Baileys puts around a normal message. */
function unwrapMessage(message) {
  let current = message;
  for (let depth = 0; depth < 4 && current && typeof current === 'object'; depth++) {
    const next = current.ephemeralMessage?.message
      ?? current.viewOnceMessage?.message
      ?? current.viewOnceMessageV2?.message
      ?? current.viewOnceMessageV2Extension?.message
      ?? current.documentWithCaptionMessage?.message
      ?? null;
    if (!next || typeof next !== 'object') break;
    current = next;
  }
  return current && typeof current === 'object' ? current : null;
}

function clip(value) {
  const text = String(value).trim();
  return text.length > MAX_OBSERVED_TEXT ? text.slice(0, MAX_OBSERVED_TEXT) : text;
}

function cleanText(value, limit) {
  if (typeof value !== 'string') return null;
  const text = value.replace(/\s+/g, ' ').trim();
  if (!text) return null;
  return text.length > limit ? text.slice(0, limit).trim() : text;
}

function cleanFileName(value) {
  const text = cleanText(value, 120);
  if (!text) return null;
  const base = text.split(/[/\\]/).pop();
  const safe = base.replace(/[^\p{L}\p{N} ._()-]/gu, '').trim();
  return safe.length > 0 ? safe.slice(0, 120) : null;
}

function cleanMime(value) {
  if (typeof value !== 'string') return null;
  const mime = value.split(';')[0].trim().toLowerCase();
  return /^[a-z0-9.+-]+\/[a-z0-9.+-]+$/.test(mime) ? mime : null;
}

function positiveInt(value) {
  const number = Number(value);
  return Number.isFinite(number) && number > 0 && number < 1_000_000 ? Math.round(number) : null;
}

function coordinate(value) {
  const number = Number(value);
  return Number.isFinite(number) && number >= -180 && number <= 180 ? Math.round(number * 1e6) / 1e6 : null;
}

function extraText(key, media) {
  if (key === 'contactMessage') return media.displayName;
  if (key === 'contactsArrayMessage') {
    const names = (Array.isArray(media.contacts) ? media.contacts : [])
      .map((contact) => contact?.displayName)
      .filter((name) => typeof name === 'string' && name.trim());
    return names.slice(0, 3).join(', ');
  }
  if (key.startsWith('poll')) return media.name;
  if (key === 'documentWithCaptionMessage') return media.message?.documentMessage?.caption ?? media.message?.documentMessage?.fileName;
  return media.caption ?? media.fileName ?? media.name ?? null;
}

/** Text for the read-along history: the text, or "[Photo] caption" style placeholders for media. */
export function describeMessage(message) {
  const text = textOf(message);
  if (text !== null) return text.length > MAX_OBSERVED_TEXT ? text.slice(0, MAX_OBSERVED_TEXT) : text;
  const inner = unwrapMessage(message);
  if (!inner) return null;
  for (const [key, label] of MEDIA_LABELS) {
    const media = inner[key];
    if (!media || typeof media !== 'object') continue;
    const named = key === 'videoMessage' && media.gifPlayback ? 'GIF' : label;
    const extra = extraText(key, media);
    const clean = typeof extra === 'string' ? extra.trim() : '';
    return clean ? clip(`[${named}] ${clean}`) : `[${named}]`;
  }
  return null;
}

/**
 * Structured description of a photo, sticker, video, voice note, document, location, contact or poll.
 * `download` says whether the bridge should fetch the file (`full`) or only the jpeg preview (`thumbnail`).
 */
export function mediaOf(message) {
  const inner = unwrapMessage(message);
  if (!inner) return null;
  for (const [key, kind] of Object.entries(MEDIA_KINDS)) {
    const media = inner[key];
    if (!media || typeof media !== 'object') continue;
    const gif = kind === 'video' && Boolean(media.gifPlayback);
    const resolved = gif ? 'gif' : kind;
    const options = kind === 'poll'
      ? (Array.isArray(media.options) ? media.options : [])
        .map((option) => cleanText(option?.optionName, 80))
        .filter(Boolean)
        .slice(0, 12)
      : [];
    const contacts = kind === 'contact'
      ? cleanText(media.displayName ?? media.contacts?.[0]?.displayName, 80)
      : null;
    return {
      kind: resolved,
      mime: cleanMime(media.mimetype),
      fileName: cleanFileName(media.fileName),
      seconds: positiveInt(media.seconds),
      width: positiveInt(media.width),
      height: positiveInt(media.height),
      animated: Boolean(media.isAnimated),
      voice: kind === 'audio' && Boolean(media.ptt),
      latitude: kind === 'location' ? coordinate(media.degreesLatitude) : null,
      longitude: kind === 'location' ? coordinate(media.degreesLongitude) : null,
      place: kind === 'location' ? cleanText(media.name ?? media.address, 120) : null,
      contactName: contacts,
      pollOptions: options,
      download: DOWNLOADABLE.has(resolved) ? 'full' : (resolved === 'video' || resolved === 'gif') ? 'thumbnail' : null,
    };
  }
  return null;
}

/** The message this one replies to, when WhatsApp included a quote. */
export function quoteOf(message) {
  const inner = unwrapMessage(message);
  if (!inner) return null;
  const info = inner.extendedTextMessage?.contextInfo
    ?? inner.imageMessage?.contextInfo
    ?? inner.videoMessage?.contextInfo
    ?? inner.stickerMessage?.contextInfo
    ?? inner.audioMessage?.contextInfo
    ?? inner.documentMessage?.contextInfo
    ?? null;
  if (!info?.quotedMessage) return null;
  const text = describeMessage(info.quotedMessage);
  if (!text) return null;
  return {
    author: phoneFromJid(info.participant) ?? cleanText(info.pushName, 80),
    text: text.length > 300 ? text.slice(0, 300) : text,
  };
}

const MEDIA_LIMITS = {
  image: 8_000_000,
  sticker: 1_500_000,
  audio: 8_000_000,
  document: 8_000_000,
  video: 200_000,
  gif: 200_000,
};

const OFFICE = new Set([
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'application/vnd.openxmlformats-officedocument.presentationml.presentation',
  'application/zip',
]);

function sniff(bytes) {
  if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) return 'image/jpeg';
  if (bytes.length >= 8 && bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47) return 'image/png';
  if (bytes.length >= 12 && bytes.toString('ascii', 0, 4) === 'RIFF' && bytes.toString('ascii', 8, 12) === 'WEBP') return 'image/webp';
  if (bytes.length >= 4 && bytes.toString('ascii', 0, 4) === 'OggS') return 'audio/ogg';
  if (bytes.length >= 3 && bytes.toString('ascii', 0, 3) === 'ID3') return 'audio/mpeg';
  if (bytes.length >= 2 && bytes[0] === 0xff && (bytes[1] & 0xe0) === 0xe0) return 'audio/mpeg';
  if (bytes.length >= 12 && bytes.toString('ascii', 4, 8) === 'ftyp') return 'audio/mp4';
  if (bytes.length >= 5 && bytes.toString('ascii', 0, 5) === '%PDF-') return 'application/pdf';
  if (bytes.length >= 4 && bytes[0] === 0x50 && bytes[1] === 0x4b && bytes[2] === 0x03 && bytes[3] === 0x04) return 'application/zip';
  return null;
}

/**
 * Accepts downloaded bytes only when the kind, the declared type and the file header agree.
 * Videos and GIFs keep their jpeg preview, never the full file.
 */
export function acceptMediaBytes(kind, declaredMime, bytes) {
  const buffer = Buffer.isBuffer(bytes) ? bytes : Buffer.from(bytes ?? []);
  const limit = MEDIA_LIMITS[kind];
  if (!limit || buffer.length < 8 || buffer.length > limit) return null;
  const sniffed = sniff(buffer);
  if (kind === 'image' && ['image/jpeg', 'image/png', 'image/webp'].includes(sniffed)) return { bytes: buffer, type: sniffed };
  if (kind === 'sticker' && sniffed === 'image/webp') return { bytes: buffer, type: 'image/webp' };
  if ((kind === 'video' || kind === 'gif') && sniffed === 'image/jpeg') return { bytes: buffer, type: 'image/jpeg' };
  if (kind === 'audio' && (sniffed === 'audio/ogg' || sniffed === 'audio/mpeg' || sniffed === 'audio/mp4')) {
    return { bytes: buffer, type: sniffed };
  }
  if (kind === 'document') {
    const declared = cleanMime(declaredMime);
    if (sniffed === 'application/pdf' && declared === 'application/pdf') return { bytes: buffer, type: declared };
    if (sniffed === 'application/zip' && declared && OFFICE.has(declared)) return { bytes: buffer, type: declared };
    if (declared === 'text/plain' && !buffer.includes(0)) {
      const head = buffer.subarray(0, 64).toString('utf8').trimStart().toLowerCase();
      if (!head.startsWith('<')) return { bytes: buffer, type: 'text/plain' };
    }
  }
  return null;
}

/** Jpeg preview embedded in a WhatsApp photo or video, when the full file is not what we keep. */
export function thumbnailBytes(message, kind) {
  const inner = unwrapMessage(message);
  const media = inner?.videoMessage ?? inner?.imageMessage ?? inner?.stickerMessage ?? null;
  const thumb = media?.jpegThumbnail;
  if (!thumb) return null;
  const bytes = Buffer.isBuffer(thumb) ? thumb : Buffer.from(thumb);
  return acceptMediaBytes(kind === 'gif' || kind === 'video' ? kind : 'image', 'image/jpeg', bytes);
}

/**
 * Decides whether an upsert entry belongs to a chat the owner turned on, and returns it for Jarvis.
 * Unlike {@link inboundFrom}, the owner's own messages (typed on the phone or sent through Jarvis) are kept, so
 * Jarvis sees both sides of the conversation. The self chat is never observed: that is where the owner talks to
 * Jarvis itself.
 */
export function observedFrom(entry, { selfPhone, watched, resolvePhone = () => null }) {
  const key = entry?.key;
  if (!key?.remoteJid || !key.id || !watched || watched.size === 0) return null;
  const canonical = messageChatId(key, resolvePhone);
  if (canonical === null || canonical === selfPhone) return null;
  // Keep the owner's saved id when WhatsApp later resolves a LID to its phone number. Group ids never
  // fall back to a participant's direct chat; a watch choice applies to the actual conversation only.
  const candidates = isGroupJid(canonical) ? [canonical] : [canonical,
    chatIdOf(key.remoteJid), chatIdOf(key.remoteJidAlt),
    ...[...watched].filter((id) => !isGroupJid(id) && chatIdOf(id, resolvePhone) === canonical)];
  const chatId = candidates.find((id) => id && watched.has(id));
  if (!chatId) return null;
  const text = describeMessage(entry.message);
  if (text === null) return null;
  const media = publishedMedia(mediaOf(entry.message));
  const quote = quoteOf(entry.message);
  const group = isGroupJid(chatId);
  const fromMe = Boolean(key.fromMe);
  let sender = null;
  let senderId = null;
  if (!fromMe) {
    const name = typeof entry.pushName === 'string' ? entry.pushName.trim().slice(0, 80) : '';
    // Baileys 7 puts the other address in participantAlt: the phone when the group is LID-addressed,
    // and the LID when it is still phone-addressed. participantPn is the older field name.
    // When the group id arrived in remoteJidAlt, remoteJid itself is the sender.
    const phone = group
      ? phoneOf(key.participantAlt, resolvePhone) ?? phoneOf(key.participantPn, resolvePhone) ?? phoneOf(key.participant, resolvePhone)
        ?? phoneOf(key.remoteJid, resolvePhone)
      : chatId.startsWith('+') ? chatId : phoneOf(key.participantAlt, resolvePhone);
    sender = name || phone || null;
    senderId = phone;
    if (!senderId && group) {
      const lid = canonicalJid(key.participant);
      if (lid && isChatId(lid)) senderId = lid;
    }
  }
  const observed = {
    id: key.id,
    chatId,
    fromMe,
    sender,
    senderId,
    text,
    timestamp: Number(entry.messageTimestamp ?? 0) || Math.floor(Date.now() / 1000),
  };
  if (media) observed.media = media;
  if (quote) observed.quote = quote;
  return observed;
}

/** Drops the download instruction and empty fields. `hasContent` stays false until bytes are stored. */
function publishedMedia(media) {
  if (!media) return null;
  const published = { kind: media.kind, hasContent: false };
  for (const key of ['mime', 'fileName', 'seconds', 'width', 'height', 'latitude', 'longitude', 'place', 'contactName']) {
    if (media[key] != null) published[key] = media[key];
  }
  if (media.animated) published.animated = true;
  if (media.voice) published.voice = true;
  if (media.pollOptions?.length) published.pollOptions = media.pollOptions;
  return published;
}

/** Holds downloaded media until Jarvis has stored it and acknowledged the message. */
export class MediaBin {
  constructor(limitBytes = 80 * 1024 * 1024) {
    this.limitBytes = limitBytes;
    /** @type {Map<string, { bytes: Buffer, type: string }>} */
    this.items = new Map();
    this.total = 0;
  }

  put(id, file) {
    if (!id || !file?.bytes?.length || !file.type) return false;
    this.drop([id]);
    this.items.set(id, { bytes: file.bytes, type: file.type });
    this.total += file.bytes.length;
    while (this.total > this.limitBytes && this.items.size > 1) {
      this.drop([this.items.keys().next().value]);
    }
    return true;
  }

  get(id) {
    return this.items.get(id) ?? null;
  }

  drop(ids) {
    for (const id of ids ?? []) {
      const item = this.items.get(id);
      if (!item) continue;
      this.total -= item.bytes.length;
      this.items.delete(id);
    }
  }
}

/** Profile-picture URLs come from WhatsApp. Anything else is refused before the bridge downloads it. */
export function pictureUrlAllowed(value) {
  let url;
  try {
    url = new URL(String(value));
  } catch {
    return false;
  }
  if (url.protocol !== 'https:' || url.username || url.password) return false;
  const host = url.hostname.toLowerCase();
  return host === 'whatsapp.net' || host.endsWith('.whatsapp.net') || host === 'fbcdn.net' || host.endsWith('.fbcdn.net');
}

/** Remembers profile pictures, and the fact that someone has none, so the phone is not asked on every scroll. */
export class PictureCache {
  constructor({ ttlMs = 12 * 60 * 60 * 1000, missingTtlMs = 60 * 60 * 1000, limit = 400 } = {}) {
    this.ttlMs = ttlMs;
    this.missingTtlMs = missingTtlMs;
    this.limit = limit;
    /** @type {Map<string, { until: number, image: { bytes: Buffer, type: string } | null }>} */
    this.items = new Map();
  }

  /** Undefined when unknown. Null when this id has no picture. */
  get(id, now = Date.now()) {
    const item = this.items.get(id);
    if (!item) return undefined;
    if (item.until <= now) {
      this.items.delete(id);
      return undefined;
    }
    return item.image;
  }

  set(id, image, now = Date.now()) {
    if (this.items.has(id)) this.items.delete(id);
    this.items.set(id, { until: now + (image ? this.ttlMs : this.missingTtlMs), image });
    while (this.items.size > this.limit) this.items.delete(this.items.keys().next().value);
  }
}

/**
 * The owner's chat list for the "read along" picker: id, display name, group flag and last activity. Fed from
 * history sync, chat/contact/group events and live messages; it holds names and numbers only, never message text.
 */
export class ChatBook {
  constructor(limit = 2_000) {
    this.limit = limit;
    /** @type {Map<string, {id: string, name: string | null, group: boolean, lastMessageAt: number}>} */
    this.chats = new Map();
    /** Contact names by chat id, kept even before a chat with them exists. */
    this.names = new Map();
    this.dirty = false;
  }

  load(saved) {
    for (const chat of Array.isArray(saved?.chats) ? saved.chats : []) {
      if (typeof chat?.id !== 'string') continue;
      this.chats.set(chat.id, {
        id: chat.id,
        name: typeof chat.name === 'string' ? chat.name : null,
        group: isGroupJid(chat.id),
        lastMessageAt: Number(chat.lastMessageAt) || 0,
        anchor: chat.anchor?.id && isChatId(chatIdOf(chat.anchor.remoteJid)) ? chat.anchor : undefined,
      });
    }
    for (const [id, name] of Object.entries(saved?.names ?? {})) if (typeof name === 'string') this.names.set(id, name);
  }

  toJSON() {
    return { chats: [...this.chats.values()], names: Object.fromEntries(this.names) };
  }

  /** Records a contact or group name for a chat id (a later, non-empty name wins). */
  name(chatId, name) {
    if (!chatId || typeof name !== 'string' || !name.trim()) return;
    const clean = name.trim().slice(0, 80);
    if (this.names.get(chatId) === clean) return;
    this.names.set(chatId, clean);
    const chat = this.chats.get(chatId);
    if (chat) chat.name = clean;
    this.dirty = true;
  }

  /** Records activity in a chat; `timestamp` is in seconds. */
  touch(chatId, { name = null, timestamp = 0, key = null } = {}) {
    if (!chatId) return;
    let chat = this.chats.get(chatId);
    if (!chat) {
      chat = { id: chatId, name: this.names.get(chatId) ?? null, group: isGroupJid(chatId), lastMessageAt: 0 };
      this.chats.set(chatId, chat);
      this.dirty = true;
    }
    if (name) this.name(chatId, name);
    const at = Number(timestamp) || 0;
    if (key?.id && at > 0 && at >= chat.lastMessageAt) {
      chat.anchor = { id: key.id, remoteJid: key.remoteJid, fromMe: Boolean(key.fromMe), timestamp: at };
      this.dirty = true;
    }
    if (at > chat.lastMessageAt) {
      chat.lastMessageAt = at;
      this.dirty = true;
    }
    if (this.chats.size > this.limit) {
      const oldest = [...this.chats.values()].sort((a, b) => a.lastMessageAt - b.lastMessageAt)[0];
      this.chats.delete(oldest.id);
    }
  }

  list() {
    return [...this.chats.values()]
      .map((chat) => ({ ...chat, name: chat.name ?? this.names.get(chat.id) ?? null }))
      .sort((a, b) => b.lastMessageAt - a.lastMessageAt);
  }
}

/** Normalizes a watch list from Jarvis: valid chat ids only, de-duplicated, capped. */
export function normalizeWatchList(ids, limit = 500) {
  if (!Array.isArray(ids)) return new Set();
  const valid = ids.filter(isChatId);
  return new Set(valid.slice(0, limit));
}
