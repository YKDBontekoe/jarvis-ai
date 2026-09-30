# Memory retrieval eval

Offline eval for memory search (`IMemoryService.SearchAsync`). It seeds invented memories for one owner (plus a second
owner and an expired memory to check isolation) into a scratch PostgreSQL database and scores the ranked hits against
graded labels in `dataset.json` (2 = answers the query, 1 = helps).

```sh
# Scratch database with pgvector and pg_trgm available; the eval migrates it and deletes all memories in it.
export MEMORY_EVAL_DB="Host=localhost;Database=jarvis_eval;Username=jarvis;Password=<local password>"
dotnet run --project tests/eval/Jarvis.MemoryEval -- after               # 32 tuning queries
dotnet run --project tests/eval/Jarvis.MemoryEval -- after --held-out    # 20 queries never used for tuning
dotnet run --project tests/eval/Jarvis.MemoryEval -- after --updates     # corrections: is the memory to update found?
dotnet run --project tests/eval/Jarvis.MemoryEval -- after --scale 2000  # plus 2,000 filler memories
```

Never point it at a database with real data.

Metrics: recall@3/@8 over all labelled memories, primary hit@1 and MRR over grade-2 memories, nDCG@8, the share of
returned hits that are irrelevant (noise that ends up in the model context), empty result count, leaks of expired or
other-owner memories, and search latency (p50/p95 over six timed runs per query after a warm-up run).

The embedding path is not covered: without an embedding provider the eval measures the keyword path, which is also
the default setup (Codex has no embeddings). Model reranking is not covered either.

## Results (2026-09-30, local PostgreSQL 16)

| Set | Version | Recall@8 | Hit@1 | MRR | nDCG@8 | Empty | p50 |
|-----|---------|---------:|------:|----:|-------:|------:|----:|
| Tuning (32) | before | 0.36 | 0.38 | 0.42 | 0.40 | 13 | 5.3 ms |
| Tuning (32) | after | 0.78 | 0.66 | 0.76 | 0.77 | 2 | 5.3 ms |
| Held-out (20) | before | 0.35 | 0.30 | 0.35 | 0.35 | 9 | 5.3 ms |
| Held-out (20) | after | 0.72 | 0.60 | 0.68 | 0.69 | 3 | 5.0 ms |
| Updates (15) | before | 0.53 | 0.40 | 0.47 | 0.48 | 4 | 5.3 ms |
| Updates (15) | after | 0.93 | 0.60 | 0.74 | 0.79 | 0 | 6.3 ms |
| +2,000 fillers | before | 0.32 | 0.19 | 0.28 | 0.28 | 5 | 34 ms |
| +2,000 fillers | after | 0.39 | 0.19 | 0.28 | 0.30 | 4 | 12 ms |

No expired or other-owner memory was returned in any run. The filler memories are shuffled words from the dataset
itself, so they are a deliberately hostile stress test for speed rather than a realistic accuracy test.

Chat turns no longer wait on a model rerank; with the new retrieval, 13 of the 32 tuning queries would otherwise have
triggered one (up to 8 s each). The `SearchMemory` tool still reranks when the top hit does not clearly win (9 of 32).
