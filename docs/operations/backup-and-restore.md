# Backup and restore

Production Compose runs a `backup` service that takes a nightly backup of everything needed to bring Jarvis back on a new host, except uploaded file blobs (see [What is not included](#what-is-not-included)).

## What a backup contains

Each run writes `$JARVIS_BACKUP_DIR/jarvis-YYYYMMDDTHHMMSSZ/`:

| File | Contents |
|------|----------|
| `jarvis.dump` | `pg_dump` (custom format) of the application database: conversations, memories, journal, people, expenses, settings, encrypted integration credentials |
| `temporal.dump` | `pg_dump` of the Temporal database: schedules and in-flight workflows |
| `data-protection-keys.tar.gz` | The ASP.NET Core key ring. **Without it, stored integration credentials and MCP tokens cannot be decrypted after a restore.** |
| `SHA256SUMS` | Checksums of the files as stored |

When `BACKUP_PASSPHRASE` is set, each file is encrypted (AES-256-CBC, PBKDF2 with 200,000 iterations) and gets a `.enc` suffix. Keep the passphrase somewhere other than this host; a backup cannot be restored without it.

The service runs as `JARVIS_UID:JARVIS_GID` with a read-only root filesystem. It joins only the two private database networks and mounts the key ring read-only. A failed run leaves no partial backup and is retried the next day; check `docker compose logs backup`.

## Setup

1. Create the backup directory, owned by the Jarvis account:

   ```sh
   sudo install -d -m 0700 -o "$JARVIS_UID" -g "$JARVIS_GID" /srv/jarvis/backups
   ```

2. Set these in `infra/compose/.env.production`:

   | Variable | Default | Purpose |
   |----------|---------|---------|
   | `JARVIS_BACKUP_DIR` | required | Host directory for backups |
   | `BACKUP_PASSPHRASE` | empty | Encrypt backups when set |
   | `BACKUP_HOUR_UTC` | `3` | Hour of the nightly run |
   | `BACKUP_RETENTION_DAYS` | `14` | Backups older than this are deleted |

3. Deploy as usual (`scripts/deploy/remote-up.sh`). To take a backup right away:

   ```sh
   docker compose --env-file infra/compose/.env.production \
     -f infra/compose/docker-compose.production.yml run --rm backup --once
   ```

4. Copy backups off the host on a schedule, for example with `rclone sync` or `restic` to a different provider. A backup on the same disk does not survive losing that disk.

## Restore

Run from the repository root on the production host:

```sh
BACKUP_PASSPHRASE=... scripts/backup/restore.sh /srv/jarvis/backups/jarvis-20261002T030000Z
```

The script:

1. Checks `SHA256SUMS` and decrypts into a private temporary directory. A wrong passphrase or a changed file stops it before anything is touched.
2. Asks you to type `restore` (set `JARVIS_RESTORE_CONFIRM=yes` to skip).
3. Stops the API, worker, Temporal and the backup schedule.
4. Restores both databases with `pg_restore --clean --if-exists --single-transaction`, so a failed restore rolls back.
5. Copies the current key ring to `<keys dir>.before-restore-<timestamp>`, then replaces it with the backup's.
6. Starts the stack.

On a new host, deploy once first so Postgres and the volumes exist, then restore. `DEPLOY_COMPOSE_FILES` and `ENV_FILE` work the same way as in `remote-up.sh`.

Practice a restore on a spare host now and then. A backup you have never restored is a guess.

## What is not included

- **Uploaded files** live in Garage (`garage-data` and `garage-meta` volumes). Back them up at the volume level, or sync the `jarvis-files` bucket with an S3 tool. File metadata and search chunks are in `jarvis.dump`, so a restore without blobs keeps search results but downloads fail.
- **Codex OAuth state** in `CODEX_HOME_DIR`. Sign in again with `codex login` after moving hosts.
- **Signal and WhatsApp link state** (`signal-cli-data`, `whatsapp-bridge-data`). Re-link from the app.
