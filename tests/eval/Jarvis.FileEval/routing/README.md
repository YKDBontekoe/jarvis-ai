# Query routing: direct, decomposition, agentic

Question: should a small trained model decide, per question, how much retrieval effort to spend, and what does each route
cost and buy? Three routes, each run for every question in a 90-question test set:

| Route | What happens | LLM calls |
|-------|--------------|-----------|
| **direct** | one hybrid search with the question (top 8), then answer | 1 |
| **decompose** | a model writes 2-4 independent sub-queries without seeing any passage; each is searched, merged, answered | 2 |
| **agentic** | a model with only the search command searches, reads, searches again (budget 6 per question), answers | searches + 1 |

In Jarvis the agentic route is what the chat agent already does today (`SearchFiles` is a tool it can call repeatedly).
Direct is "inject search results before the model starts"; decompose is a fixed plan.

## Setup

- **Test set (90):** 30 single-hop natural questions (SQuAD dev), 30 two-hop, 15 three-hop, 15 four-hop (MuSiQue dev). The
  label of a question is its hop count: 1 hop = direct, 2 = decompose, 3-4 = agentic (the labelling Adaptive-RAG uses).
- **Corpus:** 6,429 Wikipedia paragraphs: every paragraph of the test questions plus distractors from 250 other questions
  and all SQuAD dev paragraphs. Search is the best pipeline from the file eval: pg_textsearch BM25 + bge-small, RRF.
- **Executing model:** Claude Haiku 4.5 subagents for every route and every LLM step (a cheap model is the realistic
  setting for routing). Agents saw only questions and passages, never gold answers (kept in a separate directory; this is an
  honour-system boundary, not a sandbox).
