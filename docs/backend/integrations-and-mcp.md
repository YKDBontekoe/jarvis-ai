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

Before a search, the app shows `GET /api/v1/mcp-catalog/featured`: publishers' own registry entries grouped by
category (`McpFeaturedCatalog`), each fetched by name so an entry the registry drops disappears. Search results
and featured entries carry `installed` and `installedServerId` so the app opens an added app instead of adding
it twice.

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

### Checking a connection

`POST /api/v1/mcp-servers/{key}/test` (owner server id or host server name) connects once and returns
`{ok, state, toolCount, tools, errorKind, message}`. `errorKind` (`unauthorized`, `missing_key`, `timeout`,
`unreachable`, `crashed`, `runner_required`, `not_activated`, `unavailable`) never carries secret values or
response bodies. The app calls it right after a key is saved (`AppKeysDialog`, Settings key editor) and from the
**Check** button on an app's page, so a wrong key shows up at once.

### OAuth renewal

When the token response includes `refresh_token` and `expires_in`, `McpOAuthTokens` stores the refresh token,
token endpoint, client id, resource and expiry as secrets on the same provider (`oauth_*`, hidden in the app).
`McpToolHost` renews the token when it expires within two minutes, before connecting. A failed renewal reports
`needs_sign_in` (issue `sign_in_expired`) instead of `needs_credentials`. The token endpoint must be public HTTPS.

## Self-built apps

When nothing in the catalog fits and the service has an API, Jarvis can write the MCP server itself.

| Step | Tool / route | What happens |
|------|--------------|--------------|
| Write | `WriteCustomMcpServer` (no approval) | Validates and saves a draft revision (`custom_mcp_servers`, `custom_mcp_server_revisions`, last 10 kept, never the live one). The first save also creates a paused registry entry (command `jarvis-script`, catalog name `custom:<id>`) so keys can be added before approval. |
| Test | `TestCustomMcpServer` / `POST /custom-mcp-servers/{id}/test` (approval) | Runs the draft in the MCP runner, lists tools, optionally calls one. Returns the tool output and the last 4 KB of stderr with the server's key values removed, so Jarvis can fix its own code. Saved as `lastTest`. |
| Activate | `ActivateCustomMcpServer` / `POST …/activate {sha256}` (approval) | Makes the draft live only if its hash still matches what was reviewed, and switches the registry entry on. |

Rules (`CustomMcpScriptValidator`, checked on save, activation, and again in the runner):

- One Python file, at most 64 KB, with exactly one PEP 723 `# /// script` block holding only `requires-python`
  and `dependencies`. `[tool.uv]`, indexes, URLs, git and paths are refused.
- Every dependency pinned with `==` to one release, no extras or markers, and `mcp==2.2.0` (the mcp 2.x API:
  `from mcp.server.mcpserver import MCPServer`) among them. `CustomMcpTemplate` is the starting point; bump its
  pin after reading the SDK release notes.
- Keys are environment variables declared with the code; the usual variable blocklist applies.

