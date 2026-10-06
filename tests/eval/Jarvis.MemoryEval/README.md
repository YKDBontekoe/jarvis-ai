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

Without `--embeddings` the eval measures the keyword path, which is also the default setup (Codex has no embeddings).
Model reranking is not covered.

## Semantic path (local open models)

`embed_dataset.py` precomputes vectors for every memory and query with a local `sentence-transformers` model; pass the
file with `--embeddings`. Memories are indexed through `IMemoryIndexRepository` (zero-padded to the 1536-dimension
column) and queries are served from the same file, so the real hybrid ranking runs without a paid provider.
`--min-sim x` overrides the similarity floor.

```sh
pip install sentence-transformers
python3 tests/eval/Jarvis.MemoryEval/embed_dataset.py sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2 /tmp/emb.json
dotnet run --project tests/eval/Jarvis.MemoryEval -- hybrid --held-out --embeddings /tmp/emb.json
```

| Set | Retrieval | Recall@8 | Hit@1 | MRR | Noise |
|-----|-----------|---------:|------:|----:|------:|
| Tuning (32) | keyword | 0.78 | 0.66 | 0.76 | 0.46 |
| Tuning (32) | + MiniLM | 0.82 | 0.78 | 0.85 | 0.46 |
| Tuning (32) | + e5-small | 0.82 | 0.66 | 0.72 | 0.83 |
| Held-out (20) | keyword | 0.72 | 0.60 | 0.68 | 0.41 |
| Held-out (20) | + MiniLM | 0.81 | 0.85 | 0.88 | 0.36 |
| Held-out (20) | + e5-small | 0.91 | 0.60 | 0.73 | 0.83 |
| Updates (15) | keyword | 0.93 | 0.60 | 0.74 | 0.59 |
| Updates (15) | + MiniLM | 0.87 | 0.73 | 0.79 | 0.38 |

Hybrid search costs one embedding call per search (about 8 ms here because the vectors are precomputed; a provider adds
its network latency, in parallel with the keyword query). Both models are small open models, so a hosted model such as
text-embedding-3-small should do at least as well.

Models differ in how similarities are spread: multilingual-e5-small scores unrelated text around 0.8 cosine, MiniLM
around 0.1. With the original fixed mapping e5 dropped recall to 0.57, below keyword-only; `MemoryRanking` now also
uses each hit's position between the list's typical and best similarity. e5 still returns many weak hits (noise 0.83),
so for such models the similarity floor needs a higher value.

## Agentic search

`--agent-queries file.json` replaces each user message with the search queries a model wrote for it (a JSON object
message → 1-3 queries, one `SearchMemory` call each, hits merged by best score). The queries in this comparison came
from one Sonnet call that saw only the 67 user messages, not the memories or the dataset labels.

| Retrieval | Tuning recall@8 | Held-out recall@8 | Held-out hit@1 | Held-out MRR | Held-out noise |
|-----------|----------------:|------------------:|---------------:|-------------:|---------------:|
| Raw message, keyword | 0.78 | 0.72 | 0.60 | 0.68 | 0.41 |
| Raw message, + MiniLM | 0.82 | 0.81 | 0.85 | 0.88 | 0.36 |
| Agent queries, keyword | 0.87 | 0.86 | 0.70 | 0.84 | 0.62 |
| Agent queries, + MiniLM | 0.91 | 0.91 | 0.85 | 0.93 | 0.58 |

Limits: one round (the agent never reads results and searches again), a strong model writing the queries, only 52
queries, and the merged lists carry more weak hits. An agentic search costs a model round before every search, which
the automatic per-turn memory context cannot afford; the `SearchMemory` tool can.

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

## Realistic large set (1,000 memories)

`dataset-large.json` holds 1,000 invented memories of one persona (work, family, health, home/money/travel, hobbies and
assistant preferences, about 60% Dutch, 63 expired or replaced, journal entries) with 60 natural questions. Models wrote
memories and graded labels (25 of the questions are deliberately vocabulary-gap queries: the question shares few words
with the answer), so treat the numbers as indicative. Run it with `--dataset dataset-large.json`; `merge_large.py`
rebuilds it from per-area files and `embed_dataset.py` takes `DATASET=<file>`.

