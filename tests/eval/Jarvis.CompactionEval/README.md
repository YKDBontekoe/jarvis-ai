# Long-conversation eval

Builds an invented chat of about 180,000 tokens (32 turns, each with a large web-search result) with facts planted in the
early turns: a sister's birthday, a budget that is later corrected, a decision, a promise Jarvis made, a booking
reference that only appears in a tool result, a pet's name, and a web page carrying an injected instruction. It writes
the rolling summary block by block through Codex, the way `RollingSummaryCompaction` builds it over many turns, then
asks Codex seven questions with the history the truncation backstop keeps and with the summarized history.

```sh
CODEX_HOME=/path/to/codex-home EXTRACTION_EVAL_CODEX=/path/to/codex dotnet run --project tests/eval/Jarvis.CompactionEval
```

## Results (2026-10-10, Codex account default model)

| History sent | Messages | Tokens | Correct answers |
|--------------|---------:|-------:|----------------:|
| Truncation only (before) | 45 | 63,148 | 1/7 (only the most recent question) |
| Rolling summary + recent turns | 29 | 40,576 | 7/7 |

Nine summary blocks took 8 to 16 s each, written in the background. The summary kept the corrected budget (and that it
was raised from the old one), the booking reference copied exactly, and recorded the injected instruction only as a
fact about the page. One invented chat and seven questions: treat it as a check that the mechanism works, not a
benchmark.
