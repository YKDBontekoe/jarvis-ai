#!/usr/bin/env node
// Life stories: a recap is built from real decisions and journal moments with matching counts; money stays out
// unless chosen; wording edits and leaving moments out change only the story; the private save is one Library
// report per version; a shareable copy leaves out what was chosen; other kinds of input are refused.
import assert from 'node:assert/strict';

const api = `${process.env.JARVIS_BASE_URL ?? 'http://localhost:5082'}/api/v1`;

async function request(method, path, body) {
  const response = await fetch(`${api}${path}`, {
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
  return { status: response.status, data, text };
}

const tag = `st${Date.now().toString(36)}`;
const day = (offset) => new Date(Date.now() + offset * 86400000).toISOString().slice(0, 10);

console.log('Seeding a decision and a journal entry...');
const decision = await request('POST', '/decisions', {
  title: `Pick ${tag} supplier`,
  prediction: 'It arrives on time',
  probability: 0.7,
  reviewOn: day(5),
});
assert.equal(decision.status, 201, decision.text);
const journal = await request('POST', '/journal', {
  entryDate: day(-1),
  content: `Walked the canal ${tag} and felt calm.`,
});
assert.ok([200, 201].includes(journal.status), journal.text);

console.log('Drafting a recap...');
const created = await request('POST', '/stories', { from: day(-6), to: day(0), style: 'recap' });
assert.equal(created.status, 201, created.text);
const story = created.data;
assert.ok(story.items.some((i) => i.text.includes(`Pick ${tag} supplier`)), 'the decision is in the story');
assert.ok(story.items.some((i) => i.text.includes(`${tag}`) && i.kind === 'journal'), 'the journal moment is in');
assert.ok(story.items.every((i) => /^[a-z_]+:/.test(i.sourceRef)), 'every moment points at its record');
const total = Object.values(story.counts).reduce((a, b) => a + b, 0);
assert.equal(total, story.items.length, 'counts match the moments');
assert.ok(!story.kinds.includes('expense'), 'money is opt-in');

console.log('Editing, leaving out and reordering change only the story...');
const item = story.items.find((i) => i.kind === 'decision' && i.text.includes(tag));
const edited = await request('PUT', `/stories/${story.id}/items/${item.id}`, { text: `We chose the ${tag} supplier` });
assert.equal(edited.status, 200, edited.text);
const editedItem = edited.data.items.find((i) => i.id === item.id);
assert.equal(editedItem.text, `We chose the ${tag} supplier`);
assert.equal(editedItem.originalText, item.originalText);
const stillThere = await request('GET', `/decisions/${decision.data.id}`);
assert.equal(stillThere.status, 200, 'the decision record is untouched');
assert.equal(stillThere.data.title, `Pick ${tag} supplier`);

const journalItem = story.items.find((i) => i.kind === 'journal' && i.text.includes(tag));
const removed = await request('PUT', `/stories/${story.id}/items/${journalItem.id}/removed`, { removed: true });
assert.equal(removed.status, 200, removed.text);
assert.equal(removed.data.items.find((i) => i.id === journalItem.id).removed, true);
const badMove = await request('POST', `/stories/${story.id}/items/${item.id}/move`, { direction: 'sideways' });
assert.equal(badMove.status, 400, badMove.text);

console.log('Saving privately and exporting...');
const saved = await request('POST', `/stories/${story.id}/save`);
assert.equal(saved.status, 200, saved.text);
assert.equal(saved.data.savedVersion, saved.data.version);
assert.ok(saved.data.libraryItemId);
const again = await request('POST', `/stories/${story.id}/save`);
assert.equal(again.data.libraryItemId, saved.data.libraryItemId, 'saving twice does not duplicate');
const full = await request('GET', `/stories/${story.id}/export`);
assert.equal(full.status, 200);
assert.ok(full.text.includes(`We chose the ${tag} supplier`));
assert.ok(!full.text.includes('felt calm'), 'a removed moment is in no copy');
const shared = await request('GET', `/stories/${story.id}/export?redact=journal,people`);
assert.equal(shared.status, 200);
assert.ok(shared.text.includes('left out'), shared.text);
assert.equal((await request('GET', `/stories/${story.id}/export?redact=dreams`)).status, 400);

console.log('Refusing bad input...');
assert.equal((await request('POST', '/stories', { from: day(0), to: day(-3) })).status, 400);
assert.equal((await request('POST', '/stories', { from: day(-6), to: day(0), style: 'epic' })).status, 400);
assert.equal((await request('POST', '/stories', { from: day(-6), to: day(0), kinds: ['dreams'] })).status, 400);
assert.equal((await request('GET', '/stories/00000000-0000-0000-0000-000000000000')).status, 404);

const list = await request('GET', '/stories');
assert.ok(list.data.some((s) => s.id === story.id));
assert.equal((await request('DELETE', `/stories/${story.id}`)).status, 204);
assert.equal((await request('GET', `/stories/${story.id}`)).status, 404);
await request('DELETE', `/decisions/${decision.data.id}`);
console.log('story_flow ok');
