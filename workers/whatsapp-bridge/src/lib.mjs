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

  push(message) {
    if (this.items.some((item) => item.id === message.id)) return;
    this.items.push(message);
    if (this.items.length > this.limit) this.items.splice(0, this.items.length - this.limit);
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

/** Text for the read-along history: the text, or "[Photo] caption" style placeholders for media. */
export function describeMessage(message) {
  const text = textOf(message);
  if (text !== null) return text.length > MAX_OBSERVED_TEXT ? text.slice(0, MAX_OBSERVED_TEXT) : text;
  if (!message || typeof message !== 'object') return null;
  const inner = message.ephemeralMessage?.message ?? message.viewOnceMessage?.message
    ?? message.viewOnceMessageV2?.message ?? message;
  for (const [key, label] of MEDIA_LABELS) {
    const media = inner[key];
    if (!media) continue;
    const caption = media.caption ?? media.message?.documentMessage?.caption ?? media.name ?? null;
    const poll = media.name && key.startsWith('poll') ? media.name : null;
    const extra = (poll ?? caption ?? '').toString().trim();
    return extra ? `[${label}] ${extra}`.slice(0, MAX_OBSERVED_TEXT) : `[${label}]`;
  }
  return null;
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
  let chatId = chatIdOf(key.remoteJid, resolvePhone);
  if (chatId !== null && isLidJid(chatId)) chatId = chatIdOf(key.remoteJidAlt, resolvePhone) ?? chatId;
  if (chatId === null || chatId === selfPhone || !watched.has(chatId)) return null;
  const text = describeMessage(entry.message);
  if (text === null) return null;
  const group = isGroupJid(chatId);
  const fromMe = Boolean(key.fromMe);
  let sender = null;
  if (!fromMe) {
    const name = typeof entry.pushName === 'string' ? entry.pushName.trim().slice(0, 80) : '';
    // Baileys 7 puts the other address in participantAlt: the phone when the group is LID-addressed,
    // and the LID when it is still phone-addressed. participantPn is the older field name.
    const phone = group
      ? phoneOf(key.participantAlt, resolvePhone) ?? phoneOf(key.participantPn, resolvePhone) ?? phoneOf(key.participant, resolvePhone)
      : chatId.startsWith('+') ? chatId : phoneOf(key.participantAlt, resolvePhone);
    sender = name || phone || null;
  }
  return {
    id: key.id,
    chatId,
    fromMe,
    sender,
    text,
    timestamp: Number(entry.messageTimestamp ?? 0) || Math.floor(Date.now() / 1000),
  };
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
  touch(chatId, { name = null, timestamp = 0 } = {}) {
    if (!chatId) return;
    let chat = this.chats.get(chatId);
    if (!chat) {
      chat = { id: chatId, name: this.names.get(chatId) ?? null, group: isGroupJid(chatId), lastMessageAt: 0 };
      this.chats.set(chatId, chat);
      this.dirty = true;
    }
    if (name) this.name(chatId, name);
    const at = Number(timestamp) || 0;
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
