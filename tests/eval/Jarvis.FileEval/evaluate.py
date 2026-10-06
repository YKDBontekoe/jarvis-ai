#!/usr/bin/env python3
"""Scores run files written by `Jarvis.FileEval run` (or rerank.py) against the BEIR relevance judgments.

A run line holds the ranked chunk rows (line numbers of chunks.jsonl) for one query. A chunk "covers" a judged passage when
at least half of the passage's characters lie inside the chunk, so results are comparable across chunk sizes. What the
model actually reads is simulated like FileAgentTools.SearchFilesAsync: the top 8 chunks, each cut at 3,200 characters,
until 12,000 characters are used. Metrics (all on those 8 chunks):

  success   a judged passage is in the context the model reads (the answer is available)
  recall    judged passages in the context / min(#judged, 5)
  mrr       reciprocal rank of the first chunk that covers a judged passage
  ndcg      nDCG@8 over chunks (gain = grade of the best newly covered passage; ideal = judged passages by grade)
  empty     no chunk returned at all
  chars     characters of context handed to the model (cost)
  density   share of those characters that sit inside judged passages

Aggregation: mean per source, then the macro average over the three sources (so FiQA's 648 queries do not drown the
rest). 95% confidence intervals come from a bootstrap over queries within each source; "d_" columns are paired
differences against --baseline using the same resamples.

Usage: evaluate.py LIB TAG RUN [RUN ...] [--baseline RUN] [--json out.json] [--markdown]
"""
import argparse
import json
import math
import sys
from pathlib import Path

import numpy as np

TOP = 8
EXCERPT = 3200
BUDGET = 12000
HEADER = 120  # per-hit header line in the tool output ("ChunkId: ... | File: ... | Index n")
SOURCES = ["fiqa", "scifact", "nfcorpus"]
METRICS = ["success", "recall", "mrr", "ndcg", "empty", "chars", "density"]


def load_jsonl(path):
    with open(path, encoding="utf-8") as f:
        return [json.loads(line) for line in f if line.strip()]


class Library:
    def __init__(self, lib: Path, tag: str):
        self.lib, self.tag = lib, tag
        docs = load_jsonl(lib / "docs.jsonl")
        self.passage = {}
        for d, doc in enumerate(docs):
            for p in doc["passages"]:
                self.passage[p["pid"]] = (d, p["start"], p["end"])
        self.queries = load_jsonl(lib / "queries.jsonl")
        chunks = load_jsonl(lib / f"idx-{tag}" / "chunks.jsonl")
        self.chunk_doc = np.array([c["doc"] for c in chunks])
        self.chunk_start = np.array([c["start"] for c in chunks])
        self.chunk_end = np.array([c["end"] for c in chunks])
        self.chunk_len = np.array([len(c["content"]) for c in chunks])

    def overlap(self, pid, doc, start, end):
        d, ps, pe = self.passage[pid]
        if d != doc:
            return 0.0
        return max(0, min(pe, end) - max(ps, start)) / max(1, pe - ps)

    def score_query(self, query, hits):
        rel = query["rel"]
        rows = [int(h[0]) for h in hits[:TOP]]
        n_rel = len(rel)
        # ranking metrics over whole chunks
        seen, gains, first = set(), [], 0
        for rank, row in enumerate(rows):
            best = 0
            for pid, grade in rel.items():
                if pid not in seen and self.overlap(pid, self.chunk_doc[row], self.chunk_start[row], self.chunk_end[row]) >= 0.5:
                    seen.add(pid)
                    best = max(best, grade)
            gains.append(best)
            if best and not first:
                first = rank + 1
        dcg = sum((2 ** g - 1) / math.log2(i + 2) for i, g in enumerate(gains))
        ideal = sorted(rel.values(), reverse=True)[:TOP]
        idcg = sum((2 ** g - 1) / math.log2(i + 2) for i, g in enumerate(ideal))
        # what the model reads
        used, chars, shown = 0, 0, []
        for row in rows:
            if used >= BUDGET:
                break
            allowed = min(EXCERPT, BUDGET - used)
            length = min(int(self.chunk_len[row]), allowed)
            shown.append((self.chunk_doc[row], self.chunk_start[row], self.chunk_start[row] + min(length, int(self.chunk_end[row] - self.chunk_start[row]))))
            used += length + HEADER
            chars += length
        covered, relevant_chars = 0, 0
        for pid in rel:
            d, ps, pe = self.passage[pid]
            spans = sorted((s, e) for (dd, s, e) in shown if dd == d and e > ps and s < pe)
            total, cursor = 0, ps
            for s, e in spans:
                s, e = max(s, cursor), min(e, pe)
                if e > s:
                    total += e - s
                    cursor = e
            relevant_chars += total
            if total / max(1, pe - ps) >= 0.5:
                covered += 1
        return {
            "success": float(covered > 0),
            "recall": min(1.0, covered / min(n_rel, 5)),
            "mrr": 1.0 / first if first else 0.0,
            "ndcg": dcg / idcg if idcg else 0.0,
            "empty": float(not rows),
            "chars": float(chars),
            "density": relevant_chars / chars if chars else 0.0,
        }


