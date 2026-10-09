// Desktop actions on the sandbox's X display. Every external program runs through execFile with an argument array,
// never a shell, so model-supplied text cannot become a command line (computer_shell is the one deliberate exception).
import { execFile, spawn } from 'node:child_process';

export const screen = {
  width: Number.parseInt(process.env.SCREEN_WIDTH ?? '1280', 10),
  height: Number.parseInt(process.env.SCREEN_HEIGHT ?? '800', 10),
};

export const MaxShellOutput = 16_000;
export const MaxTypedText = 2_000;

/** Throws unless (x, y) is a whole pixel on the virtual screen. */
export function checkPoint(x, y, size = screen) {
  if (!Number.isInteger(x) || !Number.isInteger(y) || x < 0 || y < 0 || x >= size.width || y >= size.height)
    throw new Error(`Coordinates must be whole pixels inside the ${size.width}x${size.height} screen.`);
}

/** xdotool key chords such as "ctrl+l", "Return" or "ctrl+shift+t Tab"; nothing that could be read as an option. */
export function checkKeys(keys) {
  const trimmed = typeof keys === 'string' ? keys.trim() : '';
  if (!/^[A-Za-z0-9_+]+( [A-Za-z0-9_+]+)*$/.test(trimmed) || trimmed.length > 100)
    throw new Error('Keys must be xdotool key names such as "Return", "ctrl+l" or "ctrl+shift+t", separated by spaces.');
  return trimmed.split(' ');
}

export function scrollButton(direction) {
  switch (direction) {
    case 'up': return '4';
    case 'down': return '5';
    case 'left': return '6';
    case 'right': return '7';
    default: throw new Error('Scroll direction must be up, down, left or right.');
  }
}

export function mouseButton(button) {
  switch (button ?? 'left') {
    case 'left': return '1';
    case 'middle': return '2';
    case 'right': return '3';
    default: throw new Error('Button must be left, middle or right.');
  }
}

export function truncate(text, limit = MaxShellOutput) {
  if (text.length <= limit) return text;
  return `${text.slice(0, limit)}\n… [${text.length - limit} more characters truncated]`;
}

function run(file, args, { timeout = 10_000, encoding = 'utf8', maxBuffer = 1024 * 1024 } = {}) {
  return new Promise((resolve, reject) => {
    execFile(file, args, { timeout, encoding, maxBuffer, env: process.env }, (error, stdout, stderr) => {
      if (error) reject(new Error(`${file} failed: ${(stderr || error.message).toString().trim()}`));
      else resolve(stdout);
    });
  });
}

export const xdotool = (...args) => run('xdotool', args);

export async function click(x, y, button, repeat = 1) {
  checkPoint(x, y);
  await xdotool('mousemove', '--sync', String(x), String(y), 'click', '--repeat', String(repeat), mouseButton(button));
}

export async function move(x, y) {
  checkPoint(x, y);
  await xdotool('mousemove', '--sync', String(x), String(y));
}

export async function drag(fromX, fromY, toX, toY) {
  checkPoint(fromX, fromY);
  checkPoint(toX, toY);
  await xdotool('mousemove', '--sync', String(fromX), String(fromY), 'mousedown', '1',
    'mousemove', '--sync', String(toX), String(toY), 'mouseup', '1');
}

export async function scroll(x, y, direction, amount = 3) {
  checkPoint(x, y);
  if (!Number.isInteger(amount) || amount < 1 || amount > 20) throw new Error('Scroll amount must be 1 to 20.');
  await xdotool('mousemove', '--sync', String(x), String(y), 'click', '--repeat', String(amount),
    scrollButton(direction));
}

export async function type(text) {
  if (typeof text !== 'string' || text.length === 0 || text.length > MaxTypedText)
    throw new Error(`Type 1 to ${MaxTypedText} characters.`);
  await xdotool('type', '--delay', '12', '--', text);
}

export async function key(keys) {
  await xdotool('key', '--delay', '40', '--', ...checkKeys(keys));
}

/** The whole screen as a JPEG, small enough to send to the model after every action. */
export async function screenshot() {
  const data = await run('import', ['-silent', '-window', 'root', '-quality', '75', 'jpeg:-'],
    { encoding: 'buffer', maxBuffer: 16 * 1024 * 1024 });
  return { data: data.toString('base64'), mimeType: 'image/jpeg' };
}

/** Runs one command as the sandbox user in its home directory; output is capped and the process group killed on timeout. */
export function shell(command, timeoutSeconds, { cwd, env }) {
  if (typeof command !== 'string' || command.trim().length === 0 || command.length > 4_000)
    throw new Error('The command must be 1 to 4,000 characters.');
  const seconds = Number.isInteger(timeoutSeconds) ? Math.min(Math.max(timeoutSeconds, 1), 120) : 30;
  return new Promise((resolve) => {
    const child = spawn('bash', ['-lc', command], { cwd, env, detached: true, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '';
    let timedOut = false;
    const append = (chunk) => {
      if (output.length < MaxShellOutput * 2) output += chunk.toString('utf8');
    };
    child.stdout.on('data', append);
    child.stderr.on('data', append);
    const timer = setTimeout(() => {
      timedOut = true;
      try { process.kill(-child.pid, 'SIGKILL'); } catch { /* already gone */ }
    }, seconds * 1000);
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve({ exitCode: timedOut ? null : code, timedOut, output: truncate(output) });
    });
    child.on('error', (error) => {
      clearTimeout(timer);
      resolve({ exitCode: null, timedOut: false, output: error.message });
    });
  });
}
