#!/usr/bin/env python3
"""Prepares, collects and scores the three retrieval routes for the routing benchmark.

Routes (all share the hybrid search service):
  direct     one search with the question (top 8 passages), then one answer step
  decompose  a model writes 2-4 independent sub-queries without seeing any passage; each is searched (top 4) plus the
             original question (top 3); the merged passages (at most 12) go to one answer step
  agentic    a model with only the search command searches, reads, and searches again (budget 6 searches) before answering

Subcommands (EXEC is a working directory the executing models may read; gold answers live elsewhere):
  prep-direct      EXEC/direct_in_N.json         questions with their retrieved passages
  prep-decomp-q    EXEC/decomp_q_in_N.json       questions only (stage 1)
  prep-decomp-a    EXEC/decomp_a_in_N.json       questions with the passages retrieved for their sub-queries (stage 2)
  prep-agentic     EXEC/agentic_task_N.md        task files with a ready search command per question
  prep-router      EXEC/router_in.json           questions for a zero-shot model router
  score            table of answers and costs per route (reads EXEC/*_out_*.json and calls.jsonl)

Usage: route_exec.py ROUTE_DIR EXEC_DIR SUBCOMMAND
"""
import json
import re
import string
import sys
import urllib.parse
import urllib.request
from collections import Counter, defaultdict
from pathlib import Path

SERVICE = "http://localhost:8765/search"


def load(path):
    return [json.loads(l) for l in open(path, encoding="utf-8")]


def search(qid, query, k):
    url = SERVICE + "?" + urllib.parse.urlencode({"qid": qid, "q": query, "k": k})
    return urllib.request.urlopen(url, timeout=60).read().decode("utf-8")


def chunks(items, size):
    return [items[i:i + size] for i in range(0, len(items), size)]


def normalize(text):
    text = text.lower()
    text = "".join(c for c in text if c not in set(string.punctuation))
    text = re.sub(r"\b(a|an|the)\b", " ", text)
    return " ".join(text.split())


def f1(prediction, gold):
    p, g = normalize(prediction).split(), normalize(gold).split()
    common = Counter(p) & Counter(g)
    same = sum(common.values())
    if not p or not g or same == 0:
        return 0.0
    precision, recall = same / len(p), same / len(g)
    return 2 * precision * recall / (precision + recall)


def score_answer(prediction, golds):
    pn = normalize(prediction)
    return {
        "em": float(any(pn == normalize(g) for g in golds)),
        "f1": max(f1(prediction, g) for g in golds),
        "contains": float(any(normalize(g) and normalize(g) in pn for g in golds)),
    }


def main():
    route_dir, exec_dir, command = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
    exec_dir.mkdir(parents=True, exist_ok=True)
    test = load(route_dir / "test.jsonl")
    if command == "prep-direct":
        items = [{"qid": t["qid"], "question": t["question"],
                  "passages": search("direct-" + t["qid"], t["question"], 8)} for t in test]
        for i, batch in enumerate(chunks(items, 30)):
            (exec_dir / f"direct_in_{i}.json").write_text(json.dumps(batch, indent=1, ensure_ascii=False))
    elif command == "prep-decomp-q":
        for i, batch in enumerate(chunks([{"qid": t["qid"], "question": t["question"]} for t in test], 30)):
            (exec_dir / f"decomp_q_in_{i}.json").write_text(json.dumps(batch, indent=1, ensure_ascii=False))
    elif command == "prep-decomp-a":
        sub = {}
        for f in sorted(exec_dir.glob("decomp_q_out_*.json")):
            sub.update(json.loads(f.read_text()))
        items = []
        for t in test:
            queries = [q for q in sub.get(t["qid"], []) if q.strip()][:4]
            blocks = [search("decomp-" + t["qid"], t["question"], 3)] + [search("decomp-" + t["qid"], q, 4) for q in queries]
            seen, merged = set(), []
            for block in blocks:
                for passage in block.split("\n\n"):
                    key = passage.split("] ", 1)[-1][:80]
                    if key not in seen:
                        seen.add(key)
                        merged.append(passage.split("] ", 1)[-1])
            items.append({"qid": t["qid"], "question": t["question"], "sub_queries": queries, "passages": merged[:12]})
        for i, batch in enumerate(chunks(items, 30)):
            (exec_dir / f"decomp_a_in_{i}.json").write_text(json.dumps(batch, indent=1, ensure_ascii=False))
    elif command == "prep-agentic":
        for i, batch in enumerate(chunks(test, 6)):
            lines = [f"# Task file {i}\n"]
            for t in batch:
                cmd = ('curl -s -G "http://localhost:8765/search" --data-urlencode "qid=agentic-%s" --data-urlencode "k=5" '
                       '--data-urlencode "q=YOUR QUERY HERE"' % t["qid"])
                lines.append(f"## Question id: {t['qid']}\nQuestion: {t['question']}\nSearch command for this question:\n    {cmd}\n")
            (exec_dir / f"agentic_task_{i}.md").write_text("\n".join(lines))
    elif command == "prep-router":
        ood = load(Path(__file__).parent / "ood_assistant.jsonl")
        items = [{"id": t["qid"], "question": t["question"]} for t in test] + \
                [{"id": f"assistant-{i}", "question": o["text"]} for i, o in enumerate(ood)]
        (exec_dir / "router_in.json").write_text(json.dumps(items, indent=1, ensure_ascii=False))
    elif command == "score":
        gold = {g["qid"]: g for g in load(route_dir / "gold.jsonl")}
        calls = defaultdict(list)
        for c in load(route_dir / "calls.jsonl"):
            calls[c["qid"]].append(c)
        out = {}
        for route, prefix in (("direct", "direct_out_"), ("decompose", "decomp_a_out_"), ("agentic", "agentic_out_")):
            answers = {}
            for f in sorted(exec_dir.glob(prefix + "*.json")):
                answers.update(json.loads(f.read_text()))
            for t in test:
                g = gold[t["qid"]]
                # a question the executing model skipped counts as wrong, never as absent
                s = score_answer(str(answers.get(t["qid"], "")), g["answers"])
                s["answered"] = float(t["qid"] in answers)
                seen = {p for c in calls.get(f"{ {'direct': 'direct', 'decompose': 'decomp', 'agentic': 'agentic'}[route]}-{t['qid']}", []) for p in c["pids"]}
                n_search = len(calls.get(f"{ {'direct': 'direct', 'decompose': 'decomp', 'agentic': 'agentic'}[route]}-{t['qid']}", []))
                s.update({"support_recall": len(seen & set(g["support"])) / max(1, len(g["support"])), "searches": n_search,
                          "llm_calls": {"direct": 1, "decompose": 2, "agentic": n_search + 1}[route]})
                out.setdefault(t["qid"], {"route": t["route"], "hops": t["hops"]})[route] = s
        (exec_dir / "matrix.json").write_text(json.dumps(out, indent=1))
        for route in ("direct", "decompose", "agentic"):
            rows = [v[route] for v in out.values() if route in v]
            if rows:
                print(route, len(rows), {k: round(sum(r[k] for r in rows) / len(rows), 3) for k in rows[0]})


if __name__ == "__main__":
    main()
