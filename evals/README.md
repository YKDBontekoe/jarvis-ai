# Jarvis behavior evaluations

`jarvis-core-v1.jsonl` is the source controlled behavioral evaluation set for the production architecture plan. Each line is an independent scenario. `expected` describes observable properties, not exact prose, so model wording can change without making the evaluation brittle.

The cases cover the plan's memory recall and correction handling, tool choice and arguments, unsafe action refusal, approval generation, research honesty, and durable work. They also cover Jarvis-managed MCP discovery, tool selection, invocation, and registration under the Codex CLI plus ChatGPT OAuth model path. The research case uses Codex CLI's native live web search and checks its privacy-safe `jarvis.codex.web_search.actions` metric; it does not use a separate search provider.

## Running evaluations

Run against an isolated Jarvis Development deployment backed by disposable PostgreSQL and Temporal state. Seed each case's `fixtures`, send `input` through the normal conversation API, and collect the persisted assistant message, tool activity, and approval records. Compare those observations with `expected`. Do not substitute direct model-provider calls: model inference must go through the signed-in Codex CLI app-server. Keep the selected model ID and dataset version with each result.

Cases marked with `side_effects` require an isolated owner and cleanup after the run. Use a fake MCP endpoint and fake workflow or reminder sinks for evaluations that must not contact real services. Never put real memories, account credentials, personal data, or production endpoints in this dataset.

Run the relevant cases before deploying changes to prompts, model selection, tools, memory retrieval, or agent configuration. The dataset is deliberately model-agnostic; assertion executors should remain outside the production request path.

## Case format

- `id`: stable, unique case name.
- `category`: behavior being evaluated.
- `input`: user message sent to Jarvis.
- `fixtures`: owner-scoped memories, tasks, and/or fake MCP tools available for this run.
- `expected`: required observable properties. `tool_calls` checks tool name and argument constraints; `approval` checks the human approval boundary; `response` checks user-visible behavior.
- `side_effects`: lists durable actions that the isolated harness must clean up.
