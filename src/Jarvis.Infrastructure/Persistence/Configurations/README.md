# EF Core entity configurations

Entity mappings for `JarvisDbContext` live in `IEntityTypeConfiguration<T>` classes under this folder, grouped by bounded context:

| Folder | Domain area | Examples |
|--------|-------------|----------|
| `Identity/` | Users, auth tokens | `JarvisUser`, `AuthRefreshToken` |
| `Conversations/` | Chat and agent session state | `Conversation`, `Message`, `AgentSessionState`, `MessageFeedbackEntity` |
| `Memory/` | Long-term memory and knowledge graph | `MemoryEntity`, `GraphEntityEntity`, `GraphRelationEntity` |
| `Workflows/` | Approvals, audit, scheduling, tasks, notifications | `ToolApproval`, `AuditEvent`, `Reminder`, `JarvisTask`, `CodingRun` |
| `Integrations/` | External credentials and OAuth handshakes | `IntegrationCredential`, `McpOAuthSession` |
| `Files/` | Stored blobs and content chunks | `StoredFileEntity`, `FileContentChunkEntity` |
| `Channels/` | Messaging channel adapters | `ChannelConnectionEntity`, `ChannelMessageEntity`, `ChannelThreadEntity` |
| `Platform/` | Owner settings, skills, UI surfaces, usage telemetry | `OwnerSettingEntity`, `SkillEntity`, `ModelUsageEventEntity` |
| `Infrastructure/` | Cross-cutting model setup (not per-entity) | PostgreSQL extensions (`vector`, `pg_trgm`), ASP.NET Identity table names |

When adding a new persisted entity:

1. Add a `DbSet<>` on `JarvisDbContext` if needed.
2. Create `{EntityName}Configuration.cs` in the matching folder above.
3. Implement `IEntityTypeConfiguration<T>` with table name, keys, indexes, and relationships.
4. Do **not** edit `OnModelCreating` for entity mappings; configurations are discovered via `ApplyConfigurationsFromAssembly`.

Append-only `AuditEvent` enforcement remains on `JarvisDbContext.SaveChanges` (`RejectAuditMutation`).
