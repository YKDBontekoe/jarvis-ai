#!/usr/bin/env node
// "Ask Jarvis about this" on a file: a new chat with that file as its only source, and a profile that limits files
// to some collections is not widened by it.
import assert from 'node:assert/strict';

const base = process.env.JARVIS_BASE_URL ?? 'http://localhost:5082';
const api = `${base}/api/v1`;

async function request(method, path, body) {
  const response = await fetch(`${api}${path}`, {
    method,
    headers: body && !(body instanceof FormData) ? { 'Content-Type': 'application/json' } : undefined,
    body: body instanceof FormData ? body : body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  let data;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = text;
  }
  return { status: response.status, data };
}

import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';

const tag = Date.now().toString(36);

// A real upload goes through the virus scanner. Where ClamAV cannot load its signatures (a sandbox without internet),
// set JARVIS_E2E_PG_CONTAINER to the Postgres container and a stored-file row is inserted for the dev owner instead:
// attaching a file as a chat source needs the row, not the bytes.
async function storedFile() {
  const form = new FormData();
  form.append('file', new Blob([`The ${tag} proposal costs 4200 euro and starts in March.`], { type: 'text/plain' }), `proposal-${tag}.txt`);
  const upload = await request('POST', '/files', form);
  if (upload.status === 201) return upload.data.id;
  const container = process.env.JARVIS_E2E_PG_CONTAINER;
  assert.ok(container && upload.status === 503, `upload failed: ${JSON.stringify(upload.data)}`);
  const id = randomUUID();
  const password = execFileSync('docker', ['exec', container, 'printenv', 'POSTGRES_PASSWORD']).toString().trim();
  const owner = (await request('GET', '/conversations')).data[0]?.ownerId ?? '01996b8c-6000-7000-8000-000000000001';
  execFileSync('docker', ['exec', '-e', `PGPASSWORD=${password}`, container, 'psql', '-U', 'postgres', '-d', 'jarvis', '-c',
    `insert into files ("Id", owner_id, object_key, file_name, content_type, size_bytes, sha256, created_at, processing_status, partially_indexed) ` +
    `values ('${id}', '${owner}', 'seeded/${id}', 'proposal-${tag}.txt', 'text/plain', 10, '${'a'.repeat(64)}', now(), 'ready', false)`]);
  return id;
}
const fileId = await storedFile();

console.log('Asking about the file starts a chat with it attached...');
const ask = await request('POST', `/files/${fileId}/ask`, {});
assert.equal(ask.status, 201, JSON.stringify(ask.data));
assert.equal(ask.data.fileId, fileId);
assert.match(ask.data.fileName, new RegExp(`proposal-${tag}`));
assert.equal(ask.data.status, 'ready');
const sources = await request('GET', `/conversations/${ask.data.conversationId}/sources`);
assert.equal(sources.status, 200);
assert.deepEqual(sources.data.files.map((file) => file.fileId), [fileId]);

console.log('Detaching the source empties it...');
assert.equal((await request('DELETE', `/conversations/${ask.data.conversationId}/sources/files/${fileId}`)).status, 204);

console.log('An unknown file is a 404...');
assert.equal((await request('POST', '/files/00000000-0000-7000-8000-000000000000/ask', {})).status, 404);

console.log('A profile limited to other collections cannot read the file...');
const empty = await request('POST', '/collections', { name: `Other ${tag}`, fileIds: [] });
assert.equal(empty.status, 201, JSON.stringify(empty.data));
const profile = await request('POST', '/profiles', {
  name: `Limited ${tag}`,
  restrictFiles: true,
  allowedCollectionIds: [empty.data.id],
});
assert.equal(profile.status, 201, JSON.stringify(profile.data));
const refused = await request('POST', `/files/${fileId}/ask`, { profileId: profile.data.id });
assert.equal(refused.status, 409, JSON.stringify(refused.data));
assert.match(refused.data.detail, /cannot read this file/);

console.log('...until the file is in one of its collections.');
const add = await request('PUT', `/collections/${empty.data.id}`, { name: `Other ${tag}`, fileIds: [fileId] });
assert.equal(add.status, 200, JSON.stringify(add.data));
const allowed = await request('POST', `/files/${fileId}/ask`, { profileId: profile.data.id });
assert.equal(allowed.status, 201, JSON.stringify(allowed.data));

await request('DELETE', `/conversations/${ask.data.conversationId}`);
await request('DELETE', `/conversations/${allowed.data.conversationId}`);
await request('DELETE', `/profiles/${profile.data.id}`);
await request('DELETE', `/collections/${empty.data.id}`);
await request('DELETE', `/files/${fileId}`); // a seeded row has no stored bytes, so this may be a no-op
console.log('ask_file_flow: OK');
