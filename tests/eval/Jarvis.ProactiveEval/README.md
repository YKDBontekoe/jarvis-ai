# Proactive scenario evaluation

Run `dotnet run --project tests/eval/Jarvis.ProactiveEval -- --output /tmp/jarvis-proactive-eval.json`.

Eight synthetic scenarios exercise correct intervention, useful preparation, unnecessary interruption and false
completion against production `ProactiveRules`. They are deterministic policy checks, not a live-model or usefulness
study. The report leaves explicit owner usefulness unknown. Case lifecycle, actual preparation, concurrent writes,
pause/restart and HTTP behavior have complementary unit/integration/e2e tests documented in
[proactive-work.md](../../../docs/backend/proactive-work.md).

When evaluating a live deployment, collect explicit useful/not-useful/timing/effort feedback and an explicitly resolved
linked outcome separately. Do not count a check, a generated report, a reply or silence as successful follow-through.
