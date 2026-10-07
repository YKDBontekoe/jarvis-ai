# File (document) retrieval eval

Offline benchmark of how Jarvis finds text in uploaded files, and of the variants that could replace it. Everything runs
through the real chunker (`FileTextChunker`) and the real repositories (`FileRepository`, `FileContentRepository`), and
the baseline is the production `FileSearchService`. Query routing has its own write-up: [routing/README.md](routing/README.md).
Predictions made before the late runs: [PREDICTIONS.md](PREDICTIONS.md).

## Setup

- **Library:** 66k passages from public retrieval benchmarks (SciFact, NFCorpus, FiQA), grouped into 2,118 topical markdown
  documents (58 MB, 21k chunks at the production size). Smaller "mid" (386 docs) and "small" (100 docs, 300 queries)
  libraries host the experiments that need embeddings, because local CPU embedding is slow.
- **Queries:** 1,271 natural questions with judged answer passages (648 FiQA, 300 SciFact, 323 NFCorpus).
- **Metric:** "answer in context": a judged passage lies in what the agent tool actually hands the model (top 8 chunks, each
  cut at 3,200 characters, 12,000 characters in total). Macro average over the three sources, bootstrap 95% intervals.
- **Server:** PostgreSQL 18, pgvector 0.8.7, pg_textsearch 1.5.1, UTF-8 (the production image's settings). Scored with
  `evaluate.py`; a perfect run scores 0.998 and a random or shuffled run 0.00.

## Results

**Current pipeline** (`websearch_to_tsquery`, every word must match): finds an answer for **14%** of questions and returns
nothing at all for **69%** of them.

| Formulation (production 3,200-character chunks) | Answer in context | nDCG@8 | Empty | p50 latency |
|---|---:|---:|---:|---:|
| Current pipeline | 0.142 | 0.076 | 69% | 4 ms |
| OR terms + `ts_rank` | 0.434 | 0.292 | 1% | 87 ms |
| OR terms + `ts_rank_cd` | 0.255 | 0.166 | 1% | 128 ms |
| OR terms + BM25 (hand-built, two-stage) | 0.507 | 0.350 | 1% | 51 ms |
| **pg_textsearch BM25** | **0.512** | 0.349 | 1% | **15 ms** |
| pg_textsearch BM25 + English stemming | 0.512 | 0.359 | 1% | 18 ms |

Latencies were taken with other jobs running; treat them as relative.

**Chunk size** (8 chunks returned, as the tool does; pg_textsearch BM25): 400 chars 0.25, 800 0.44, **1,600 0.55**, 3,200 0.51
(production), 6,400 0.32. Overlap (0 / 10% / 25%) moves the result by about 0.01. Two effects mix here: the tool returns
8 chunks, so small chunks give the model less text (400 chars x 8 = 3,200 characters), and the tool cuts every hit at 3,200
characters, so half of a 6,400-character chunk is never seen. Not tested: equal context budget per chunk size. nDCG is not
comparable across chunk sizes (a small chunk cannot cover half of a long passage).

**Embeddings and fusion** (small library, 1,600-char chunks, 300 queries; run before the final UTF-8 rebuild, the BM25 grid
above reproduced within 0.005 afterwards): BM25 0.74; MiniLM alone 0.76; bge-small alone 0.78; multilingual-e5-small alone
0.69; BM25 + MiniLM (RRF) 0.82; BM25 + bge-small 0.80; BM25 + e5 0.77. Two Claude-written rewrites per question add +0.07 to BM25
and +0.02 to the hybrid. With a weak embedder at 3,200-char chunks (it only reads the first ~1,000 characters), fusion fell
below BM25 alone.

## Conclusion

1. **The retrieval query is the problem, not the chunks.** Requiring every word to match is why 69% of questions return
   nothing. Switching to OR terms with BM25 ranking gives about **3.6x** more answered questions (0.14 to 0.51).
2. **Use real BM25.** `ts_rank` has no inverse document frequency, `ts_rank_cd` is worse and slower. `pg_textsearch` matches a
   hand-built BM25 on quality at roughly a third of the latency and needs no extra tables; it needs the extension in the
   Postgres image. Stemming adds almost nothing for English.
3. **Add embeddings and fuse.** BM25 + a small embedding model through reciprocal rank fusion (already the shape of
   `FileSearchService.AddRanks`) adds 3-9 points on top of BM25, if the model can read the whole chunk. Chunks of about
   1,600 characters (half today's size) suit models with a 256-512 token window.
4. **Query rewriting helps keyword search more than hybrid** (+0.07 vs +0.02): another reason to add embeddings first.
5. **Agentic search is already there.** The chat agent calls `SearchFiles` repeatedly; the routing study shows an agentic
   loop beats one-shot and fixed decomposition on multi-hop questions at 1-2 more searches, and that routing between
   direct and agentic can only save about 10-15% of the LLM calls.

## Not finished

Embedding models beyond MiniLM / bge-small / e5 (EmbeddingGemma, Qwen3-Embedding, BGE-M3), BGE-M3 sparse and multi-vector,
ColBERT-small, cross-encoder and listwise rerankers, pgvector iterative scans with scoped search, the scale test with 171k
distractor passages, the Dutch run, late chunking and auto-merge retrieval. Scripts for most of them exist
(`embed.py`, `embed_m3.py`, `score_m3.py`, `colbert_small.py`, `rerank.py`, `prepare_corpus.py --extra`), they were not run
to completion because local CPU embedding is slow and the sandbox restarted several times. The same holds for any number
above that is marked as run before the UTF-8 rebuild.

## Caveats

English benchmark data (Dutch is about 60% of real usage); judged passages are sparse, so absolute scores are a lower bound;
embedding models are local open models, not a hosted provider; one run per variant for quality, single-run latencies;
the 12,000-character output budget and 8-hit limit come from `FileAgentTools` and would change the results if changed.

## Run it

```sh
export FILE_EVAL_HOST="Host=localhost;Port=5433;Username=jarvis;Password=..."   # a scratch server, superuser
python3 prepare_corpus.py RAW OUT en                       # build a library (see the script header for the raw files)
dotnet run -c Release --project . -- index --lib OUT --tag c3200o320 --length 3200 --overlap 320
dotnet run -c Release --project . -- prepare-lex --lib OUT --tag c3200o320
dotnet run -c Release --project . -- run --lib OUT --tag c3200o320 --variant baseline --name baseline
dotnet run -c Release --project . -- run --lib OUT --tag c3200o320 --variant lex --name lex-pgbm25-english \
    --opt q=or --opt rank=pgbm25 --opt cfg=english
python3 evaluate.py OUT c3200o320 baseline lex-pgbm25-english --baseline baseline
```

Embeddings: `python3 embed.py OUT TAG bge-small`, then `embed-load --model bge-small`, then
`run --variant hyb --opt q=or --opt rank=pgbm25 --opt cfg=english --opt model=bge-small`.
