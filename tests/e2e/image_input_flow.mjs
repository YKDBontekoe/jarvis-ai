#!/usr/bin/env node
// Images reach the model as images, not as extracted text: a photo sent with a message, an image file attached as a
// source, and a recent earlier photo shown again on a follow-up. Run on the fake Codex fixture, which answers
// "How many images do you see?" with the number of image inputs it received.
import assert from 'node:assert/strict';
import { deflateSync } from 'node:zlib';

const base = (process.env.JARVIS_API_URL ?? 'http://localhost:5082') + '/api/v1';

function png(rgb) {
  const w = 16, h = 16;
  const raw = Buffer.concat(Array.from({ length: h }, () => Buffer.concat([Buffer.from([0]), Buffer.from(Array(w).fill(rgb).flat())])));
  const crcTable = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = buffer => { let c = 0xffffffff; for (const byte of buffer) c = crcTable[(c ^ byte) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => {
    const body = Buffer.concat([Buffer.from(type), data]);
    const length = Buffer.alloc(4); length.writeUInt32BE(data.length);
    const checksum = Buffer.alloc(4); checksum.writeUInt32BE(crc(body));
    return Buffer.concat([length, body, checksum]);
  };
  const header = Buffer.alloc(13); header.writeUInt32BE(w, 0); header.writeUInt32BE(h, 4); header[8] = 8; header[9] = 2;
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}

async function request(method, path, body, headers = {}) {
  const response = await fetch(base + path, { method,
    headers: body && !(body instanceof FormData) ? { 'Content-Type': 'application/json', ...headers } : headers,
    body: body instanceof FormData ? body : body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(120_000) });
  const text = await response.text();
  let data; try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  return { status: response.status, data, text };
}

async function upload(name, rgb) {
  const form = new FormData();
  form.append('file', new Blob([png(rgb)], { type: 'image/png' }), name);
  const result = await request('POST', '/files', form);
  assert.equal(result.status, 201, `upload ${name}: ${result.text}`);
  return result.data.id;
}

const ask = (conversationId, content, imageFileIds) =>
  request('POST', `/conversations/${conversationId}/messages`, { content, ...(imageFileIds ? { imageFileIds } : {}) });
const question = 'How many images do you see?';

const tag = Date.now().toString(36);
const chat = (await request('POST', '/conversations', { title: `Images ${tag}` })).data;
const red = await upload(`red-${tag}.png`, [200, 30, 30]);
const green = await upload(`green-${tag}.png`, [30, 200, 30]);

console.log('A photo sent with the message is an image input...');
const first = await ask(chat.id, question, [red]);
assert.equal(first.status, 200, first.text);
assert.match(first.data.content, /I see 1 image\(s\): image\/png/, first.text);

console.log('A follow-up without a photo shows the earlier photo again...');
const followUp = await ask(chat.id, question);
assert.match(followUp.data.content, /I see 1 image\(s\)/, `earlier photo was not re-attached: ${followUp.text}`);

console.log('A new photo is what is asked about, so the old one stays out...');
const second = await ask(chat.id, question, [green]);
assert.match(second.data.content, /I see 1 image\(s\)/, second.text);

console.log('An image file attached as a source reaches the model as an image...');
const sourceChat = (await request('POST', '/conversations', { title: `Source ${tag}` })).data;
const none = await ask(sourceChat.id, question);
assert.match(none.data.content, /I see 0 image\(s\)/, none.text);
assert.equal((await request('POST', `/conversations/${sourceChat.id}/sources/files/${green}`)).status, 204);
const withSource = await ask(sourceChat.id, question);
assert.match(withSource.data.content, /I see 1 image\(s\): image\/png/, `source image not attached: ${withSource.text}`);

console.log('Detaching the source stops it...');
assert.equal((await request('DELETE', `/conversations/${sourceChat.id}/sources/files/${green}`)).status, 204);
const detached = await ask(sourceChat.id, question);
assert.match(detached.data.content, /I see 0 image\(s\)/, detached.text);

for (const id of [red, green]) await request('DELETE', `/files/${id}`);
console.log('image input flow OK');
