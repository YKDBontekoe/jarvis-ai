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

**Autonomy mode** — owner setting `GET/PUT /api/v1/settings/autonomy` (`{ "mode": "ask" | "trusted" }`, default `ask`, changes are audited). In `trusted` mode, allowlisted MCP tools, `InvokeMcpTool`, `ReadMcpResource`, `GetMcpPrompt`, `DiscoverMcpServerTools`, and `RunAutomation` run without a prompt. Code execution (`AddMcpStdioServer`, coding tasks), deletes, MCP registration changes, browser, device, and remote-agent tools always stay approval-gated.

Agent management tools: list/discover/add/update/invoke plus chat setup (`OfferMcpSetup`, `AskForMcpCredential`, `InstallIntegrationPack`) — see [agent-tools.md](agent-tools.md).

Compose overlays:

- `docker-compose.github.yml` — official GitHub MCP server in API image
- `docker-compose.home-assistant.yml` — Streamable HTTP to HA `/api/mcp`

## Channels

- **WhatsApp Cloud API** — webhooks at `/api/v1/channels/whatsapp/{key}/webhook`.
- **Signal** — signal-cli REST (`Channels__Signal__BaseUrl`); bundled in Aspire/Compose dev.

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
