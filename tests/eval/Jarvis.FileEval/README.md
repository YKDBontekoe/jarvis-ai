# File retrieval eval

Offline eval for file search (`FileSearchService`). It chunks six invented documents (a lease, an employee handbook, an
espresso machine manual, a trip itinerary, a Dutch insurance policy and renovation meeting notes; PDFs as pages,
Markdown with headings) the way the worker does, stores them for one owner plus a copy for a second owner to check
isolation, and scores the ranked chunks for 50 questions (English and Dutch, some cross-language) in `dataset.json`.
A chunk counts as relevant when it comes from the right document and contains the passage that answers the question.

```sh
# Scratch database with pgvector and pg_trgm available; the eval migrates it and deletes all files in it.
export FILE_EVAL_DB="Host=localhost;Database=jarvis_eval;Username=jarvis;Password=<local password>"
dotnet run --project tests/eval/Jarvis.FileEval -- before            # previous pipeline
dotnet run --project tests/eval/Jarvis.FileEval -- after             # current keyword path
dotnet run --project tests/eval/Jarvis.FileEval -- after --verbose   # plus every query that missed hit@1
dotnet run --project tests/eval/Jarvis.FileEval -- after --scale 400 # plus 400 filler documents (~2,400 chunks)
dotnet run --project tests/eval/Jarvis.FileEval -- after --chunk-length 3200
```

Never point it at a database with real data.

`before` reproduces the previous pipeline: 3,200-character chunks without headings, and `websearch_to_tsquery`, which
requires every word of the question, ranked by `ts_rank`. `after` runs the current chunking and `FileSearchService`.

Metrics: recall@3/@8 (the answer is in the top 3/8 chunks), hit@1, MRR, queries with no results, leaks of the other
owner's chunks, the characters of excerpts read up to and including the first answering chunk (context cost), and
search latency (median of five timed runs per query after a warm-up run; p50/p95 over queries).

## Semantic path (local open model)

```sh
pip install sentence-transformers
dotnet run --project tests/eval/Jarvis.FileEval -- after --dump-texts /tmp/texts.json
python3 tests/eval/Jarvis.FileEval/embed_texts.py sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2 \
    /tmp/texts.json /tmp/file-emb.json
dotnet run --project tests/eval/Jarvis.FileEval -- after --embeddings /tmp/file-emb.json
```

The dump holds what `FileChunkIndexer` embeds (file name, page and heading in front of each chunk) and the queries;
dump again whenever the chunking changes (`--chunk-length` included).

## Results (2026-10-14, PostgreSQL 18 with pgvector, in Docker)

| Pipeline | Chunks | Recall@3 | Recall@8 | Hit@1 | MRR | Empty | Context to answer | p50 |
|----------|-------:|---------:|---------:|------:|----:|------:|------------------:|----:|
| before (AND query, 3,200 chars) | 8 | 0.04 | 0.04 | 0.04 | 0.04 | 48 | 2,056 | 2.3 ms |
| after, keyword (1,600 chars) | 15 | 0.84 | 0.86 | 0.68 | 0.76 | 2 | 1,700 | 8.9 ms |
| after + MiniLM (1,600 chars) | 15 | 0.90 | 0.90 | 0.76 | 0.82 | 0 | 1,550 | 18.5 ms |
| after, keyword, 800 chars | 28 | 0.80 | 0.84 | 0.60 | 0.70 | 2 | 908 | 9.0 ms |
| after + MiniLM, 800 chars | 28 | 0.90 | 0.94 | 0.78 | 0.85 | 0 | 821 | 16.0 ms |
| after, keyword, 3,200 chars | 8 | 0.88 | 0.90 | 0.78 | 0.84 | 2 | 2,876 | 9.2 ms |
| after + MiniLM, 3,200 chars | 8 | 0.94 | 0.96 | 0.80 | 0.88 | 1 | 2,803 | 16.2 ms |

The old query almost never matched a question: it needed every word, stopwords included, in one chunk. The OR-ed,
BM25-style query is the bulk of the gain; embeddings mainly fix cross-language and paraphrased questions.

Chunk size is a trade-off this set cannot settle: its documents are 1,600 to 3,600 characters, so a 3,200-character
chunk is close to a whole document and finding the document almost always means finding the answer. Larger chunks
score a little higher here but put 1.7 to 1.8 times more text in front of the model per answer, and the automatic
excerpts for attached sources show at most 1,500 characters per passage. The default stays at 1,600; a set with longer
documents is needed to choose it with confidence.

With `--scale 400` (word salad built from the documents' own vocabulary, so every filler chunk matches most query terms)
keyword recall@8 drops to 0.36 and p50/p95 latency is about 40/120 ms. The pg_trgm typo pass is the costly step on long
chunks (1.5 s per query at this scale when it ran for every term), which is why it only runs as a fallback when the
keywords find fewer than three chunks.

Limits: 50 queries written alongside the documents, no held-out set yet, a small open embedding model (MiniLM reads
only the first 128 tokens of a chunk), and no model reranking.