def per_query(lib: Library, name: str):
    runs = {r["qid"]: r for r in load_jsonl(lib.lib / f"runs-{lib.tag}" / f"{name}.jsonl")}
    table = {m: np.zeros(len(lib.queries)) for m in METRICS}
    latency = np.zeros(len(lib.queries))
    for i, q in enumerate(lib.queries):
        run = runs[q["qid"]]
        for m, v in lib.score_query(q, run["hits"]).items():
            table[m][i] = v
        latency[i] = run["ms"]
    return table, latency


def macro(values, sources, index_by_source, resample=None):
    """Mean over the sources of the per-source means (optionally on bootstrap-resampled query indices)."""
    means = []
    for s in SOURCES:
        idx = index_by_source[s] if resample is None else resample[s]
        means.append(values[idx].mean())
    return float(np.mean(means))


def main():
    global TOP, BUDGET
    ap = argparse.ArgumentParser()
    ap.add_argument("lib")
    ap.add_argument("tag")
    ap.add_argument("runs", nargs="+")
    ap.add_argument("--baseline")
    ap.add_argument("--json")
    ap.add_argument("--boot", type=int, default=400)
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--top", type=int, default=TOP, help="chunks handed to the model (the tool returns 8)")
    ap.add_argument("--budget", type=int, default=BUDGET, help="tool output budget in characters (the tool uses 12000)")
    args = ap.parse_args()
    TOP, BUDGET = args.top, args.budget

    lib = Library(Path(args.lib), args.tag)
    sources = np.array([q["source"] for q in lib.queries])
    index_by_source = {s: np.where(sources == s)[0] for s in SOURCES}
    rng = np.random.default_rng(args.seed)
    resamples = [{s: rng.choice(index_by_source[s], size=len(index_by_source[s])) for s in SOURCES} for _ in range(args.boot)]

    scored = {name: per_query(lib, name) for name in dict.fromkeys(list(args.runs) + ([args.baseline] if args.baseline else []))}
    base = scored.get(args.baseline)
    out = {}
    for name in args.runs:
        table, latency = scored[name]
        row = {"latency_p50_ms": float(np.percentile(latency, 50)), "latency_p95_ms": float(np.percentile(latency, 95))}
        for m in METRICS:
            boots = [macro(table[m], sources, index_by_source, r) for r in resamples]
            row[m] = {
                "all": macro(table[m], sources, index_by_source),
                "ci": [float(np.percentile(boots, 2.5)), float(np.percentile(boots, 97.5))],
                **{s: float(table[m][index_by_source[s]].mean()) for s in SOURCES},
            }
            if base is not None and name != args.baseline:
                diffs = [macro(table[m] - base[0][m], sources, index_by_source, r) for r in resamples]
                row[m]["delta"] = [macro(table[m] - base[0][m], sources, index_by_source),
                                   float(np.percentile(diffs, 2.5)), float(np.percentile(diffs, 97.5))]
        out[name] = row

    head = f"{'run':34s} {'success':>8s} {'recall':>7s} {'mrr':>6s} {'ndcg':>6s} {'empty':>6s} {'chars':>6s} {'p50ms':>7s} {'p95ms':>7s}"
    print(head)
    for name, row in out.items():
        print(f"{name:34s} {row['success']['all']:8.3f} {row['recall']['all']:7.3f} {row['mrr']['all']:6.3f} "
              f"{row['ndcg']['all']:6.3f} {row['empty']['all']:6.3f} {row['chars']['all']:6.0f} "
              f"{row['latency_p50_ms']:7.1f} {row['latency_p95_ms']:7.1f}")
    print("\nper source (success / ndcg):")
    for name, row in out.items():
        cells = "  ".join(f"{s}: {row['success'][s]:.2f}/{row['ndcg'][s]:.2f}" for s in SOURCES)
        print(f"{name:34s} {cells}")
    if base is not None:
        print(f"\nvs {args.baseline} (macro, paired bootstrap 95% CI):")
        for name, row in out.items():
            if "delta" not in row["success"]:
                continue
            d = lambda m: f"{row[m]['delta'][0]:+.3f} [{row[m]['delta'][1]:+.3f},{row[m]['delta'][2]:+.3f}]"
            print(f"{name:34s} success {d('success')}  ndcg {d('ndcg')}")
    if args.json:
        Path(args.json).write_text(json.dumps(out, indent=1))


if __name__ == "__main__":
    main()