| Retrieval | Recall@8 | Hit@1 | MRR | Noise | p50 / p95 |
|-----------|---------:|------:|----:|------:|----------:|
| Raw message, keyword | 0.29 | 0.27 | 0.33 | 0.91 | 9.7 / 16 ms |
| Raw message, + MiniLM | 0.45 | 0.33 | 0.46 | 0.85 | 18 / 25 ms |
| Agent queries, keyword | 0.58 | 0.35 | 0.48 | 0.83 | 15 / 19 ms |
| Agent queries, + MiniLM | 0.68 | 0.52 | 0.64 | 0.79 | 23 / 29 ms |
| Raw message, keyword, +5,000 fillers | 0.19 | 0.25 | 0.29 | 0.95 | 26 / 40 ms |

Much harder than the small set: the questions are indirect, so keyword search alone finds under a third of the labelled
memories. No expired or other-owner memory was returned.

## Search hints and rerank (1,000-memory set)

`--hints file.json` stores model-written search hints per memory (`hints.json` style `{key: text}`; Sonnet wrote them
from the memory text only) and embeds content + hints like the background indexer does. `--pool 15 --dump pool.json`
dumps each query's candidates, and `--rerank file.json` scores the keys a model kept per query (Sonnet, shown only the
query and the candidate texts, not the labels).

| Retrieval | Recall@8 | Hit@1 | MRR | Noise | p50 |
|-----------|---------:|------:|----:|------:|----:|
| Raw message, keyword | 0.29 | 0.27 | 0.33 | 0.91 | 10 ms |
| Raw message, keyword + hints | 0.51 | 0.48 | 0.56 | 0.85 | 12 ms |
| Raw message, + MiniLM | 0.45 | 0.33 | 0.46 | 0.85 | 18 ms |
| Raw message, + MiniLM + hints | 0.59 | 0.58 | 0.66 | 0.81 | 17 ms |
| Agent queries, + MiniLM | 0.68 | 0.52 | 0.64 | 0.79 | 23 ms |
| Agent queries, + MiniLM + hints | 0.78 | 0.63 | 0.78 | 0.76 | 17 ms |
| + rerank, keep at most 8 (pool 15) | 0.79 | 0.87 | 0.93 | 0.51 | |
| + rerank, keep at most 5 (pool 15) | 0.69 | 0.85 | 0.91 | 0.25 | |

Rerank rows are on top of the agent-queries + MiniLM + hints row and exclude the model call itself (a few seconds). Keeping
fewer results trades recall for noise: at most 8 holds recall and halves the noise, at most 5 cuts noise to a quarter
but loses the helpful-but-secondary memories. Skipping the rerank when the top hit clearly wins saved only 11 of 60
calls and cost noise, so the tool always reranks. Hints and queries are written by the same family of model that
wrote the memories and questions, which probably flatters these numbers; real usage will show less.

### Follow-up messages and a larger embedding model

24 short follow-ups ("And which school is she at?") whose subject sits in the previous user message (written by Sonnet from
the original question and its answer memory, same labels), searched with hints on:

| Query sent to search | Retrieval | Recall@8 | Hit@1 | MRR |
|----------------------|-----------|---------:|------:|----:|
| Follow-up alone | keyword | 0.30 | 0.17 | 0.29 |
| Follow-up alone | + MiniLM | 0.40 | 0.25 | 0.38 |
| Previous message + follow-up | keyword | 0.63 | 0.50 | 0.62 |
| Previous message + follow-up | + MiniLM | 0.64 | 0.83 | 0.89 |

Prepending the previous message to every short message turned out to be risky: when the previous message is unrelated
(10 short standalone questions, each preceded by a random other question) recall fell from 0.77 to 0.57 and hit@1 from 0.70
to 0. `PersonalMemoryContextProvider` therefore searches both ways and merges (`--prior file.json --prior-weight w`):

| Context weight | Follow-ups recall / hit@1 | Topic switch recall / hit@1 |
|----------------|--------------------------:|----------------------------:|
| no context | 0.40 / 0.25 | 0.77 / 0.70 |
| prepend only | 0.64 / 0.83 | 0.57 / 0.00 |
| merge 0.6 | 0.50 / 0.38 | 0.77 / 0.70 |
| merge 1.0 | 0.56 / 0.54 | 0.73 / 0.70 |
| merge 1.2 (used) | 0.59 / 0.63 | 0.70 / 0.70 |
| merge 1.5 | 0.59 / 0.75 | 0.70 / 0.30 |
| merge 2.0 | 0.62 / 0.79 | 0.65 / 0.00 |

Both sets are small (24 and 10 queries) and model-written.

A bigger embedding model helps only a little here: multilingual-e5-base gives recall 0.63 / hit@1 0.65 on raw questions
(MiniLM 0.59 / 0.58) and 0.77 / 0.70 with agent queries (MiniLM 0.78 / 0.63), at several times the compute.

