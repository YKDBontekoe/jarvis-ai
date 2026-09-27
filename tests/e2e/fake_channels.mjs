#!/usr/bin/env node
// Deterministic stand-ins for Meta's WhatsApp Cloud API and signal-cli-rest-api used by channel tests.
// Point Channels__WhatsApp__GraphBaseUrl at http://localhost:5198/v21.0 and Channels__Signal__BaseUrl at
// http://localhost:5198. Test hooks under /_test inject inbound Signal messages and expose sent messages.
import http from 'node:http';

const PORT = Number(process.env.FAKE_CHANNELS_PORT ?? 5198);
const SIGNAL_ACCOUNT = process.env.FAKE_SIGNAL_ACCOUNT ?? '+31600000001';
// A 21x21 QR-style placeholder PNG so the app can render the Signal linking step.
const QR_PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAABUAAAAVAQAAAACo4fJpAAAAWklEQVR4nGP4z8DwnwGE/zMw/Idi' +
  'EPcBw38GBoZ/DP8ZGP4z/GdgYPjPwPCfgYHhPwPDfwYGhv8M/xkYGP4z/GdgYPjP8J+BgeE/w38G' +
  'Bob/DP8ZGBj+M/wHABWnD/HNtb7uAAAAAElFTkSuQmCC', 'base64');
const sent = [];
const signalInbox = [];

const server = http.createServer(async (request, response) => {
  const url = new URL(request.url, `http://localhost:${PORT}`);
  const body = request.method === 'POST' ? JSON.parse(await readBody(request) || '{}') : null;

  const graph = url.pathname.match(/^\/v\d+\.\d+\/(\d+)\/messages$/);
  if (graph && request.method === 'POST') {
    if (!/^Bearer EAA/.test(request.headers.authorization ?? ''))
      return json(response, 401, { error: { message: 'Invalid OAuth access token.' } });
    sent.push({ channel: 'whatsapp', from: graph[1], to: '+' + body.to, text: body.text?.body ?? '' });
    return json(response, 200, { messaging_product: 'whatsapp', messages: [{ id: 'wamid.' + sent.length }] });
  }
  if (url.pathname === '/v1/accounts') return json(response, 200, [SIGNAL_ACCOUNT]);
  if (url.pathname === '/v1/qrcodelink') {
    response.writeHead(200, { 'Content-Type': 'image/png' });
    return response.end(QR_PNG);
  }
  const receive = url.pathname.match(/^\/v1\/receive\/(.+)$/);
  if (receive) {
    const account = decodeURIComponent(receive[1]);
    const ready = signalInbox.filter(item => item.account === account);
    for (const item of ready) signalInbox.splice(signalInbox.indexOf(item), 1);
    return json(response, 200, ready.map(item => ({ envelope: {
      source: item.from, sourceNumber: item.from, timestamp: item.timestamp,
      dataMessage: { message: item.message, timestamp: item.timestamp } } })));
  }
  if (url.pathname === '/v2/send' && request.method === 'POST') {
    for (const recipient of body.recipients ?? [])
      sent.push({ channel: 'signal', from: body.number, to: recipient, text: body.message });
    return json(response, 201, { timestamp: String(Date.now()) });
  }
  if (url.pathname === '/_test/signal/inbound' && request.method === 'POST') {
    signalInbox.push({ account: body.account ?? SIGNAL_ACCOUNT, from: body.from, message: body.message,
      timestamp: Date.now() + signalInbox.length });
    return json(response, 202, { queued: signalInbox.length });
  }
  if (url.pathname === '/_test/sent') {
    if (request.method === 'DELETE') sent.length = 0;
    return json(response, 200, sent);
  }
  json(response, 404, { error: 'not found' });
});
server.listen(PORT, () => console.log(`fake WhatsApp + Signal listening on ${PORT}`));

function json(response, status, value) {
  response.writeHead(status, { 'Content-Type': 'application/json' });
  response.end(JSON.stringify(value));
}

function readBody(request) {
  return new Promise((resolve, reject) => {
    let data = '';
    request.on('data', part => { data += part; });
    request.on('end', () => resolve(data));
    request.on('error', reject);
  });
}
