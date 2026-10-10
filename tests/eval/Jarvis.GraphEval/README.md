# Knowledge-graph eval

Feeds ordered, invented memories through `KnowledgeGraphExtractor` one at a time (as the indexer does after each new
memory), merges the facts with the same rules as `KnowledgeGraphRepository.MergeAsync` (kept in sync by hand in
`SimulatedGraph`), and checks the end state without depending on exact predicate names: after a move the old city is
no longer current, liking three things keeps all three, a sold car is no longer driven, the same person is one entity.

```sh
CODEX_HOME=/path/to/codex-home EXTRACTION_EVAL_CODEX=/path/to/codex dotnet run --project tests/eval/Jarvis.GraphEval -- --runs 2
```

`--verbose` prints every relation of every scenario.

## Results (2026-10-10, Codex account default model, 12 scenarios × 2 runs)

| Extractor | Scenarios | Checks | Distinct predicates |
|-----------|----------:|-------:|--------------------:|
| Before | 20/24 | 49/54 | 29 |
| After | 24/24 | 54/54 | 22 |

Before, "sold the Volvo and bought a Tesla" left "drives Volvo" current (the graph had no way to end a fact), and
"plays tennis on Tuesdays" became `plays_tennis_on → Tuesdays`, so tennis was not an entity at all. Predicates such as
`discussed_with_mark` also put objects into predicate names, which stops an exclusive fact from closing the previous
one. The extractor now emits `ended` facts, uses a shared predicate vocabulary (also in dreaming), keeps objects out of
predicates and skips one-off happenings; the merge ignores `exclusive` on many-valued predicates such as `likes`.