### Tried without gain

- **Stricter score cut** (`RelativeCutoff` 0.35 to 0.65, `MinimumRelevance` 0.08 to 0.2): noise falls only as fast as recall
  (raw hybrid: 0.35 gives recall 0.59 and noise 0.81, 0.65 gives 0.46 and 0.69). Scores do not separate helpful from
  topically similar memories well, so the model rerank in `SearchMemory` stays the noise fix.
- **Local cross-encoder** (`cross-encoder/mmarco-mMiniLMv2-L12-H384-v1`, about 8 ms per query-memory pair on 4 CPU threads)
  as a per-turn rerank over 11 candidates: hit@1 0.55 against 0.58 for the hybrid order, so no better and slower.
- **Fusion weights** (semantic x0.7 to x1.4, keyword x0.7 to x1.0): all within about 0.02 recall / 0.05 hit@1, which is
  within the noise of 60 queries, so the weights stay as they are.

## Embedding model comparison: MiniLM vs EmbeddingGemma 2 (CPU, 2026-10-06)

Compares the production model (`paraphrase-multilingual-MiniLM-L12-v2`, 384 dimensions) with Google's
[EmbeddingGemma 2](https://huggingface.co/google/embeddinggemma-2) (Apache 2.0, 768 dimensions, Matryoshka 768/512/256/128,
8K context). EmbeddingGemma 2 is a 740M multimodal model; Jarvis only needs text, so it is loaded text-only (vision and audio
encoders off, 271M parameters). Machine: 4 vCPU Intel Xeon 2.8 GHz (AVX-512 VNNI, no bf16/AMX), 16 GB RAM, no GPU, PyTorch 2.14
CPU, sentence-transformers 6.1, fp32. Raw numbers: `results/embedding-bench-cpu-2026-10-06.json`.

```sh
pip install sentence-transformers pillow psutil
EMBED_DIM=512 DATASET=dataset-large.json python3 embed_dataset.py google/embeddinggemma-2 /tmp/eg2.json   # EMBED_PROMPTS=none, EMBED_QUANT=int8
dotnet run --project tests/eval/Jarvis.MemoryEval -- hybrid --dataset dataset-large.json --embeddings /tmp/eg2.json
python3 dense_metrics.py dataset-large.json /tmp/eg2.json     # embedding only, no keyword path
python3 bench_embedding.py --repeat 2 /tmp/perf.json           # CPU performance, one subprocess per configuration
```

### Retrieval quality (real hybrid ranking, raw user message, no hints)

| Set | Retrieval | Recall@8 | Hit@1 | MRR | nDCG@8 | Noise |
|-----|-----------|---------:|------:|----:|-------:|------:|
| Large (1,000 / 60 q) | keyword | 0.29 | 0.27 | 0.33 | 0.30 | 0.91 |
| Large | + MiniLM | 0.45 | 0.33 | 0.46 | 0.43 | 0.85 |
| Large | **+ EmbeddingGemma 2 (768)** | **0.63** | **0.45** | **0.59** | **0.59** | 0.81 |
| Tuning (32) | + MiniLM | 0.82 | 0.78 | 0.85 | 0.84 | 0.46 |
| Tuning (32) | **+ EmbeddingGemma 2** | **0.95** | **0.88** | **0.91** | **0.92** | 0.74 |
| Held-out (20) | + MiniLM | 0.81 | 0.85 | 0.88 | 0.83 | 0.36 |
| Held-out (20) | **+ EmbeddingGemma 2** | **0.93** | 0.85 | **0.92** | **0.93** | 0.78 |
| Updates (15) | + MiniLM | 0.87 | 0.73 | 0.79 | 0.81 | 0.38 |
| Updates (15) | **+ EmbeddingGemma 2** | **1.00** | **0.87** | **0.93** | **0.95** | 0.84 |

No expired or other-owner memory leaked in any run. Embedding only (dense, no keyword fusion, large set): MiniLM recall@8 0.50 /
hit@1 0.32 / MRR 0.44; EmbeddingGemma 2 0.63 / 0.45 / 0.58. A paired bootstrap over the 60 large-set queries gives a gain of
+0.13 recall@8 (95% CI +0.06 to +0.21), +0.15 MRR (+0.05 to +0.24) and +0.13 hit@1 (+0.02 to +0.27); EmbeddingGemma 2 is better on
23, worse on 7 and equal on 30 queries for recall@8.

