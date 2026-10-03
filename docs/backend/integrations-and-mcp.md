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

- **Packs** — calendar/mail/contacts guided setup (`/integrations/packs`). Suggested npm MCP packages in `IntegrationPackCatalog` are pinned to exact versions (a unit test enforces it); review a release before bumping. Contacts has no default package and needs an MCP endpoint.
- **OAuth** — PKCE + dynamic client registration for MCP servers with authorize/token endpoints; paste fallback when not supported.

## Adding an MCP server

Owners do not need to know an address or package first:

1. **App catalog** — Settings → Integrations → Add → **Browse apps** searches the official MCP registry
   (`registry.modelcontextprotocol.io`) through `GET /api/v1/mcp-catalog?search=`. Only entries Jarvis can run
   are listed: `streamable-http` remotes and npm / PyPI packages with a pinned version. A publisher's own server
   ranks first. Install is `POST /api/v1/mcp-catalog/install {name, option}`; the server fetches the entry again
   by name, so a client cannot substitute its own endpoint or package.
2. **Paste an address** — `POST /api/v1/mcp-servers/connect {endpoint}` names the server after its host.
3. **Chat** — "connect Notion" makes the agent call `SearchMcpCatalog` and `InstallMcpFromCatalog` (approval
   required), then ask for keys with `AskForMcpCredential` or start OAuth.

Install and connect return `{server, nextStep}` where `nextStep` is `ready`, `secrets` (the server lists required
keys in `server.secrets`) or `sign_in` (OAuth metadata was found). Every tool the server offers is enabled (`*`, up
to 80) and each call still asks for approval.

### Secrets for owner-added servers

A server definition can declare up to 10 secrets. Each binds an owner secret to an environment variable (stdio)
or an HTTP header (remote, optional prefix such as `Bearer`). Registry `environmentVariables` and header templates
like `Bearer {api_key}` become these bindings, with secret names derived from the variable (`BRAVE_API_KEY` →
`brave_api_key`). Values are saved with `PUT /integrations/{serverId}/credentials/{secretName}`, kept encrypted,
injected only when the server starts, and redacted from tool output. Variables and headers that change how code
loads or how requests route (`PATH`, `NODE_OPTIONS`, `LD_*`, `PYTHON*`, proxies, `Host`, `Cookie`, …) are refused.
A server missing a required secret reports `needs_credentials`.

Catalog npm packages run with `npx -y pkg@version`; PyPI packages run with `uvx pkg@version` (the API image ships
uv). Set `Mcp__Registry__BaseUrl` to an empty value to turn the catalog off, or to a mirror.

### MCP runner

Owner-installed npm and PyPI connectors do not run inside the API or worker when `McpRunner:Url` is set. The
`mcp-runner` service (the API image started as `Jarvis.Api.dll mcp-runner`) accepts one authenticated WebSocket
per connection (`Authorization: Bearer <McpRunner:Token>`). The first text message is the launch spec
`{command, arguments, environment}`. The runner validates it again (`npx`/`uvx` only, pinned package rules,
the secret variable blocklist) and starts the package with a fresh temporary home and a clean environment that
holds only that server's keys. MCP traffic then flows over the socket as it would over stdin/stdout. When the
socket closes, or after `McpRunner:MaxSessionMinutes`, the whole process tree is killed and the home deleted.
Connector stderr is discarded because packages may print their own keys.

`McpRunnerClientTransport` (`Jarvis.Mcp`) is the API side; `McpRunnerHost` (`Jarvis.Api/McpRunner`) is the
runner. Operator host binaries such as `github-mcp-server` still run in the API image.

In the production deployment the runner has no database networks and no Docker socket. It runs as `JARVIS_UID` on a
read-only root with dropped capabilities, a PID and memory limit, and tmpfs for work and package caches. It
reaches the internet through `mcp-egress`, and the API and worker reach it through the internal `mcp` network.
Aspire starts the same runner as the `mcp-runner` resource. Without `McpRunner:Url`, connectors run in-process
as before (tests).

## MCP architecture

| Layer | Responsibility |
|-------|----------------|
| **Configuration** | `Mcp__Servers__*` in appsettings / env (host-level servers: GitHub, Home Assistant overlays) |
| **Owner registry** | `IUserMcpServerRegistry` — user-added HTTP/stdio servers |
| **Policy** | `IOwnerMcpPolicyStore` — enable/disable, allowlists |
| **Runtime** | `McpToolHost` — per agent run connections, credential injection, redaction |

