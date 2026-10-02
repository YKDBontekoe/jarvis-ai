# WhatsApp bridge

Small Node service that links Jarvis to WhatsApp as a companion device using [Baileys](https://github.com/WhiskeySockets/Baileys) (WhatsApp Web protocol). Users link by scanning a QR code in the Jarvis app — no Meta developer account, webhook, or public URL.

Jarvis owns the UX (`POST /api/v1/channels/link`); this service is internal and should never be published.

| Method | Path | Purpose |
|--------|------|---------|
| `PUT` | `/sessions/{id}` | Start (or resume) a session; returns `{state, qr, phone}` |
| `GET` | `/sessions/{id}` | `state`: `none`, `connecting`, `qr`, `open`, `logged_out`; `qr` is a PNG data URL |
| `GET` | `/sessions/{id}/messages` | Buffered inbound text messages `{id, from, text, timestamp}` |
| `POST` | `/sessions/{id}/ack` | `{ids}` — drop delivered messages |
| `POST` | `/sessions/{id}/send` | `{to, text}` or `{chat, text}` (phone, `…@g.us` group or `…@lid`); returns `{id}` |
| `GET` | `/sessions/{id}/chats` | Chat list for the read-along picker `{id, name, group, lastMessageAt}` (names only, no text) |
| `GET`/`PUT` | `/sessions/{id}/watch` | `{chats}` — the chats the owner turned on in Jarvis; nothing else is forwarded |
| `GET` | `/sessions/{id}/observed` | Buffered messages of watched chats `{id, chatId, fromMe, sender, text, timestamp}` (media as `[Photo] caption`) |
| `POST` | `/sessions/{id}/observed/ack` | `{ids}` — drop stored read-along messages |
| `DELETE` | `/sessions/{id}` | Log out and delete the stored credentials |
| `GET` | `/health` | Liveness (no auth) |

Session ids are the Jarvis channel connection ids (UUIDs). For talking to Jarvis only one-to-one chats are forwarded; groups, status and media are ignored. Read along is separate: only chats on the watch list (`jarvis-watch.json` in the session folder) are forwarded, both directions, and the self chat never is. "Message yourself" is accepted as the owner talking, and messages the bridge itself sent are filtered out so replies never loop.

Environment: `PORT` (3000), `DATA_DIR` (`/data`, mount a volume — it holds the WhatsApp credentials), `BRIDGE_TOKEN` (optional bearer token), `LOG_LEVEL`.

```sh
npm ci && npm test   # unit tests for the message filtering logic
DATA_DIR=./data node src/server.mjs
```
