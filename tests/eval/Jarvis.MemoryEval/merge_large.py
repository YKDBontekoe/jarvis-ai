"""Merges the per-area files (memories + labelled queries written by a model) into one dataset for --dataset and
validates keys and labels. Usage: python3 merge_large.py out.json area1.json area2.json ..."""
import json, sys

out, parts = sys.argv[1], sys.argv[2:]
memories, queries, keys = [], [], set()
for part in parts:
    data = json.load(open(part))
    for memory in data["memories"]:
        assert memory["key"] not in keys, "duplicate key " + memory["key"]
        keys.add(memory["key"])
        memories.append(memory)
    queries += data["queries"]
expired = {m["key"] for m in memories if m.get("expired")}
for query in queries:
    query["relevant"] = {k: g for k, g in query["relevant"].items() if k in keys and k not in expired and g in (1, 2)}
queries = [q for q in queries if any(g == 2 for g in q["relevant"].values())]
json.dump({"memories": memories, "otherOwnerMemories": ["Other user's sister is called Anna and lives in Rotterdam."],
           "queries": queries}, open(out, "w"), ensure_ascii=False, indent=1)
print(len(memories), "memories", len(queries), "queries", len(expired), "expired")
