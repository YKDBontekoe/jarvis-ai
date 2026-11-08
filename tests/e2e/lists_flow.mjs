#!/usr/bin/env node
// Plain lists (shopping, packing): add items, tick them, clear the ticked ones, delete a list. No model needed.
import assert from 'node:assert/strict';

const base = (process.env.JARVIS_API_URL ?? 'http://localhost:5082') + '/api/v1';
async function request(method, path, body) {
  const response = await fetch(base + path, { method, headers: body ? { 'Content-Type': 'application/json' } : {},
    body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(30_000) });
  const text = await response.text();
  let data; try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  return { status: response.status, data, text };
}

const name = `Shopping ${Date.now().toString(36)}`;

console.log('Adding items creates the list and skips duplicates...');
let added = await request('POST', '/lists/items', { list: name, items: ['Milk', 'Eggs', 'milk'] });
assert.equal(added.status, 200, added.text);
assert.deepEqual(added.data.items.map(i => i.text), ['Milk', 'Eggs']);
added = await request('POST', '/lists/items', { list: name.toUpperCase(), items: ['Bread'] });
assert.equal(added.data.items.length, 3, 'the same list, found by name');

console.log('Ticking and clearing...');
const ticked = await request('POST', '/lists/done', { list: name, items: ['milk', 'bread'] });
assert.equal(ticked.status, 200, ticked.text);
assert.deepEqual(ticked.data.items.filter(i => i.done).map(i => i.text).sort(), ['Bread', 'Milk']);
const cleared = await request('POST', '/lists/clear-done', { list: name });
assert.deepEqual(cleared.data.items.map(i => i.text), ['Eggs']);

console.log('Renaming...');
const renamed = await request('POST', '/lists/rename', { list: name, newName: name + ' renamed' });
assert.equal(renamed.status, 200, renamed.text);
assert.equal(renamed.data.name, name + ' renamed');
assert.equal(renamed.data.items.length, 1);
assert.equal((await request('POST', '/lists/rename', { list: name + ' renamed', newName: '' })).status, 400);
const back = await request('POST', '/lists/rename', { list: name + ' renamed', newName: name });
assert.equal(back.data.name, name);

console.log('Bad input and unknown lists...');
assert.equal((await request('POST', '/lists/items', { list: '', items: ['x'] })).status, 400);
assert.equal((await request('POST', '/lists/items', { list: name, items: [' '] })).status, 400);
assert.equal((await request('POST', '/lists/done', { list: 'No such list zz9', items: ['x'] })).status, 404);

console.log('Deleting a list...');
const all = await request('GET', '/lists');
const mine = all.data.find(l => l.name === name);
assert.ok(mine, 'the list is listed');
assert.equal((await request('DELETE', `/lists/${mine.id}`)).status, 204);
assert.equal((await request('DELETE', `/lists/${mine.id}`)).status, 404);
console.log('lists flow OK');
