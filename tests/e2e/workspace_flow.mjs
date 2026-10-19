#!/usr/bin/env node
// Workspaces: a saved profile (and optional project) for a recurring job. A workspace carries no access of its
// own: launch returns the profile's scope summary and what to start; foreign or missing profiles and projects are
// refused; a deleted profile blocks launch with a reason; edit, delete and the 404s work.
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

const tag = `ws${Date.now().toString(36)}`;
console.log('Preparing a profile and a project...');
const profile = await request('POST', '/profiles', { name: `Research ${tag}`, memoryScope: 'pinned', allowRemember: false });
assert.equal(profile.status, 201, profile.text);
const project = await request('POST', '/projects', { name: `Reno ${tag}` });
assert.ok([200, 201].includes(project.status), project.text);

console.log('Creating a workspace...');
const created = await request('POST', '/workspaces', {
  name: `Weekly ${tag}`, profileId: profile.data.id, projectId: project.data.id, instruction: 'Summarise what changed',
});
assert.equal(created.status, 201, created.text);
const id = created.data.workspace.id;
assert.equal(created.data.profileName, `Research ${tag}`);
assert.equal(created.data.scope.memory, 'Only pinned memories');
assert.ok(created.data.scope.limits.includes('Cannot save new memories.'));

console.log('Validation...');
assert.equal((await request('POST', '/workspaces', { name: ' ', profileId: profile.data.id })).status, 400);
assert.equal((await request('POST', '/workspaces', { name: 'x', profileId: '00000000-0000-0000-0000-000000000000' })).status, 400);
assert.equal((await request('POST', '/workspaces', { name: 'x', profileId: profile.data.id, projectId: '00000000-0000-0000-0000-000000000000' })).status, 400);

console.log('Launching gives the app what to start...');
const launch = await request('POST', `/workspaces/${id}/launch`);
assert.equal(launch.status, 200, launch.text);
assert.equal(launch.data.profileId, profile.data.id);
assert.equal(launch.data.projectId, project.data.id);
assert.equal(launch.data.openingMessage, 'Summarise what changed');
const chat = await request('POST', '/conversations', { title: launch.data.title, profileId: launch.data.profileId, projectId: launch.data.projectId });
assert.equal(chat.status, 201, chat.text);
const list = await request('GET', '/workspaces');
assert.ok(list.data.find((w) => w.workspace.id === id).workspace.lastUsedAt);

console.log('Editing, and a deleted profile blocks launch...');
const edited = await request('PUT', `/workspaces/${id}`, { name: `Renamed ${tag}`, profileId: profile.data.id });
assert.equal(edited.data.workspace.name, `Renamed ${tag}`);
assert.equal(edited.data.workspace.projectId, null);
assert.equal((await request('DELETE', `/profiles/${profile.data.id}`)).status, 204);
const broken = (await request('GET', '/workspaces')).data.find((w) => w.workspace.id === id);
assert.match(broken.problem, /profile was deleted/);
assert.equal((await request('POST', `/workspaces/${id}/launch`)).status, 400);

console.log('Delete and 404s...');
assert.equal((await request('DELETE', `/workspaces/${id}`)).status, 204);
assert.equal((await request('DELETE', `/workspaces/${id}`)).status, 404);
assert.equal((await request('POST', `/workspaces/${id}/launch`)).status, 404);
console.log('workspace flow OK');
