// Confirms the API is serving the versioned Flutter web release.
// Cloudflare keeps stable .js URLs for four hours, so /main.dart.js must not exist.
const origin = (process.argv[2] || "http://127.0.0.1:5082").replace(/\/$/, "");

(async () => {
  const entry = await fetch(`${origin}/`);
  const html = await entry.text();
  const cache = entry.headers.get("cache-control") || "";
  if (!entry.ok || !html.includes("flutter_bootstrap.js")) {
    throw new Error("Web entry document is unavailable");
  }
  if (!cache.includes("no-cache")) {
    throw new Error("Web entry document is cacheable");
  }
  const match = html.match(/<base href="(\/r\/[A-Za-z0-9._-]{7,64}\/)">/);
  if (!match) {
    throw new Error("Web release path is missing");
  }
  const bundle = await fetch(`${origin}${match[1]}main.dart.js`, { method: "HEAD" });
  const type = bundle.headers.get("content-type") || "";
  if (!bundle.ok || !type.includes("javascript")) {
    throw new Error("Web client bundle is unavailable");
  }
  const stable = await fetch(`${origin}/main.dart.js`, { method: "HEAD" });
  if (stable.ok) {
    throw new Error("Stable web bundle URL is still published");
  }
})().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
