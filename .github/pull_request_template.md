## Summary

<!-- What changes, why, and what the person using Jarvis will notice. Link to issues, design notes or mockups (docs/design/mockups/). -->

## SemVer impact

Jarvis uses [Semantic Versioning 2.0.0](https://semver.org/) for **release tags** (`vMAJOR.MINOR.PATCH`) that start the Release workflow.

| Bump | When to choose it | Example tag |
|------|-------------------|-------------|
| **Major** | Breaking API, auth, database, or mobile contract changes; removals or incompatible config | `v2.0.0` |
| **Minor** | Backward-compatible features or capabilities | `v1.3.0` |
| **Patch** | Backward-compatible fixes, docs, CI, or dependency updates with no new capability | `v1.2.4` |
| **None** | No user-facing release (refactor, tests, internal tooling only) | — |

- [ ] **SemVer bump: major**
- [ ] **SemVer bump: minor**
- [ ] **SemVer bump: patch**
- [ ] **SemVer bump: none** (merge without creating a release tag)
- [ ] If mobile changes ship: `apps/mobile/pubspec.yaml` `version:` matches the intended **marketing** SemVer (`X.Y.Z+N`).

Merging to `main` runs [`.github/workflows/create-release-tag.yml`](.github/workflows/create-release-tag.yml), which reads the checked **SemVer bump** above and pushes `vX.Y.Z`. That tag starts [`.github/workflows/release.yml`](.github/workflows/release.yml), which checks the backend/web and iOS builds and then requests one production approval. Use **none** for refactors and other changes that should not release.

**Intended release tag (optional):** `v1.2.0` — use when the computed bump would not land on the version you need.

## Type of change

- [ ] Feature (minor)
- [ ] Bug fix (patch)
- [ ] Breaking change (major)
- [ ] Documentation only
- [ ] CI / release pipeline
- [ ] Dependency or infrastructure
- [ ] Refactor (no SemVer release)

## Scope

### Backend / API

- [ ] Not applicable
- [ ] API or SignalR contract change
- [ ] Database migration
- [ ] Temporal workflow or worker behavior
- [ ] Docker / AppHost / production deploy
- [ ] MCP, integrations, or auth
- [ ] Agent tools or context (prompt shape changed; `tests/e2e/fake_codex_app_server.mjs` updated)
- [ ] Owner scope or approvals touched

### Mobile (Flutter)

- [ ] Not applicable
- [ ] UI / UX
- [ ] Sign-in, push, or LiveKit
- [ ] iOS build / unsigned IPA / LiveContainer
- [ ] Home screen widget or other native iOS code

### Docs and agent config

- [ ] Not applicable
- [ ] `docs/`, `AGENTS.md` or `.claude/` (skills, hooks) changed

## Related issues

<!-- Fixes #123, relates to #456 -->

## Evidence

Required by [`AGENTS.md`](AGENTS.md) ("Verify every change"). Tick what ran, paste the results, and say why a step did not run. Never fill in results that were not produced.

- [ ] Unit tests: `dotnet test tests/unit/Jarvis.UnitTests/Jarvis.UnitTests.csproj`
- [ ] Integration tests (persistence changes): `dotnet test tests/integration/Jarvis.IntegrationTests/Jarvis.IntegrationTests.csproj` (Docker)
- [ ] Flutter: `cd apps/mobile && flutter analyze && flutter test`
- [ ] Smoke test (backend changes): `scripts/dev/smoke.sh`, plus the new endpoint or flow exercised against `http://localhost:5082/api/v1`
- [ ] Screenshots (UI changes): `app_screenshots.dart`, `ui_polish.dart`, `layout_audit.dart`, with a new case per new screen or state; images attached below
- [ ] Recording (flows and interactions): `flutter build web -t tool/ui_preview.dart --profile` and `node tool/record_web.mjs --steps "…"`; video attached below
- [ ] Python / bridge tests when touched: `python3 -m unittest discover -s tests/unit -p 'test_*.py'`

```text
# Paste results, e.g. "Passed! Failed: 0, Passed: 1611", "11/11 checks passed", "+715: All tests passed!"
```

**Not run, and why:**

### Screenshots / recordings

<!-- Required for UI changes. Light and dark; add 320pt / large text when layout changed. -->

### UI and design (Mobile UI changes)

- [ ] Follows [`docs/design/playbook.md`](docs/design/playbook.md): tokens from `JarvisColors`, motion from `JarvisMotion`, existing components reused
- [ ] Checked at 320pt and 2× text, light and dark, and with reduced motion
- [ ] New screen or redesign: mockup linked above
- [ ] Playbook updated if a token, component or pattern was added or changed

### Checklist

- [ ] CI (**All checks passed** on this PR) is green
- [ ] Automated tests added or updated where behavior changed
- [ ] Owner scope (`ICurrentUser.OwnerId`) on every new query and endpoint
- [ ] Sensitive agent actions go through approvals
- [ ] `docs/` updated for API or behaviour changes; `README.md` or ops docs updated if setup, secrets, or release steps changed
- [ ] No secrets, tokens, or production `.env` files committed
- [ ] Production AppHost deployment / GHCR image names unchanged unless intentional
- [ ] iOS release workflow / `Jarvis.ipa` packaging considered if mobile or version fields changed

## Deployment / release notes

<!-- What operators must do after merge: env vars, migrations, `docker compose pull`, new GitHub secrets, AltStore source URL, etc. -->

## Reviewer notes

<!-- Risks, follow-ups, or areas you want extra eyes on -->