Noise is higher on the small sets because the current similarity floor (0.3 cosine) was tuned on MiniLM: MiniLM returns about 2
hits per query there, EmbeddingGemma 2 about 6. Raising the floor to 0.4 to 0.6 did not change the large-set result and cut
held-out noise only from 0.78 to 0.76 at the cost of recall (0.93 to 0.91 at 0.6), so the model rerank in `SearchMemory` is still the noise fix.

### Variants (large set, hybrid)

| Variant | Recall@8 | Hit@1 | MRR | Note |
|---------|---------:|------:|----:|------|
| EmbeddingGemma 2, 768d, task prompts | 0.63 | 0.45 | 0.59 | |
| 512d | 0.63 | 0.45 | 0.60 | no loss |
| 256d | 0.61 | 0.42 | 0.57 | small loss |
| 128d | 0.56 | 0.38 | 0.50 | clear loss |
| 768d, **no prompts** | 0.54 | 0.40 | 0.52 | loses most of the gain |
| 768d, dynamic int8 (PyTorch) | 0.54 | 0.38 | 0.51 | loses most of the gain |
| MiniLM, dynamic int8 | 0.44 | 0.35 | 0.46 | about equal to fp32 |

The task prompts matter: EmbeddingGemma 2 expects `task: search result | query: ...` on queries and `title: none | text: ...` on
memories. `IMemoryEmbedder.EmbedAsync(texts)` does not distinguish the two roles, so adopting the model needs a query/document
parameter (or the prefixes added in the embedder). Dynamic int8 hurts EmbeddingGemma 2 far more than MiniLM.

### CPU performance (4 threads unless stated; best of two rounds)

| | MiniLM fp32 | MiniLM int8 | EmbeddingGemma 2 fp32 (text-only) | EG2 int8 | EG2 full multimodal |
|---|---:|---:|---:|---:|---:|
| Parameters | 118M | 96M | 271M | 134M | 744M |
| Output dimensions | 384 | 384 | 768 (MRL to 128) | 768 | 768 |
| Mean tokens per memory / question | 22 / 17 | | 28 / 25 | | |
| Query embed, p50 / p95 | 23 / 33 ms | 14 / 22 ms | 128 / 165 ms | 73 / 100 ms | 142 / 180 ms |
| Query embed, 1 thread, p50 | 33 ms | | 186 ms | | |
| Indexing, 1,000 memories (batch 32) | 4.7 s | 3.4 s | 39.5 s | 28.9 s | 40.1 s |
| Indexing throughput, 1 thread | 78 docs/s | | 8 docs/s | | |
| Process RSS after load (incl. about 420 MB torch) | 1.0 GB | | 1.8 GB | | 3.6 GB |
| Peak RSS | 1.2 GB | | 2.1 GB | | 4.7 GB |
| Weights to download | 471 MB | | 1.49 GB (full checkpoint; text-only ONNX fp32 1.08 GB) | | 1.49 GB |

EmbeddingGemma 2 is about 5.5x slower per query and 8x slower to index than MiniLM in PyTorch. Every search embeds the query
once (twice when the previous message is merged in), so a search adds about 130 ms instead of 25 ms; re-embedding after a
model switch takes 40 s per 1,000 memories (2 min on one thread). bf16 is 1.5x slower than fp32 here (126 vs 193 ms p50;
fp16 is unusable, it produces NaNs). int8 RSS figures are not meaningful: `quantize_dynamic` keeps the fp32 copy resident.

Storage: the column is `vector(1536)` and vectors are zero-padded, so 768d (or 512d) costs the same 6 KB per memory as
MiniLM today; the Matryoshka saving only materialises if the column and HNSW index are resized.

### Reading the numbers

- Quality: EmbeddingGemma 2 is better or equal on every metric and set (held-out hit@1 ties), mainly on vocabulary-gap questions (large set: +0.18 recall@8).
  The datasets are model-written, the large set has 60 queries, and fusion weights and the similarity floor were tuned with
  MiniLM, which if anything favours MiniLM. Search hints and agent-written queries (not in the repo) lift MiniLM a lot
  (README above: 0.45 to 0.78 recall@8) and were not re-run, so the gap in the production path may be smaller.
- Cost: about 5x latency and 2x RAM per query path on CPU. Fine for the `SearchMemory` tool and background indexing, noticeable for the
  automatic per-turn memory context.
- Not measured: the Hugging Face Text Embeddings Inference image Jarvis deploys (no Docker daemon here; support for the
  `embedding_gemma2` architecture is unverified), ONNX/llama.cpp builds (GGUF and q4 ONNX checkpoints exist and are likely
  faster than eager PyTorch), non-Dutch/English languages, and concurrent request load.
