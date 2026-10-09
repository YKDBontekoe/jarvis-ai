# Security and ownership

Jarvis is designed to **fail closed** when authentication, scanning, or approval requirements are not met.

## Identity and owner scope

| Environment | Behavior |
|-------------|----------|
| **Development** | Unauthenticated requests may use a fixed local owner id for scripts; Flutter can still sign in. |
| **Non-Development** | All `/api/v1/*` routes require a Jarvis JWT; SignalR hub requires authorization. |

- Registration/login: `/api/v1/auth/register`, `/login`, `/refresh`, `/logout` (`IdentityAuthEndpoints`).
- Tokens: short-lived access JWT + rotating refresh; mobile uses `flutter_secure_storage`.
- **OwnerId** = Identity user id; all repositories filter by `ICurrentUser.OwnerId`.

Production requires `Authentication:Issuer`, `Authentication:Audience`, and `Authentication:SigningKey` (≥32 bytes). Set `Authentication:AllowRegistration=false` after bootstrap.

## Secrets and credentials

- **Integration credentials** (MCP tokens, OpenRouter key, channel secrets): stored encrypted via ASP.NET **Data Protection**; API lists provider/field names only.
- **MCP per run**: credentials injected into stdio env or HTTP headers for that connection only; values **redacted** from tool results/errors before model context.
- **Codex OAuth** (`auth.json` under `CODEX_HOME`): treated as host credential; mounted read/write in the production containers for token refresh.
- **Audit log** (`GET /api/v1/audit`): action metadata only—no prompts, file bodies, or memory text.

Configure `DataProtection:KeysDirectory` to a persistent `0700` directory in production.

## Network and SSRF controls

- **Condition watches** (public JSON): HTTPS only, no redirects to private IPs, JSON size cap, no local hostnames.
- **Browser MCP** (optional `browser` feature): isolated container, Squid egress deny private ranges, navigation tools approval-gated.
- **Computer sandbox** (optional `computer` feature): one hardened desktop container (read-only root, no capabilities, internal `agents` network only, egress through the same Squid proxy). Starting a session is approval-gated; shell commands and form fills ask every time. One active session per deployment, reset on start and stop, so one owner's files and logins never reach the next session. The live view is proxied by the API with one-time tickets and an owner-bound cookie; no sandbox port is published. Screenshots are stored under the owner's object-storage prefix and served only to that owner. The sandbox holds no Jarvis credentials; on the `agents` network it can reach the API port, which offers nothing beyond what Caddy already serves publicly.
- **File uploads**: MIME/extension/signature allowlist, ClamAV scan **before** S3 put; fail closed if scanner unavailable.

## Approvals and risk

Default: MCP tools, memory forget, browser navigation, coding runs, remote agent delegation, and many MCP admin operations require explicit user approval. The owner can permanently allow a category of those actions from an approval card; Jarvis then auto-approves later calls in that category and audits `approval.auto_approved`. Host MCP servers can still auto-approve individual tools via `Mcp__Servers__*__AutoApprovedTools`. Beyond grants, an **approval policy** approves read-only tools, and reversible changes inside background tasks, within the owner's autonomy settings; outbound, destructive, private-read and unclassified actions always ask. Every automatic approval is audited without arguments and bounded by a daily limit, and `autonomy.enabled = false` switches it off. Details in [chat-agent-runtime.md](chat-agent-runtime.md#tool-approval-flow). Owner-defined assistant profiles cannot unwrap `ApprovalRequiredAIFunction`, grant standing approvals, or change MCP operator allowlists; see [assistant-profiles.md](../backend/assistant-profiles.md).

## Multi-tenant gaps

Development browser profile and some channel defaults assume a **single local operator**. Before multi-user production browser or shared hosts, enforce per-owner browser sessions and review channel webhook isolation.

## CORS and web

Set `Cors:AllowedOrigins` for Flutter web deployments (HTTPS or localhost). API must be reachable from clients; SignalR uses `/hubs/events`.

## Related

- [backend/integrations-and-mcp.md](../backend/integrations-and-mcp.md)
- [operations/deployment-and-ci.md](../operations/deployment-and-ci.md) — production topology (generated from the AppHost)
- Root [README.md](../../README.md) — operator security notes (audit, browser, Codex sandbox)
