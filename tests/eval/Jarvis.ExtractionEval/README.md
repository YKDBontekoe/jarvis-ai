# Memory extraction eval

Scores `ConversationMemoryExtractor` against a real model. Each case in `dataset.json` gives the memories that already
exist, the turns before a message and the message, and lists the writes extraction should make:

| op | Meaning |
|----|---------|
| `extracted` | a new memory |
| `enriched` | an existing memory replaced by a fuller statement |
| `superseded` | an existing memory replaced by a correction |
| `expired` | an existing memory ended without a replacement |

An expectation can accept several ops (`"op": ["expired", "superseded"]`), names its `target` memory, lists text the
stored memory must `contains` (every entry, ignoring case; `"a|b"` accepts either) and can check `validUntil` (`"yyyy-MM-dd"`, `"set"` or
`"none"`). `"expect": []` means nothing may be stored. `mustNotContain` catches relative dates that should have been
resolved, and `allowExtra` tolerates additional memories. Every case runs at the dataset's fixed `today` in its
`timeZone`, and search returns all of a case's memories, so the eval measures the extraction decision only; retrieval
is measured by [Jarvis.MemoryEval](../Jarvis.MemoryEval/README.md).

```sh
dotnet run --project tests/eval/Jarvis.ExtractionEval -- --dry-run       # validate the dataset, no model calls
export EXTRACTION_EVAL_API_KEY=<OpenRouter key> EXTRACTION_EVAL_MODEL=openai/gpt-5-mini
dotnet run --project tests/eval/Jarvis.ExtractionEval                     # all cases once
dotnet run --project tests/eval/Jarvis.ExtractionEval -- --runs 3         # repeat to see variance
dotnet run --project tests/eval/Jarvis.ExtractionEval -- --case expire-car
```

`EXTRACTION_EVAL_BASE_URL` points it at any other OpenAI-compatible endpoint, such as a local model server.

`--codex` runs extraction through Jarvis's own Codex app-server client instead, the default production path. It uses the
ChatGPT account signed in under `CODEX_HOME` (`codex login --device-auth`), the account's default model unless
`EXTRACTION_EVAL_MODEL` names one, and `EXTRACTION_EVAL_CODEX` as the executable (default `codex`):

```sh
CODEX_HOME=/path/to/codex-home dotnet run --project tests/eval/Jarvis.ExtractionEval -- --codex --runs 3
```

A result through another provider shows how the prompt and rules behave, not exactly what a Codex install will do. Keep
the model ID with every result.

Metrics: cases passed, write recall (expected writes that happened), extra writes, harmful writes (a memory replaced or
ended that the case did not name), stored relative dates, and latency. A case passes only with every expected write,
no extra or harmful write and no forbidden phrase.

All cases are invented. Never add real memories or messages.

## Results (2026-10-10, Codex account default model, `--codex --runs 2`, 44 cases)

| Extractor | Cases passed | Write recall | Extra | Harmful | Relative dates | p50 |
|-----------|-------------:|-------------:|------:|--------:|---------------:|----:|
| Before (message only, add/duplicate/supersede) | 63/88 (0.72) | 41/66 | 6 | 0 | 0 | 6.1 s |
| After (context, enrich, expire, dates, validUntil) | 88/88 (1.00) | 66/66 | 0 | 0 | 0 | 5.9 s |

Many of the old extractor's misses need what it did not have: short replies that only make sense with the question
before them, temporary situations without an end date, and memories it could not enrich or end. It also missed a plain
correction ("I quit coffee, it's tea only now" became a second memory instead of replacing the first) and a project
detail. The new actions caused no harmful writes. The set is written by the same author as the prompt, so a perfect
score means these behaviours work, not that extraction is solved: add cases whenever a real conversation goes wrong.
