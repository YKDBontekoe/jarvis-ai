// Record a video and screenshots of the Flutter web build in headless Chromium.
//
//   flutter build web -t tool/ui_preview.dart --profile   # fixture data, no backend
//   node tool/record_web.mjs [--out build/recordings] [--width 393] [--height 852]
//                            [--steps "Chats,Everything,Home"]
//
// Each step taps the first element whose accessible name contains the text
// (Flutter semantics are switched on first) and saves a numbered screenshot.
// The run ends with a .webm video plus an .mp4 copy when ffmpeg is present.
import { execFileSync } from 'node:child_process';
import { createReadStream, existsSync, mkdirSync, readdirSync, renameSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { extname, join, resolve } from 'node:path';

const args = Object.fromEntries(
  process.argv.slice(2).reduce((pairs, arg, i, all) => {
    if (arg.startsWith('--')) pairs.push([arg.slice(2), all[i + 1]]);
    return pairs;
  }, []),
);
const webRoot = resolve(args.web ?? 'build/web');
const outDir = resolve(args.out ?? 'build/recordings');
const width = Number(args.width ?? 393);
const height = Number(args.height ?? 852);
const steps = (args.steps ?? 'Chats,Everything,Home').split(',').map((s) => s.trim()).filter(Boolean);

if (!existsSync(join(webRoot, 'index.html'))) {
  console.error(`No web build at ${webRoot}; run: flutter build web -t tool/ui_preview.dart --profile`);
  process.exit(1);
}

// Playwright ships globally in cloud sessions; fall back to a local install.
function loadPlaywright() {
  const roots = [process.cwd()];
  try {
    roots.push(execFileSync('npm', ['root', '-g'], { encoding: 'utf8' }).trim());
  } catch {}
  for (const root of roots) {
    try {
      return createRequire(join(root, 'noop.js'))('playwright');
    } catch {}
  }
  console.error('Playwright not found; install it with: npm i -g playwright');
  process.exit(1);
}
const { chromium } = loadPlaywright();

const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json',
  '.wasm': 'application/wasm', '.png': 'image/png', '.otf': 'font/otf', '.ttf': 'font/ttf', '.css': 'text/css' };
const server = createServer((req, res) => {
  let path = join(webRoot, decodeURIComponent(new URL(req.url, 'http://x').pathname));
  if (!path.startsWith(webRoot) || !existsSync(path) || statSync(path).isDirectory()) path = join(webRoot, 'index.html');
  res.writeHead(200, { 'content-type': types[extname(path)] ?? 'application/octet-stream' });
  createReadStream(path).pipe(res);
}).listen(0, '127.0.0.1');
await new Promise((r) => server.once('listening', r));
const url = `http://127.0.0.1:${server.address().port}/`;

mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader'] });
const context = await browser.newContext({
  viewport: { width, height },
  deviceScaleFactor: 2,
  // Flutter's intl rejects the empty default locale of headless Chromium.
  locale: args.locale ?? 'en-US',
  recordVideo: { dir: outDir, size: { width, height } },
});
const page = await context.newPage();
page.on('pageerror', (e) => console.error('page error:', e.message));

let shot = 0;
const capture = async (name) => {
  const file = join(outDir, `${String(++shot).padStart(2, '0')}-${name.replace(/\W+/g, '-').toLowerCase()}.png`);
  await page.screenshot({ path: file });
  console.log('screenshot', file);
};

await page.goto(url);
await page.waitForSelector('flutter-view, flt-glass-pane', { state: 'attached', timeout: 60_000 });
await page.waitForTimeout(2500);
// Flutter web renders to canvas; enabling semantics exposes accessible names.
await page.evaluate(() => document.querySelector('flt-semantics-placeholder')?.click());
await page.waitForTimeout(500);
await capture('start');

let failed = false;
for (const step of steps) {
  // Exact button names first (tab bar, toolbars), then any visible partial match.
  const exact = page.getByRole('button', { name: step, exact: true });
  const target = (await exact.count()) > 0
    ? exact.first()
    : page.locator(`[aria-label*="${step}" i]:visible, flt-semantics:visible:has-text("${step}")`).first();
  try {
    await target.click({ timeout: 5_000 });
  } catch {
    console.error(`step "${step}": no element with that accessible name`);
    failed = true;
    continue;
  }
  await page.waitForTimeout(1200);
  await capture(step);
}

const video = page.video();
await context.close();
await browser.close();
server.close();

const webm = join(outDir, 'recording.webm');
renameSync(await video.path(), webm);
console.log('video', webm);
try {
  execFileSync('ffmpeg', ['-y', '-loglevel', 'error', '-i', webm, '-pix_fmt', 'yuv420p', join(outDir, 'recording.mp4')]);
  console.log('video', join(outDir, 'recording.mp4'));
} catch {}
for (const f of readdirSync(outDir)) if (f.endsWith('.webm') && f !== 'recording.webm') console.log('extra', f);
process.exit(failed ? 1 : 0);
