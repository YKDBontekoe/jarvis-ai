# Lesson eval

Checks how Jarvis learns from turns that went wrong, over invented traces and signals (ids, tool names, outcomes and
signal kinds only, as in production). `scenarios.json` has four parts:

| Part | What it checks | Model |
|------|----------------|-------|
| `mining` | which tools `LessonMinerEngine` picks out, how many incidents, and why: a failed tool, a correction (matched to the reply just before it), a regenerate, a thumbs-down (the last thumb wins) or a declined approval. Noise must stay quiet: one incident, old incidents and working tools are not findings | no |
| `counting` | how `LessonCounterEngine` credits a lesson with thumbs-up turns it was shown in and blames it for thumbs-down, regenerated and corrected ones; unrated and too-recent turns and failing tools count for neither | no |
| `selection` | which lessons reach a turn: a feature lesson only while its tool group is active (also for a follow-up after the tool ran), a lesson about an always-on tool always, the best rated five within the size cap | no |
| `drafting` | `ModelLessonDrafter` writes one clean lesson per finding and invents no details (`forbidden` lists words it was never told) | `--model` |

```sh
dotnet run --project tests/eval/Jarvis.LessonEval                      # rules only, seconds, no model
CODEX_HOME=/path/to/codex-home EXTRACTION_EVAL_CODEX=/path/to/codex dotnet run --project tests/eval/Jarvis.LessonEval -- --model
```

The run exits 1 unless every rule scenario passes with no false findings (`--min-pass 1`, `--max-false 0`) and, with
`--model`, at least `--min-drafted` (0.75) of the findings get a clean lesson with at most `--max-invented` (0)
invented details. Add a scenario whenever a real turn teaches a lesson the rules got wrong.

## Results (2026-10-19)

| Rule scenarios | Findings found | False findings |
|---------------:|---------------:|---------------:|
| 19/19 | 6/6 | 0 |

Drafting has only been run against the fixture model (`tests/e2e/fake_codex_app_server.mjs`), which checks the wiring
but not the quality of a real model's lessons. Run it once with a real model and record the result here.