Transports: **stdio** and **streamableHttp**. Allowlists are explicit tool names (or `*` up to 80 tools). Default = approval required; `AutoApprovedTools` for unattended tools only.

Agent management tools: list/discover/add/update/invoke plus chat setup (`OfferMcpSetup`, `AskForMcpCredential`, `InstallIntegrationPack`) — see [agent-tools.md](agent-tools.md).

AppHost features (`JARVIS_FEATURES`, see `src/Jarvis.AppHost/JarvisFeatures.cs`):

- `github` — official GitHub MCP server in the API image
- `home-assistant` — Streamable HTTP to HA `/api/mcp` (`HOME_ASSISTANT_MCP_URL`)

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

### Personal WhatsApp for read along

Settings → WhatsApp & Signal → **Your WhatsApp · Read along** connects the owner's personal account alongside an existing Jarvis WhatsApp channel. Scan the code from the personal account's Linked devices settings. After linking, the app opens that account's chat picker; every chat starts with read along off.

The sidebar/drawer also has a **WhatsApp** destination for everyday use. It opens the personal account by default and offers an account picker when several accounts are linked. The account number and actual bridge connection state appear above the searchable chat list. Selected chats show previews of messages saved by Jarvis and unread counts; these counts are shared across Jarvis clients and do not change WhatsApp read receipts. Chats that have not been enabled expose names and activity only, without message previews.

Chat views can load older saved messages, retain loaded history during polling, and catch up across reconnects. Previous WhatsApp history is not imported; the picker and empty conversation state explain that collection begins after enabling read along. Attachments remain text descriptions rather than downloaded media. Cached chats and messages remain visible while reconnecting, with a warning instead of a false connected indicator.

The chat-list response includes `account`, `state`, `preview`, `previewFromMe`, and `unreadCount`; `live` is true only for an open bridge session. `GET /channels/{id}/chats/status` returns the account and state without QR codes or credentials. `POST /channels/{id}/chats/{chatId}/read {messageId}` advances the owner-scoped read watermark through a message the client loaded, keeping later arrivals unread. `GET /channels/{id}/chats/{chatId}/messages?beforeId={messageId}` pages by timestamp and message id together so equal timestamps do not skip messages. The existing timestamp-only `before` parameter remains supported; choose one cursor per request.

`POST /channels/link {kind: "whatsapp_linked", readAlong: true}` creates a new bridge session and owner-scoped connection named **My WhatsApp**, with no allowed channel senders and notification forwarding off. It does not change the existing Jarvis connection. The flag is only valid for a new WhatsApp connection; re-linking uses `channelId` without it and preserves settings. The usual link flow keeps its existing self-chat and notification defaults. Personal accounts can be paused, re-linked, or disconnected from their connection detail screen. Read-along replies still require tapping Send or approving an agent action.

### Forwarding notifications and approvals

Per channel, `forwardNotifications` turns forwarding on and `notificationCategories` chooses what is sent (`ChannelNotificationCategories`): `reminders`, `tasks`, `briefings`, `watches`, `automations`, `learning`, `check_ins`, `approvals`. A channel with no stored list gets every category **except approvals** (opt-in). Omitting the field on `PUT /channels/{id}` keeps the stored choice. `ChannelNotificationForwarder` sends the notifications; it is best-effort and never blocks the in-app inbox or push.

Things a chat app cannot do are called out in the message itself, pointing the owner to the Jarvis app:

- **Tool approvals** started in the channel's own chat can be answered with YES/NO. Approvals from app chats or tasks, and all automation approvals, are forwarded as "can't be approved from WhatsApp — open the Jarvis app". Approvals already decided elsewhere are not forwarded.
- `/status` and a bare YES/NO mention approvals waiting only in the app instead of sending the reply to the agent as chat.
- **WhatsApp Cloud API** only allows free-form messages within 24 h of the owner's last message. When that window is closed the notifications are not sent; the channel records a failed message with an explanation (visible as the channel error) rather than dropping them silently. Linked WhatsApp and Signal have no such window.

Allowlist per channel; replies approval-gated. Thread history in API + Flutter `features/channels/`.

## Browser (Playwright)

Opt-in AppHost feature `browser`:

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
