## Summary

<!-- What does this pull request change, and why? Link to design notes or issues when helpful. -->

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

### Mobile (Flutter)

- [ ] Not applicable
- [ ] UI / UX
- [ ] Sign-in, push, or LiveKit
- [ ] iOS build / unsigned IPA / LiveContainer

## Related issues

<!-- Fixes #123, relates to #456 -->

## How to test

<!-- Commands, environments, and manual steps reviewers or release managers should run. -->

```sh
# Example
dotnet test tests/unit/Jarvis.UnitTests/Jarvis.UnitTests.csproj
python3 -m unittest discover -s tests/unit -p 'test_*.py'
```

### Checklist

- [ ] CI (**All checks passed** on this PR) is green
- [ ] Automated tests added or updated where behavior changed
- [ ] `README.md` or ops docs updated if setup, secrets, or release steps changed
- [ ] No secrets, tokens, or production `.env` files committed
- [ ] Production AppHost deployment / GHCR image names unchanged unless intentional
- [ ] iOS release workflow / `Jarvis.ipa` packaging considered if mobile or version fields changed

## Deployment / release notes

<!-- What operators must do after merge: env vars, migrations, `docker compose pull`, new GitHub secrets, AltStore source URL, etc. -->

## Screenshots / recordings

<!-- Optional for UI changes -->

## Reviewer notes

<!-- Risks, follow-ups, or areas you want extra eyes on -->
