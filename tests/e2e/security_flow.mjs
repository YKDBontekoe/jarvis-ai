import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
const fixture = JSON.parse(readFileSync('artifacts/verification/identity-fixture.json'));
const origin = process.env.JARVIS_API_URL ?? 'http://localhost:5082';
async function request(token, method, path, data, expected = [200]) {
  const response = await fetch(origin + '/api/v1' + path, {method,
    headers: {'Content-Type': 'application/json', ...(token ? {Authorization: `Bearer ${token}`} : {})},
    body: data ? JSON.stringify(data) : undefined, signal: AbortSignal.timeout(60000)});
  assert(expected.includes(response.status), `${method} ${path}: ${response.status}`);
  const text = await response.text();
  return text ? JSON.parse(text) : null;
}
const owner = (await request(null, 'POST', '/auth/login', {email: fixture.email, password: fixture.password})).accessToken;
const other = (await request(null, 'POST', '/auth/login', {email: fixture.otherEmail, password: fixture.password})).accessToken;
const conversation = await request(owner, 'POST', '/conversations', {title: 'Security fixture'}, [201]);
const hub = new HubConnectionBuilder().withUrl(origin + '/hubs/events', {accessTokenFactory: () => other})
  .configureLogging(LogLevel.None).build();
try {
  await hub.start();
  await assert.rejects(hub.invoke('JoinConversation', conversation.id), /Conversation not found/);
  await request(owner, 'POST', `/conversations/${conversation.id}/messages`, {content: 'Remember that I like jazz'});
  const approvals = await request(owner, 'POST', `/conversations/${conversation.id}/messages`, {content: 'Forget jazz'}, [202]);
  const approval = approvals[0];
  await request(other, 'POST', `/approvals/${approval.id}/decision`, {approved: true}, [404]);
  assert((await request(owner, 'GET', '/memory')).some(memory => /jazz/i.test(memory.content)));
  await request(owner, 'POST', `/approvals/${approval.id}/decision`, {approved: false});
  await request(owner, 'POST', `/approvals/${approval.id}/decision`, {approved: true}, [200, 404, 409]);
  assert((await request(owner, 'GET', '/memory')).some(memory => /jazz/i.test(memory.content)), 'Rejected approval replay executed a deletion');
  writeFileSync('artifacts/ci/security-flow.json', JSON.stringify({checks: [
    {name: 'signalr-owner-isolation', status: 'passed'}, {name: 'foreign-approval-rejected', status: 'passed'},
    {name: 'rejected-approval-replay-has-no-side-effect', status: 'passed'}]}, null, 2));
} finally {
  await hub.stop();
  await request(owner, 'DELETE', `/conversations/${conversation.id}`, null, [204]);
}
console.log('Owner isolation and approval replay verified');
