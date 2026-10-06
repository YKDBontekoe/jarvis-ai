# Predictions (written before the model, late-interaction, rerank, scale and Dutch runs finished)

Metric: "success" = a judged passage is in the context the agent tool hands the model (macro average over three sources).
Already observed when this was written: baseline ~0.14-0.16, OR + BM25 ~0.51 (full) / ~0.60 (mid), MiniLM alone at 3,200-char
chunks 0.43, BM25 + weak MiniLM fused 0.53 (worse than BM25 alone), pg_textsearch ~ hand-built BM25, query rewrites +7 pts on BM25.

| # | Prediction | Confidence |
|---|-----------|-----------|
| 1 | BM25 variants stay the best *lexical* option; stemming (`english`) adds 1-3 pts; `ts_rank_cd` and length normalisation add < 2 pts | high |
| 2 | Chunk size: 800-1,600 chars beat the production 3,200 on nDCG and density; success within the 12k-char budget moves < 3 pts; 400 loses; overlap (0 / 10% / 25%) moves < 1 pt | medium |
| 3 | Recursive (paragraph-aware) chunker ~ fixed chunker (within 1 pt); the title/heading prefix helps < 2 pts because passages already start with their own title | medium |
| 4 | Dense models at 1,600 chars: bge-small ~ MiniLM +3-5 pts; Qwen3-Embedding-0.6B and BGE-M3 are the best, +5-10 over MiniLM; EmbeddingGemma between bge-small and Qwen3 | medium |
| 5 | A strong dense model alone matches or beats BM25 on FiQA, ties on SciFact/NFCorpus; overall within +-4 pts of BM25 | medium |
| 6 | RRF hybrid of BM25 + a strong dense model beats both parts by 3-6 pts; with a weak dense model it can fall below BM25 alone (already seen once) | high |
| 7 | BGE-M3 multi-vector ~ its dense score (+-2); dense+sparse+multi-vector fusion is its best single setting (+1-3); sparse alone is weaker than BM25 | medium |
| 8 | ColBERT-small (33M) ~ bge-small level; as a reranker over BM25's top 100 it adds 4-8 pts to BM25 | low-medium |
| 9 | Cross-encoder rerank of the top 30 gives the biggest gain on MRR/nDCG (+5-10) but only +2-4 on success (recall@30 is the ceiling); bge-reranker-v2-m3 / Qwen3-Reranker beat ms-marco MiniLM by 2-4 pts; on NFCorpus the old reranker may not help at all | medium |
| 10 | Query rewrites help hybrid less than BM25 (+2-4 vs +7) because dense retrieval already bridges vocabulary gaps | medium |
| 11 | Scoped search (1-5% of files in scope): HNSW without iterative scans loses most results (success drops by half or more, many empty answers); `relaxed_order` restores it; lexical is unaffected | high |
| 12 | Scale (+171k biomedical distractors): `ts_rank` OR queries get several times slower, pg_textsearch stays well under 50 ms; success drops 3-8 pts on SciFact/NFCorpus, FiQA barely moves | medium |
| 13 | Dutch: English-only models (MiniLM, bge-small) lose 15-25 pts versus their English score; multilingual ones (e5, BGE-M3, Qwen3) lose 5-10; the `dutch` text config adds 3-6 pts over `simple` | high |
| 14 | Best practical stack: pg_textsearch BM25 + a multilingual dense model (RRF) at ~1,600-char chunks, optionally reranked; it beats today's pipeline by 0.6+ success | high |

Most likely to be wrong: #8 (implementation shortcuts in my ColBERT code), #4 ordering of Gemma vs bge-small, #3, #12 (distractors from one domain).
