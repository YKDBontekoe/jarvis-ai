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
