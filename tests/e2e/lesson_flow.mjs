#!/usr/bin/env node
// One saved note becomes a short lesson built only from its own sentences; answers are checked against that text,
// not knowing is welcome, a few chosen cards enter the existing due-card review, and a repeat adds no duplicates.
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
const content =
  `Sourdough starter ${tag} is a living mixture of flour and water that holds wild yeast and bacteria. ` +
  'Feeding the starter every day keeps the yeast active and the dough rising reliably. ' +
  'A warm kitchen speeds up fermentation, while a cold fridge slows fermentation down to a crawl. ' +
  'Bakers judge readiness by bubbles on the surface and a pleasant sour smell.';
const note = await request('POST', '/api/v1/library/notes', { title: `Sourdough basics ${tag}`, content });
assert.equal(note.status, 200, JSON.stringify(note.data));
const id = note.data.id;

console.log('A lesson quotes the saved text...');
const lesson = await request('GET', `/api/v1/library/${id}/lesson`);
assert.equal(lesson.status, 200, JSON.stringify(lesson.data));
assert.ok(lesson.data.points.length >= 3);
for (const point of lesson.data.points) assert.ok(content.includes(point.text), 'every point is a sentence from the text');
assert.ok(lesson.data.note.includes('Nothing here is invented'));
const blanks = lesson.data.questions.filter((question) => question.kind === 'blank');
assert.equal(blanks.length, 3);

console.log('Answers are checked against the text...');
const first = blanks[0];
const wrong = await request('POST', `/api/v1/library/${id}/lesson/check`, { questionId: first.id, answer: 'pineapple' });
assert.equal(wrong.data.verdict, 'incorrect');
assert.ok(content.includes(wrong.data.source.quote));
const right = await request('POST', `/api/v1/library/${id}/lesson/check`, { questionId: first.id, answer: wrong.data.expected });
assert.equal(right.data.verdict, 'correct');
const unsure = await request('POST', `/api/v1/library/${id}/lesson/check`, { questionId: first.id, answer: "I don't know" });
assert.equal(unsure.data.verdict, 'unsure');
assert.equal((await request('POST', `/api/v1/library/${id}/lesson/check`, { questionId: 'zz', answer: 'x' })).status, 400);

console.log('Chosen cards enter the existing review, once...');
const kept = await request('POST', `/api/v1/library/${id}/lesson/cards`, { questionIds: [blanks[0].id, blanks[1].id, 'e1'] });
assert.equal(kept.status, 200, JSON.stringify(kept.data));
assert.equal(kept.data.added, 2);
const again = await request('POST', `/api/v1/library/${id}/lesson/cards`, { questionIds: [blanks[0].id, blanks[1].id] });
assert.equal(again.data.added, 0);
assert.equal(again.data.skipped, 2);
const due = await request('GET', '/api/v1/library/cards/due?limit=50');
assert.ok(due.data.cards.some((card) => card.front.includes('_____') && card.back.includes('“')));
assert.equal((await request('POST', `/api/v1/library/${id}/lesson/cards`, { questionIds: ['e1'] })).status, 400);

console.log('Thin text gives no lesson, and other items are unknown...');
const thin = await request('POST', '/api/v1/library/notes', { title: `Tiny ${tag}`, content: 'Just a few words, nothing to teach.' });
assert.equal((await request('GET', `/api/v1/library/${thin.data.id}/lesson`)).status, 400);
assert.equal((await request('GET', `/api/v1/library/${crypto.randomUUID()}/lesson`)).status, 404);

await request('DELETE', `/api/v1/library/${id}`);
await request('DELETE', `/api/v1/library/${thin.data.id}`);
console.log('OK lesson_flow');