- **Scoring:** token F1 and "contains" against the gold answer and its aliases; a skipped question counts as wrong.
  Cost = LLM calls and searches per question (an agentic question's LLM calls are approximated as searches + 1).
- **Policies** are evaluated offline from the matrix (every question run through every route), with a bootstrap over questions.
  With n = 90 the intervals are wide: only large differences are real.
- **Routers:** length-feature tree; TF-IDF + logistic regression; frozen bge-small + logistic regression; fine-tuned
  all-MiniLM-L6-v2 (22M); a zero-shot Haiku router. Trained on 12,000 labelled Wikipedia-style questions (MuSiQue train
  2-hop / 3-4-hop, SQuAD train), none of them test questions.

Research this follows: Adaptive-RAG (a small classifier picks no-retrieval / single-step / multi-step; T5-Small to Large
perform alike), the 2026 lightweight-routing baseline on RAGRouter-Bench (SBERT-class routers), and two 2026 component studies
(two retrieval rounds capture most of the agentic gain; reflection loops often hurt; fixed choices can beat adaptive ones).

## What each route does on each kind of question

| True class | Route | F1 | contains | LLM calls | searches |
|-----------|-------|---:|---------:|----------:|---------:|
| 1 hop (n=30) | direct | **0.79** | 0.70 | 1.0 | 1.0 |
| | decompose | 0.66 | 0.53 | 2.0 | 3.1 |
| | agentic | 0.72 | 0.63 | 2.1 | 1.1 |
| 2 hops (n=30) | direct | 0.66 | 0.63 | 1.0 | 1.0 |
| | decompose | 0.65 | 0.63 | 2.0 | 3.4 |
| | agentic | **0.77** | 0.80 | 3.7 | 2.7 |
| 3-4 hops (n=30) | direct | 0.27 | 0.10 | 1.0 | 1.0 |
| | decompose | 0.27 | 0.10 | 2.0 | 4.0 |
| | agentic | **0.48** | 0.40 | 5.8 | 4.8 |

- **Decomposition is dominated.** With sub-queries planned up front it is never better than agentic, costs 2-4x the
  searches of direct, and beats a single search only on 4-hop questions (F1 0.42 vs 0.32). A fixed plan cannot use the
  entity found in hop 1 to search for hop 2, which is exactly what multi-hop questions need.
- **Agentic regulates its own cost.** On single-hop questions it stopped after 1.1 searches. On 4-hop questions it used 6.2.
- **Agentic finds the evidence:** support recall on 3-4 hop questions is 0.77-0.91 against 0.33-0.69 for direct.
- On single-hop questions direct is as good or better (0.79 vs 0.72, within noise at n = 30).

## Policies (all 90 questions)

| Policy | F1 (95% CI) | LLM calls | vs always-agentic |
|--------|-------------|----------:|-------------------|
| always-direct | 0.573 [0.48, 0.66] | 1.00 | -0.084 [-0.18, +0.01], 74% fewer calls |
| always-decompose | 0.528 [0.44, 0.62] | 2.00 | -0.129 [-0.22, -0.04] |
| always-agentic | 0.657 [0.57, 0.75] | 3.88 | |
| hop-count label (a perfect router for the training target) | 0.641 | 2.94 | -0.016 [-0.09, +0.06], 24% fewer calls |
| router: MiniLM fine-tuned (22M, 12 ms/query CPU) | 0.630 | 2.73 | -0.026 [-0.10, +0.05], 30% fewer calls |
| router: bge-small + logistic regression | 0.643 | 2.60 | -0.014 |
| router: TF-IDF + logistic regression (0.1 ms) | 0.613 | 2.48 | -0.045 |
| router: length-feature tree | 0.647 | 2.74 | -0.010 |
| router: zero-shot Haiku | 0.581 | 2.24 | -0.076 |
| oracle (best route per question, cheapest on ties) | 0.760 | 1.76 | +0.103 [+0.05, +0.16], 55% fewer calls |

Exploratory (chosen after seeing the matrix): send every "decompose" prediction to agentic, i.e. a two-route system.
Fine-tuned MiniLM 0.686, bge-small + LR 0.682, TF-IDF 0.675, always-agentic 0.657, with 11-16% fewer LLM calls. All
intervals include zero.

Reading: the routers do not buy much on this benchmark. Even a perfect hop-count router saves about a quarter of the calls
at no significant quality change, because agentic already stops early on easy questions. The oracle shows what is possible
(+0.10 F1 at 55% fewer calls, an optimistic ceiling since it takes the best of three noisy runs), and that hop count is
the wrong target: the oracle agrees with the hop label for only 41% of questions. Many multi-hop questions fail on every
route, and many "hard" ones are answered by one search. Labels from observed outcomes (the cheapest route that answers
correctly) would be a better training target, but need every route run over thousands of training questions.

## Router quality

| Router (trained on Wikipedia-style questions) | dev acc | test acc | assistant-style acc (36 hand-written) |
|---|---:|---:|---:|
| length tree | 0.70 | 0.63 | 0.53 |
| TF-IDF + LR | 0.70 | 0.77 | 0.39 |
| bge-small + LR | 0.73 | 0.72 | 0.33 |
| MiniLM fine-tuned | **0.89** | **0.90** | 0.36 |
| zero-shot Haiku | | 0.71 | 0.97 |

Trained on Wikipedia trivia, every router collapses on questions about personal documents (31 of 36 sent to "direct"):
they learned the shape of trivia questions, not the amount of retrieval needed. The zero-shot LLM transfers (0.97) but
confuses agentic with decompose on the benchmark (19 of 30 three/four-hop questions called "decompose").

## A router for the assistant domain

Four Sonnet agents wrote 1,800 labelled assistant-style requests (finance, housing, work, health/travel; 70% English, 30%
Dutch; direct / decompose / agentic defined by the retrieval each needs). One life area (health/travel, 450) was held out
completely. 36 English and 18 Dutch requests, hand-written by me and never shown to the writers, are the test.

| Router | trained on | unseen area | English (36) | Dutch (18) | Wikipedia dev | single-hop questions sent to direct |
|---|---|---:|---:|---:|---:|---:|
| TF-IDF (word + char) + LR | assistant | 0.90 | 0.89 | 0.94 | 0.36 | 45% |
| multilingual-e5-small + LR | assistant | 0.91 | 0.83 | 0.89 | 0.41 | 47% |
| multilingual MiniLM fine-tuned | assistant | **0.97** | 0.89 | 0.94 | 0.32 | 40% |
| TF-IDF + LR | assistant + Wikipedia | 0.86 | 0.89 | 0.89 | 0.72 | 88% |
| multilingual-e5-small + LR | assistant + Wikipedia | 0.82 | 0.86 | 0.83 | 0.72 | 65% |
| **multilingual MiniLM fine-tuned (118M, 8.5 ms/query CPU)** | assistant + Wikipedia | 0.93 | **0.92** | 0.89 | **0.89** | 83% |

The last column is a sanity check: ordinary one-hop retrieval questions (FiQA and NFCorpus from the file eval) should
mostly go to direct. Routers trained only on assistant requests send 55-60% of them to the expensive routes (they learned
"short = direct"); mixing in the Wikipedia questions fixes that. The mixed fine-tuned MiniLM is the best all-round router.

## Caveats

- **The assistant results are an upper bound.** Training data and test set were both written by Claude from the same
  definitions, so shared phrasing cues ("compare", "all my", "everything about") make the task easier than real user
  logs. Treat 0.9 as "learnable", not as expected production accuracy. Direct requests are also shorter in the synthetic
  data (11 words vs 15-16), a shortcut the router can use.
- **No end-to-end test in the assistant domain.** The route matrix is from Wikipedia questions; whether the savings carry
  over to documents of one person depends on how many requests are really multi-step.
- 90 test questions, one run per route and question, one executing model (Haiku 4.5), parallel (not sequential)
  decomposition, no reflection step, haiku-style searches capped at 6 per question.
- Questions skipped by an executing model count as wrong (one in the direct route).

## Reproduce

```sh
python3 routing/prepare_routing.py RAW OUT                 # corpus, test set, router training data
python3 routing/search_service.py OUT                      # hybrid search service on localhost:8765
python3 routing/route_exec.py PRIV EXEC prep-direct        # also prep-decomp-q, prep-decomp-a, prep-agentic, prep-router
# run the executing models over the prepared files (this study used Claude subagents), then:
python3 routing/route_exec.py PRIV EXEC score
python3 routing/train_routers.py PRIV routers.json --beir-queries q.jsonl
python3 routing/route_policies.py EXEC/matrix.json routers.json EXEC/router_out.json
python3 routing/train_domain_router.py GEN PRIV domain_routers.json --holdout health_travel
```

Results are in `results/` (`route_matrix.json` per-question outcomes, `policies.txt`, `router_metrics.json`,
`domain_router_metrics.json`).