Self-built apps run **only** in the MCP runner: without `McpRunner:Url` they report `runner_required` and never
start in the API or worker. The runner gets `{command: "uv", script: {language, source, sha256}}`, recomputes the
hash, writes `server.py` into the session's temporary home and runs `uv run --no-config --no-project --quiet
--script server.py` with the same clean environment and limits as registry packages. When the server stops on its
own, the runner sends one text frame `{type: "diagnostics", exitCode, stderrTail}` before closing; registry
packages still have their stderr dropped. Live servers run the approved `active_source`; editing only changes the
draft. Removing the registry entry (any screen or `RemoveMcpServer`) deletes the code too.

**Saved state (staying logged in).** A run ends when the agent turn ends, so module variables and files in the
temporary home are gone by the next tool call. A server that needs a session (a login, tokens, a device list) keeps
it in the file named by `JARVIS_STATE_FILE`. Flow: the API sends the last saved text in the launch (`state`); the
runner writes it to that file before starting the server; before forwarding each chunk of the server's output the
runner re-reads the file and, if it changed, sends a text frame `{type: "state", state}` ahead of the MCP reply.
`McpToolHost` keeps the newest value per server and saves it after each tool call and when the host is disposed as
the `server_state` secret on the server's own credential provider, so it is encrypted with the keys, owner-scoped
and removed with the server. A failed save is kept and retried unless a newer value arrived.

Rules for the file: one JSON object of at most 16,000 characters (`McpRunnerState.MaxLength`). Anything else (too
large, a write caught half way, not an object) is ignored and the previous state stays. A deleted file, an empty
file or `{}` clears the saved state. The state is never a declarable key, is not available as a `{{secret:…}}`
placeholder, and is not given to test runs of a draft. Registry packages get no state file.

How Jarvis learns about it: the template (`CustomMcpTemplate.Source`) has `load_state` / `save_state` (atomic
`os.replace`), and `Guidance` says to save before the tool returns and to treat missing or unfamiliar content as
logged out. `GetCustomMcpTemplate` returns both. Code that looks like it logs in (`login`, `session`, `token`
terms, …) but never mentions `JARVIS_STATE_FILE` gets a warning from `CustomMcpTemplate.StateAdvice` in the results
of `WriteCustomMcpServer`, `TestCustomMcpServer`, `ListCustomMcpServers` and `GetCustomMcpServerSource`, so an
existing server that loses its login is flagged without the user having to say so.

### MCP runner

Owner-installed npm and PyPI connectors do not run inside the API or worker when `McpRunner:Url` is set. The
`mcp-runner` service (the API image started as `Jarvis.Api.dll mcp-runner`) accepts one authenticated WebSocket
per connection (`Authorization: Bearer <McpRunner:Token>`). The first text message is the launch spec
`{command, arguments, environment}` (plus `script` and `state` for self-built servers). The runner validates it again (`npx`/`uvx` only, pinned package rules,
the secret variable blocklist) and starts the package with a fresh temporary home and a clean environment that
holds only that server's keys. MCP traffic then flows over the socket as it would over stdin/stdout. When the
socket closes, or after `McpRunner:MaxSessionMinutes`, the whole process tree is killed and the home deleted.
Connector stderr is discarded because packages may print their own keys.

`McpRunnerClientTransport` (`Jarvis.Mcp`) is the API side; `McpRunnerHost` (`Jarvis.Api/McpRunner`) is the
runner. Operator host binaries such as `github-mcp-server` still run in the API image.

In the production deployment the runner has no database networks and no Docker socket. It runs as `JARVIS_UID` on a
read-only root with dropped capabilities, a PID and memory limit, and tmpfs for work and package caches. It
reaches the internet through `mcp-egress`, and the API and worker reach it through the internal `mcp` network.
Its `/cache` and `/tmp` tmpfs mounts explicitly allow execution: npm package entrypoints, uv tools and downloaded
Python run from those directories. Docker's default `noexec` would make a downloaded connector stop with exit
code 126 before MCP initialization. This permission applies only to the runner; its root remains read-only and
it still drops all capabilities, runs as the service user, and uses `nosuid`, `nodev` and `no-new-privileges`.
Nonzero connector exit codes are logged without stderr or credential values so startup failures can be diagnosed.
Aspire starts the same runner as the `mcp-runner` resource. Without `McpRunner:Url`, connectors run in-process
as before (tests).

## MCP architecture

| Layer | Responsibility |
|-------|----------------|
| **Configuration** | `Mcp__Servers__*` in appsettings / env (host-level servers: GitHub, Home Assistant overlays) |
| **Owner registry** | `IUserMcpServerRegistry` — user-added HTTP/stdio servers |
| **Policy** | `IOwnerMcpPolicyStore` — enable/disable, allowlists |
| **Runtime** | `McpToolHost` — per agent run connections, credential injection, redaction |

Integration tools that are not host-auto-approved still ask by default, except that a tool whose server declares an explicit `readOnlyHint` (and no `destructiveHint`) runs without a card while the owner's `autoApproveMcpReadHints` autonomy switch is on (default). Treat that as the server's own claim: add only servers you trust, or switch it off to have every integration tool ask.

Transports: **stdio** and **streamableHttp**. Allowlists are explicit tool names (or `*` up to 80 tools). Default = approval required; `AutoApprovedTools` for unattended tools only.

Secrets a tool takes as an *argument* (for example an IMAP `password`): the agent saves it with `AskForMcpCredential` (provider = the `jarvis-mcp-` id, `secretName` = the argument name) and passes `{{secret:NAME}}` as the value in `InvokeMcpTool`'s `argumentsJson`. `McpToolHost.InvokeToolAsync` resolves it from that server's own credential store after approval (the card shows only the placeholder), refuses `server_config` and `oauth_*`, and redacts the value from the result. A missing value returns an instruction to ask for it. Direct (non-`InvokeMcpTool`) tool calls do not resolve placeholders.

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

WhatsApp chats also appear in the app's **Chats** tab next to Jarvis conversations, and Everything has a **WhatsApp** tile for everyday use. It opens the personal account by default and offers an account picker when several accounts are linked. The account number and actual bridge connection state appear above the searchable chat list. Selected chats show previews of messages saved by Jarvis and unread counts; these counts are shared across Jarvis clients and do not change WhatsApp read receipts. Chats that have not been enabled expose names and activity only, without message previews.

**Catch up.** `WhatsAppCatchUpScanner` (API host, every 20 s) summarizes the unread messages of read-along chats once a chat has been quiet for 90 s and has at least two unread messages from other people. One tool-less background model call writes a one-to-three sentence summary and up to five things the owner should reply to (`{summary, toReply:[{who, about}], messageCount}`); chat text is treated as untrusted data and nothing is sent. The result is stored on `whatsapp_chats` (`catch_up`, `catch_up_through`, `catch_up_lease_until`; claimed with a lease like the reminder scan) and returned as `catchUp` on the chat list only while it still covers unread messages: reading the chat (`/read`) hides it without another write, and clearing saved messages or consolidating aliases removes or regenerates it. Chats with read along off are never summarized. Audit action `whatsapp.catch_up_created` carries ids only.

Chat views can load older saved messages, retain loaded history during polling, and catch up across reconnects. Chats with reading off show that state and a Start reading action rather than claiming to receive messages. For enabled chats, More → Load WhatsApp history calls `POST /channels/{id}/chats/open/history?chatId=&beforeId=`. Jarvis requests up to 100 earlier messages from the linked phone using a saved message cursor or the most recent key supplied by the phone. The phone must be connected and have supplied at least one message key for that chat; availability and depth depend on WhatsApp. Requested history arrives asynchronously through `messaging-history.set` and is saved only for enabled chats. It does not trigger message automations or automatic reminders. When WhatsApp confirms that an internal LID belongs to a phone number, the bridge lists them as one chat. Loading the owned chat list consolidates saved settings and messages under the phone ID in a transaction. Message IDs, media, timestamps and pagination cursors remain intact; the latest reading/reminder choice wins. Empty aliases do not reset unread state. Groups and different linked accounts are never combined, and names alone are not used to match contacts. A running reminder scan postpones consolidation until it finishes. After the deployment, refresh Chats and reopen an old LID-based chat so it uses the phone ID. Photos, stickers, voice notes and documents are downloaded (up to 8 MB; stickers up to 1.5 MB) and shown in the chat. Video and GIF messages keep a jpeg preview rather than the full file. Locations, contacts, polls and quoted replies are shown as themselves. Messages saved before this change still show a labelled card, because the original file was not stored. `GET /channels/{id}/chats/open/media?messageId=` returns those bytes for a message the owner can already read; a missing file is 404. Cached chats and messages remain visible while reconnecting, with a warning instead of a false connected indicator.

The chat-list response includes `account`, `state`, `preview`, `previewFromMe`, and `unreadCount`; `live` is true only for an open bridge session. `GET /channels/{id}/chats/status` returns the account and state without QR codes or credentials. `POST /channels/{id}/chats/open/read?chatId= {messageId}` advances the owner-scoped read watermark through a message the client loaded, keeping later arrivals unread. `GET /channels/{id}/chats/open/messages?chatId=&beforeId={messageId}` pages by timestamp and message id together so equal timestamps do not skip messages. The chat id is a query value because group ids contain `@`. The app sends a group id as `b64.` plus base64url so the `@` is not in the URL; the API still accepts the plain id. The same actions still accept `/{chatId}` in the path. `GET /channels/{id}/chats/open/picture?subject=` returns the preview picture for a chat or a group participant (`subject` is the chat id or the message `senderId`); a missing picture is 404 and the app shows initials. The existing timestamp-only `before` parameter remains supported; choose one cursor per request.

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
