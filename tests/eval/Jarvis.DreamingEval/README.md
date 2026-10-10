# Dreaming eval

Runs the REM model step of `DreamingService` over invented memories and recent user messages, then applies the deep
phase's own filters (known unpinned, non-journal target of the same kind, confidence at least 0.8) to see which
memories a night of dreaming would rewrite. Merge and supersede rewrite memories without review, so the main number is
harmful rewrites: one the scenario neither requires nor allows. Scenarios cover a visit that is not a move, a real move,
a pinned memory, related but distinct memories, an implied job change, unrelated chat, a changed style preference,
health talk that must not be stored, someone else's dog and a corrected relationship.

```sh
CODEX_HOME=/path/to/codex-home EXTRACTION_EVAL_CODEX=/path/to/codex dotnet run --project tests/eval/Jarvis.DreamingEval -- --runs 2
```

## Results (2026-10-10, Codex account default model, 10 scenarios × 2 runs)

| Scenarios | Required rewrites | Harmful rewrites | Sensitive proposals |
|----------:|------------------:|-----------------:|--------------------:|
| 20/20 | 4/4 | 0 | 0 |

No change was needed; keep this green when the dreaming prompt or model changes.
