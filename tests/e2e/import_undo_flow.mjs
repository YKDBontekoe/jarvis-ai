#!/usr/bin/env node
// A statement import is a batch: importing the same file twice makes one result, and undo removes only the rows
// the import created that were never edited; edited rows and unrelated rows with the same merchant stay.
import assert from 'node:assert/strict';

const base = process.env.JARVIS_BASE_URL ?? 'http://localhost:5082';

async function request(method, path, body) {
  const response = await fetch(`${base}${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
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

const tag = Date.now().toString(36);
const shopA = `Bakker ${tag}`;
const shopB = `Kaas ${tag}`;
const day = new Date().toISOString().slice(0, 10);
const csv = `Date,Description,Amount\n${day},${shopA},-4.20\n${day},${shopB},-7.35\n`;

console.log('Preview creates nothing and has no batch...');
const preview = await request('POST', '/api/v1/finance/import', { csv, commit: false });
assert.equal(preview.status, 200, JSON.stringify(preview.data));
assert.equal(preview.data.batchId ?? null, null);

console.log('Committing twice makes one result...');
const first = await request('POST', '/api/v1/finance/import', { csv, commit: true });
assert.equal(first.data.imported, 2);
assert.ok(first.data.batchId);
const second = await request('POST', '/api/v1/finance/import', { csv, commit: true });
assert.equal(second.data.imported, 0);
assert.equal(second.data.duplicates, 2);
assert.equal(second.data.batchId ?? null, null);

const month = day.slice(0, 7);
const list = async () => (await request('GET', `/api/v1/expenses?month=${month}`)).data;
const rows = async () => {
  const data = await list();
  return (data.expenses ?? data.items ?? data).filter((row) => [shopA, shopB].includes(row.merchant));
};
let mine = await rows();
assert.equal(mine.length, 2, JSON.stringify(mine));

console.log('Editing one imported row and adding an unrelated one by hand...');
const edited = mine.find((row) => row.merchant === shopB);
const patch = await request('PUT', `/api/v1/expenses/${edited.id}`, {
  amount: 7.35,
  currency: edited.currency,
  merchant: shopB,
  category: edited.category,
  note: 'cheese for the party',
  spentOn: edited.spentOn,
});
assert.equal(patch.status, 200, JSON.stringify(patch.data));
const manual = await request('POST', '/api/v1/expenses', { amount: 9, currency: 'EUR', merchant: shopA, category: 'groceries', spentOn: day });
assert.equal(manual.status, 201, JSON.stringify(manual.data));

console.log('The batch shows what is still there and what was edited...');
const batches = await request('GET', '/api/v1/finance/imports');
const batch = batches.data.find((item) => item.id === first.data.batchId);
assert.equal(batch.stillThere, 2);
assert.equal(batch.edited, 1);

console.log('Undo removes only the untouched imported row...');
const undone = await request('POST', `/api/v1/finance/imports/${batch.id}/undo`);
assert.equal(undone.status, 200, JSON.stringify(undone.data));
assert.equal(undone.data.removed, 1);
assert.equal(undone.data.keptEdited, 1);
mine = await rows();
assert.deepEqual(mine.map((row) => [row.merchant, row.amount]).sort(), [[shopA, 9], [shopB, 7.35]].sort());

console.log('Undo twice or for a stranger changes nothing...');
assert.equal((await request('POST', `/api/v1/finance/imports/${batch.id}/undo`)).data.undone, true);
assert.equal((await request('POST', `/api/v1/finance/imports/${crypto.randomUUID()}/undo`)).status, 404);
assert.equal((await rows()).length, 2);

for (const row of await rows()) await request('DELETE', `/api/v1/expenses/${row.id}`);
console.log('OK import_undo_flow');
