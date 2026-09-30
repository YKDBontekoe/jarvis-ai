# Integrations, MCP, channels, and browser

## Integration credentials

- Stored per owner, encrypted with Data Protection.
- API never returns secret values—only provider id and field names.
- Flutter: `apps/mobile/lib/integrations_*.dart` and Settings → Integrations (token vault). **Chat is the primary place to add, authorize, pause, and remove MCP servers** via `OfferMcpSetup`, secret cards (`AskForMcpCredential`), and OAuth (`RequestMcpAuthorization`).

REST:

- `PUT /api/v1/integrations/{provider}/credentials/{secretName}` — single secret upsert
- `DELETE` variants for field or whole provider
- `GET /integrations/connections` — active MCP tool counts for the owner

### Packs and OAuth

- **Packs** — calendar/mail/contacts guided setup (`/integrations/packs`).
- **OAuth** — PKCE + dynamic client registration for MCP servers with authorize/token endpoints; paste fallback when not supported.

## MCP architecture

| Layer | Responsibility |
|-------|----------------|
| **Configuration** | `Mcp__Servers__*` in appsettings / env (host-level servers: GitHub, Home Assistant overlays) |
| **Owner registry** | `IUserMcpServerRegistry` — user-added HTTP/stdio servers |
| **Policy** | `IOwnerMcpPolicyStore` — enable/disable, allowlists |
| **Runtime** | `McpToolHost` — per agent run connections, credential injection, redaction |

Transports: **stdio** and **streamableHttp**. Allowlists are explicit tool names (or `*` up to 80 tools). Default = approval required; `AutoApprovedTools` for unattended tools only.

Agent management tools: list/discover/add/update/invoke plus chat setup (`OfferMcpSetup`, `AskForMcpCredential`, `InstallIntegrationPack`) — see [agent-tools.md](agent-tools.md).

Compose overlays:

- `docker-compose.github.yml` — official GitHub MCP server in API image
- `docker-compose.home-assistant.yml` — Streamable HTTP to HA `/api/mcp`

## Channels

- **WhatsApp (QR link, default)** — kind `whatsapp_linked`. Jarvis links as a companion device through the Baileys-based `workers/whatsapp-bridge` sidecar (`Channels__WhatsAppBridge__BaseUrl`, optional `__Token`); bundled in Aspire and both Compose files. No Meta account, webhook, or public URL needed.
- **Signal** — kind `signal`, signal-cli REST (`Channels__Signal__BaseUrl`); linked as a secondary device by QR code.
- **WhatsApp Cloud API (advanced)** — kind `whatsapp`, webhooks at `/api/v1/channels/whatsapp/{key}/webhook`.

### One-tap linking

`ChannelLinkService` (`Jarvis.Api/Channels`) drives both QR flows:

1. `POST /channels/link {kind, channelId?}` starts an attempt and returns `{linkId, state, qrImage}` (`qrImage` is a PNG data URL). Pass `channelId` to re-link an existing WhatsApp channel.
2. The app shows the QR and polls `GET /channels/link/{linkId}` (2 s). WhatsApp QR codes rotate; `state` is `waiting`, `linked`, `expired` or `failed`.
3. On scan the channel is created automatically: account = the scanned number, that number is the only allowed sender (so the owner can use "Message yourself"), notifications forward to it. Add more senders in the channel detail screen.

`GET /channels/providers` reports which flows the server supports. For WhatsApp the bridge session id equals the channel connection id; inbound messages are pulled by `WhatsAppLinkedReceiver` (poll + ack, deduped by external id), outbound go through `WhatsAppLinkedTransport`. Deleting the channel logs the device out via the bridge. Signal "Note to Self" sync messages are accepted as owner messages (`SignalReceiver.ParseEnvelopes`).

### Forwarding notifications and approvals

Per channel, `forwardNotifications` turns forwarding on and `notificationCategories` chooses what is sent (`ChannelNotificationCategories`): `reminders`, `tasks`, `briefings`, `watches`, `automations`, `learning`, `check_ins`, `approvals`. A channel with no stored list gets every category **except approvals** (opt-in). Omitting the field on `PUT /channels/{id}` keeps the stored choice. `ChannelNotificationForwarder` sends the notifications; it is best-effort and never blocks the in-app inbox or push.

Things a chat app cannot do are called out in the message itself, pointing the owner to the Jarvis app:

- **Tool approvals** started in the channel's own chat can be answered with YES/NO. Approvals from app chats or tasks, and all automation approvals, are forwarded as "can't be approved from WhatsApp — open the Jarvis app". Approvals already decided elsewhere are not forwarded.
- `/status` and a bare YES/NO mention approvals waiting only in the app instead of sending the reply to the agent as chat.
- **WhatsApp Cloud API** only allows free-form messages within 24 h of the owner's last message. When that window is closed the notifications are not sent; the channel records a failed message with an explanation (visible as the channel error) rather than dropping them silently. Linked WhatsApp and Signal have no such window.

Allowlist per channel; replies approval-gated. Thread history in API + Flutter `features/channels/`.

## Browser (Playwright)

Opt-in Compose profile `docker-compose.browser.yml`:

- Isolated MCP browser container, no Docker socket, Squid egress filtering.
- `BrowseTheWeb` starts session; navigation/interaction approval-gated; read-only tools allowlisted.
- Timeline API: `/conversations/{id}/browser-sessions`.

## Agent2Agent

- Public card at `/.well-known/agent-card.json`.
- Inbound `POST /a2a` with hashed bearer tokens.
- Outbound delegation via `RemoteAgentToolContributor` after user approval.

## Device node

SignalR + REST invoke flow for clipboard, location, battery, open URL, local notifications. Capability toggles in Settings → This device. Telemetry: `POST /devices/telemetry`.

## Related security notes

[security-and-ownership.md](../architecture/security-and-ownership.md) — SSRF, redaction, fail-closed upload scanning.
