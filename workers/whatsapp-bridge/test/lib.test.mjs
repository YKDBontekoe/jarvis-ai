import assert from 'node:assert/strict';
import test from 'node:test';
import {
  ChatBook,
  Inbox,
  PictureCache,
  RecentIds,
  chatIdOf,
  describeMessage,
  inboundFrom,
  isChatId,
  jidFromChatId,
  jidFromPhone,
  normalizeWatchList,
  observedFrom,
  phoneFromJid,
  pictureUrlAllowed,
  safeSessionId,
  textOf,
} from '../src/lib.mjs';

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

test('chat ids: phones, groups and unresolved lids', () => {
  assert.equal(chatIdOf('31687654321@s.whatsapp.net'), '+31687654321');
  assert.equal(chatIdOf('120363025-1@g.us'), '120363025-1@g.us');
  assert.equal(chatIdOf('120363025123456789:0@g.us'), '120363025123456789@g.us');
  assert.equal(chatIdOf('120363025123456789_1@g.us'), '120363025123456789@g.us');
  assert.equal(chatIdOf('99887766:2@lid'), '99887766@lid');
  assert.equal(chatIdOf('99887766@lid', () => '31687654321@s.whatsapp.net'), '+31687654321');
  assert.equal(chatIdOf('status@broadcast'), null);
  assert.equal(chatIdOf('123@newsletter'), null);
  assert.ok(isChatId('+31687654321'));
  assert.ok(isChatId('120363025-1@g.us'));
  assert.ok(!isChatId('120363025123456789:0@g.us'));
  assert.ok(!isChatId('31687654321'));
  assert.ok(!isChatId('../etc'));
  assert.equal(jidFromChatId('+31687654321'), '31687654321@s.whatsapp.net');
  assert.equal(jidFromChatId('120363025-1@g.us'), '120363025-1@g.us');
  assert.throws(() => jidFromChatId('evil@s.whatsapp.net'));
});

test('describeMessage keeps text and labels media with their caption', () => {
  assert.equal(describeMessage({ conversation: 'hoi' }), 'hoi');
  assert.equal(describeMessage({ imageMessage: { caption: 'kijk' } }), '[Photo] kijk');
  assert.equal(describeMessage({ audioMessage: {} }), '[Voice message]');
  assert.equal(describeMessage({ protocolMessage: {} }), null);
  assert.equal(describeMessage({ reactionMessage: { text: '👍' } }), null);
});

test('observed messages: only watched chats, both directions, never the self chat', () => {
  const watched = new Set(['+31687654321', '120363025-1@g.us', self]);
  const opts = { selfPhone: self, watched };
  assert.deepEqual(observedFrom(entry({ rest: { pushName: 'Piet' } }), opts), {
    id: 'A1', chatId: '+31687654321', fromMe: false, sender: 'Piet', senderId: '+31687654321', text: 'hello', timestamp: 1_700_000_000,
  });
  const mine = observedFrom(entry({ key: { fromMe: true } }), opts);
  assert.equal(mine.fromMe, true);
  assert.equal(mine.sender, null);
  assert.equal(mine.senderId, null);
  assert.equal(observedFrom(entry({ key: { remoteJid: '31600000000@s.whatsapp.net' } }), opts), null);
  assert.equal(observedFrom(entry({ key: { remoteJid: '31612345678@s.whatsapp.net', fromMe: true } }), opts), null);
  const group = observedFrom(entry({
    key: { remoteJid: '120363025-1@g.us', participantPn: '31655555555@s.whatsapp.net' },
  }), opts);
  assert.equal(group.chatId, '120363025-1@g.us');
  assert.equal(group.sender, '+31655555555');
  assert.equal(group.senderId, '+31655555555');
  const lidWatched = new Set([...watched, '120363025123456789@g.us']);
  const lidGroup = observedFrom(entry({
    key: {
      remoteJid: '120363025123456789:0@g.us',
      participant: '999@lid',
      participantAlt: '31655555555@s.whatsapp.net',
    },
    rest: { pushName: '' },
  }), { ...opts, watched: lidWatched });
  assert.equal(lidGroup.chatId, '120363025123456789@g.us');
  assert.equal(lidGroup.sender, '+31655555555');
  const mapped = observedFrom(entry({
    key: { remoteJid: '120363025-1@g.us', participant: '999:2@lid' },
    rest: { pushName: '' },
  }), { ...opts, resolvePhone: (lid) => (lid === '999@lid' ? '31655555555@s.whatsapp.net' : null) });
  assert.equal(mapped.sender, '+31655555555');
  assert.equal(mapped.senderId, '+31655555555');
  assert.equal(observedFrom(entry(), { selfPhone: self, watched: new Set() }), null);
});

test('chat book merges names, tracks activity and sorts newest first', () => {
  const book = new ChatBook(3);
  book.name('+31687654321', 'Piet');
  book.touch('+31687654321', { timestamp: 10 });
  book.touch('120363025-1@g.us', { timestamp: 20 });
  book.name('120363025-1@g.us', 'Familie');
  assert.deepEqual(book.list().map((chat) => [chat.id, chat.name, chat.group]), [
    ['120363025-1@g.us', 'Familie', true],
    ['+31687654321', 'Piet', false],
  ]);
  book.touch('+31600000001', { timestamp: 30 });
  book.touch('+31600000002', { timestamp: 40 });
  assert.equal(book.list().length, 3);
  assert.ok(!book.list().some((chat) => chat.id === '+31687654321'));
  const copy = new ChatBook();
  copy.load(JSON.parse(JSON.stringify(book)));
  assert.deepEqual(copy.list(), book.list());
});

test('picture urls stay on WhatsApp hosts and the cache remembers a miss', () => {
  assert.equal(pictureUrlAllowed('https://pps.whatsapp.net/v/t61/abc'), true);
  assert.equal(pictureUrlAllowed('https://evil.example/pps.whatsapp.net'), false);
  assert.equal(pictureUrlAllowed('http://pps.whatsapp.net/a'), false);
  const cache = new PictureCache({ ttlMs: 1_000, missingTtlMs: 50, limit: 2 });
  assert.equal(cache.get('a', 0), undefined);
  cache.set('a', null, 0);
  assert.equal(cache.get('a', 10), null);
  assert.equal(cache.get('a', 60), undefined);
  cache.set('b', { bytes: Buffer.from('img'), type: 'image/jpeg' }, 0);
  assert.equal(cache.get('b', 10)?.type, 'image/jpeg');
});

test('watch lists keep valid chat ids only', () => {
  assert.deepEqual([...normalizeWatchList(['+31687654321', 'nope', '120363025-1@g.us', '+31687654321'])],
    ['+31687654321', '120363025-1@g.us']);
  assert.equal(normalizeWatchList(null).size, 0);
});
