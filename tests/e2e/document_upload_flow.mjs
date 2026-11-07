#!/usr/bin/env node
// Word, PowerPoint, TSV, YAML and calendar files can be uploaded, indexed by the worker and found by search; a file
// whose bytes do not match its declared type is refused. Uses the fixtures in fixtures/docs.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const base = (process.env.JARVIS_API_URL ?? 'http://localhost:5082') + '/api/v1';
const fixture = name => readFileSync(new URL(`./fixtures/docs/${name}`, import.meta.url));
async function request(method, path, body) {
  const response = await fetch(base + path, { method, body, signal: AbortSignal.timeout(120_000) });
  const text = await response.text();
  let data; try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  return { status: response.status, data, text };
}
async function upload(name, type, bytes = fixture(name)) {
  const form = new FormData();
  form.append('file', new Blob([bytes], { type }), name);
  return request('POST', '/files', form);
}
async function ready(id) {
  for (let i = 0; i < 60; i++) {
    const file = (await request('GET', '/files')).data.find(item => item.id === id);
    if (file?.processingStatus === 'ready') return;
    assert.notEqual(file?.processingStatus, 'failed', `indexing failed for ${id}`);
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  assert.fail(`file ${id} was not indexed in time`);
}
const search = async query => (await request('GET', `/files/search?query=${encodeURIComponent(query)}`)).data;

const cases = [
  ['roadmap.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'Zephyrcoat', /two ovens/],
  ['pitch.pptx', 'application/vnd.openxmlformats-officedocument.presentationml.presentation', 'Marmalade', /Marmalade launch plan/],
  ['schedule.tsv', 'text/tab-separated-values', 'Quillfeather', /Opening with Quillfeather/],
  ['settings.yaml', 'application/yaml', 'Brindlewick', /Brindlewick first/],
  ['opening.ics', 'text/calendar', 'Tastevale', /Tastevale tasting/],
];
const ids = [];
for (const [name, type, word, excerpt] of cases) {
  console.log(`${name} is indexed and found...`);
  const uploaded = await upload(name, type);
  assert.equal(uploaded.status, 201, `${name}: ${uploaded.text}`);
  ids.push(uploaded.data.id);
  await ready(uploaded.data.id);
  const hits = await search(word);
  const hit = hits.find(item => item.fileId === uploaded.data.id);
  assert.ok(hit, `${name}: no hit for ${word}: ${JSON.stringify(hits.map(item => item.fileName))}`);
  assert.match(hit.sanitizedExcerpt, excerpt, `${name}: ${hit.sanitizedExcerpt}`);
}

console.log('A table cell in a Word file is searchable...');
assert.ok((await search('18,400')).some(item => item.fileName === 'roadmap.docx'));

console.log('Bytes that are not a Word file are refused...');
const fake = await upload('fake.docx', cases[0][1], Buffer.from('this is plain text, not a zip archive'));
assert.equal(fake.status, 400, fake.text);

console.log('A zip that is not a Word document is accepted as a file but fails to index, not silently...');
const plainZip = await upload('notes.docx', cases[0][1], Buffer.concat([Buffer.from([0x50, 0x4b, 0x03, 0x04]), Buffer.alloc(40)]));
if (plainZip.status === 201) {
  let status;
  for (let i = 0; i < 30; i++) {
    status = (await request('GET', '/files')).data.find(item => item.id === plainZip.data.id)?.processingStatus;
    if (status === 'failed') break;
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  assert.equal(status, 'failed');
  ids.push(plainZip.data.id);
}

for (const id of ids) await request('DELETE', `/files/${id}`);
console.log('document upload flow OK');
