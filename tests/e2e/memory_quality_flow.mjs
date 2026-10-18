// Run only against a disposable Development API using fake_codex_app_server.mjs.
import assert from 'node:assert/strict';
const base = (process.env.JARVIS_API_URL ?? 'http://localhost:5082') + '/api/v1';
async function request(method, path, body) {
  const r = await fetch(base + path, { method,
    headers: body ? { 'Content-Type': 'application/json' } : {},
    body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(240_000) });
  const text = await r.text();
  assert(r.ok, `${method} ${path}: ${r.status} ${text}`);
  return text ? JSON.parse(text) : null;
}
const created = await request('POST', '/memory', { kind: 'fact',
  content: 'The user keeps a personal knowledge base for fixture verification.',
  importance: .7, confidence: .95, isPinned: false });
try {
  const dream = await request('POST', '/learning/dream');
  const status = await request('GET', '/learning/status');
  assert.equal(status.memoryReview.status, 'completed');
  assert.equal(status.memoryReview.model, 'gpt-6.1-sol');
  assert(status.memoryReview.memoryCount > 0);
  assert(Array.isArray(status.memoryReview.findings));
  assert(dream.diary.some(entry => entry.phase === 'review'));
  assert(status.dreaming.userSummary.length > 20);
  console.log(JSON.stringify({ passed: true, model: status.memoryReview.model,
    reviewed: status.memoryReview.memoryCount, findings: status.memoryReview.findings.length }));
} finally {
  await request('DELETE', `/memory/${created.id}`);
}
