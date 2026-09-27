// Shared heuristic stand-in for Jarvis's reflection model, used by the fake Codex and OpenRouter servers.
// It learns only from explicit cues in the reviewed messages so tests stay deterministic.
export function reflect(requestJson) {
  let input;
  try { input = JSON.parse(requestJson); } catch { return { persona: [], skills: [], memories: [], insights: [] }; }
  const userText = (input.recent_messages ?? []).filter(m => m.role === 'user').map(m => m.content).join('\n');
  const persona = [];
  const skills = [];
  const memories = [];
  if (/\b(short|brief|concise|tl;?dr)\b/i.test(userText))
    persona.push({ category: 'format', statement: 'Keep replies short and skimmable, with the answer first.', confidence: 0.8 });
  if (/\bbullet|list\b/i.test(userText))
    persona.push({ category: 'format', statement: 'Use bullet lists when comparing options.', confidence: 0.7 });
  for (const item of input.reply_feedback ?? []) {
    if (item.rating === 'down' && item.note)
      persona.push({ category: 'other', statement: `Based on feedback: ${item.note}.`.replace(/\.\.$/, '.'), confidence: 0.75 });
  }
  if (/\btrip|travel|flight\b/i.test(userText))
    skills.push({ name: 'trip-planning', description: 'Plan trips the way this user prefers.',
      instructions: '1. Confirm dates and budget.\n2. Prefer trains for trips under five hours.\n3. Summarize options in a table.',
      reason: 'The user planned travel with me.' });
  const name = userText.match(/\bmy name is ([a-z]+)/i)?.[1];
  if (name) memories.push({ kind: 'fact', content: `The user's name is ${name[0].toUpperCase()}${name.slice(1)}.`, importance: 0.8, confidence: 0.95 });
  const city = userText.match(/\bI live in ([A-Z][a-zA-Z]+)/)?.[1];
  if (city) memories.push({ kind: 'fact', content: `The user lives in ${city}.`, importance: 0.7, confidence: 0.9 });
  return { persona, skills, memories, insights: [] };
}

// Heuristic stand-in for knowledge-graph extraction over stored memories.
export function extractGraph(requestJson) {
  let input;
  try { input = JSON.parse(requestJson); } catch { return { facts: [] }; }
  const facts = [];
  for (const memory of input.memories ?? []) {
    const content = String(memory.content ?? '');
    const add = (subject, subjectType, predicate, object, objectType, objectIsEntity, exclusive) =>
      facts.push({ memory: memory.index, subject, subjectType, predicate, object, objectType, objectIsEntity, exclusive,
        validFrom: null });
    let match;
    if ((match = content.match(/\b(?:lives|live|moved) (?:in|to) ([A-Z][\w-]+)/)))
      add('user', 'person', 'lives_in', match[1], 'place', true, true);
    if ((match = content.match(/\bname is ([A-Z][a-z]+)/)))
      add('user', 'person', 'name', match[1], null, false, true);
    if ((match = content.match(/\bworks? (?:at|for) ([A-Z][\w&-]+(?: [A-Z][\w&-]+)?)/)))
      add('user', 'person', 'works_at', match[1], 'organization', true, true);
    if ((match = content.match(/\b(sister|brother|partner|wife|husband|friend|daughter|son)(?: is| named|,)? ([A-Z][a-z]+)/i)))
      add('user', 'person', `has_${match[1].toLowerCase()}`, match[2], 'person', true, false);
    if ((match = content.match(/\b([A-Z][a-z]+)'s birthday is ([^.]+)/)))
      add(match[1], 'person', 'birthday', match[2].trim(), null, false, true);
    if ((match = content.match(/\b(?:likes?|loves?|enjoys?) ([a-z][\w -]{2,40}?)(?:\.|,|$)/i)))
      add('user', 'person', 'likes', match[1].trim(), 'topic', true, false);
  }
  return { facts };
}
