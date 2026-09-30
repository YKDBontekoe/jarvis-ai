import assert from 'node:assert/strict';
import test from 'node:test';
import { Inbox, RecentIds, inboundFrom, jidFromPhone, phoneFromJid, safeSessionId, textOf } from '../src/lib.mjs';

const self = '+31612345678';
const entry = (over = {}) => ({
  key: { remoteJid: '31687654321@s.whatsapp.net', id: 'A1', fromMe: false, ...over.key },
  message: { conversation: 'hello' },
  messageTimestamp: 1_700_000_000,
  ...over.rest,
});

test('phone <-> jid conversion', () => {
  assert.equal(phoneFromJid('31612345678:12@s.whatsapp.net'), '+31612345678');
  assert.equal(phoneFromJid('123@lid'), null);
  assert.equal(phoneFromJid('123@g.us'), null);
  assert.equal(jidFromPhone('+31 6 1234 5678'), '31612345678@s.whatsapp.net');
  assert.throws(() => jidFromPhone('12'));
});

test('textOf reads plain, extended and wrapped text only', () => {
  assert.equal(textOf({ conversation: 'hi' }), 'hi');
  assert.equal(textOf({ extendedTextMessage: { text: 'link' } }), 'link');
  assert.equal(textOf({ ephemeralMessage: { message: { conversation: 'poof' } } }), 'poof');
  assert.equal(textOf({ imageMessage: {} }), null);
  assert.equal(textOf({ conversation: '   ' }), null);
});

test('inbound messages from others are forwarded with a normalized phone', () => {
  const result = inboundFrom(entry(), { selfPhone: self, sentIds: new RecentIds() });
  assert.deepEqual(result, { id: 'A1', from: '+31687654321', text: 'hello', timestamp: 1_700_000_000 });
});

test('groups, status and media are ignored', () => {
  const opts = { selfPhone: self, sentIds: new RecentIds() };
  assert.equal(inboundFrom(entry({ key: { remoteJid: '1-2@g.us' } }), opts), null);
  assert.equal(inboundFrom(entry({ key: { remoteJid: 'status@broadcast' } }), opts), null);
  assert.equal(inboundFrom(entry({ rest: { message: { imageMessage: {} } } }), opts), null);
});

test('self chat is accepted, own messages to others and bridge echoes are not', () => {
  const sent = new RecentIds();
  const opts = { selfPhone: self, sentIds: sent };
  assert.equal(inboundFrom(entry({ key: { remoteJid: '31612345678@s.whatsapp.net', fromMe: true } }), opts)?.from, self);
  assert.equal(inboundFrom(entry({ key: { fromMe: true } }), opts), null);
  sent.add('A1');
  assert.equal(inboundFrom(entry({ key: { remoteJid: '31612345678@s.whatsapp.net', fromMe: true } }), opts), null);
});

test('lid chats resolve through the alternate jid or the mapping', () => {
  const opts = { selfPhone: self, sentIds: new RecentIds() };
  const viaAlt = entry({ key: { remoteJid: '99@lid', remoteJidAlt: '31687654321@s.whatsapp.net' } });
  assert.equal(inboundFrom(viaAlt, opts)?.from, '+31687654321');
  const viaMap = entry({ key: { remoteJid: '99@lid' } });
  assert.equal(inboundFrom(viaMap, { ...opts, resolvePhone: () => '31687654321@s.whatsapp.net' })?.from, '+31687654321');
  assert.equal(inboundFrom(viaMap, opts), null);
});

test('inbox keeps messages until acknowledged and dedupes by id', () => {
  const inbox = new Inbox(2);
  inbox.push({ id: '1' });
  inbox.push({ id: '1' });
  inbox.push({ id: '2' });
  inbox.push({ id: '3' });
  assert.deepEqual(inbox.list().map((m) => m.id), ['2', '3']);
  inbox.ack(['2']);
  assert.deepEqual(inbox.list().map((m) => m.id), ['3']);
});

test('session ids are restricted to safe characters', () => {
  assert.equal(safeSessionId('0198c3e0-aaaa-7bbb-8ccc-123456789abc'), '0198c3e0-aaaa-7bbb-8ccc-123456789abc');
  assert.equal(safeSessionId('../etc'), null);
});
