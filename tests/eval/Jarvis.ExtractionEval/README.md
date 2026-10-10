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
stored memory must `contains` (all of it, ignoring case) and can check `validUntil` (`"yyyy-MM-dd"`, `"set"` or
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

`EXTRACTION_EVAL_BASE_URL` points it at any other OpenAI-compatible endpoint, such as a local model server. Production
extraction uses the owner's background model, which is Codex by default, so a result through another provider shows how
the prompt and rules behave, not exactly what a Codex install will do. Keep the model ID with every result.

Metrics: cases passed, write recall (expected writes that happened), extra writes, harmful writes (a memory replaced or
ended that the case did not name), stored relative dates, and latency. A case passes only with every expected write,
no extra or harmful write and no forbidden phrase.

All cases are invented. Never add real memories or messages.
