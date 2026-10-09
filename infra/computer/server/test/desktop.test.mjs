import assert from 'node:assert/strict';
import test from 'node:test';
import { checkKeys, checkPoint, mouseButton, scrollButton, shell, truncate } from '../src/desktop.mjs';

test('points must be whole pixels on the screen', () => {
  const size = { width: 100, height: 50 };
  assert.doesNotThrow(() => checkPoint(0, 0, size));
  assert.doesNotThrow(() => checkPoint(99, 49, size));
  assert.throws(() => checkPoint(100, 0, size));
  assert.throws(() => checkPoint(-1, 0, size));
  assert.throws(() => checkPoint(1.5, 2, size));
});

test('keys accept xdotool chords and reject anything option-like', () => {
  assert.deepEqual(checkKeys(' ctrl+l Return '), ['ctrl+l', 'Return']);
  assert.throws(() => checkKeys('--window 1'));
  assert.throws(() => checkKeys('ctrl+l; rm -rf /'));
  assert.throws(() => checkKeys(''));
});

test('buttons and scroll directions map to X buttons', () => {
  assert.equal(mouseButton(undefined), '1');
  assert.equal(mouseButton('right'), '3');
  assert.equal(scrollButton('down'), '5');
  assert.throws(() => scrollButton('sideways'));
});

test('long output is truncated with a note', () => {
  assert.equal(truncate('short', 10), 'short');
  assert.match(truncate('x'.repeat(30), 10), /20 more characters truncated/);
});

test('shell runs a command and reports its exit code', async () => {
  const ok = await shell('echo hello', 5, { cwd: process.cwd(), env: process.env });
  assert.equal(ok.exitCode, 0);
  assert.equal(ok.output.trim(), 'hello');
  const failed = await shell('exit 3', 5, { cwd: process.cwd(), env: process.env });
  assert.equal(failed.exitCode, 3);
});

test('shell kills commands that run past the timeout', async () => {
  const result = await shell('sleep 30', 1, { cwd: process.cwd(), env: process.env });
  assert.equal(result.timedOut, true);
  assert.equal(result.exitCode, null);
});
