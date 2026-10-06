#!/usr/bin/env python3
"""Reorders the top candidates of a run with a local cross-encoder and writes a new run file.

A model rerank is the standard fix for a retriever whose top 8 are noisy: retrieve a wider pool (default 30), score
every (query, chunk) pair, keep the best. Pairs are scored on CPU, so this measures quality only; the latency of a
real provider rerank would add a model round trip.

Usage: rerank.py LIB TAG SOURCE_RUN NEW_RUN [--model cross-encoder/ms-marco-MiniLM-L-6-v2] [--pool 30] [--threads 3]
"""
import argparse
import json
import sys
import time
from pathlib import Path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("lib")
    ap.add_argument("tag")
    ap.add_argument("source")
    ap.add_argument("name")
    ap.add_argument("--model", default="cross-encoder/ms-marco-MiniLM-L-6-v2")
    ap.add_argument("--pool", type=int, default=30)
    ap.add_argument("--threads", type=int, default=3)
    ap.add_argument("--max-length", type=int, default=512)
    args = ap.parse_args()

    import torch
    from sentence_transformers import CrossEncoder

    torch.set_num_threads(args.threads)
    lib = Path(args.lib)
    queries = {}
    for line in open(lib / "queries.jsonl", encoding="utf-8"):
        q = json.loads(line)
        queries[q["qid"]] = q["text"]
    chunks = [json.loads(l)["content"] for l in open(lib / f"idx-{args.tag}" / "chunks.jsonl", encoding="utf-8")]
    model = CrossEncoder(args.model, device="cpu", max_length=args.max_length)
    runs = [json.loads(l) for l in open(lib / f"runs-{args.tag}" / f"{args.source}.jsonl", encoding="utf-8")]
    out_path = lib / f"runs-{args.tag}" / f"{args.name}.jsonl"
    started = time.time()
    with open(out_path, "w", encoding="utf-8") as out:
        for i, run in enumerate(runs):
            pool = run["hits"][: args.pool]
            if pool:
                scores = model.predict([(queries[run["qid"]], chunks[int(row)]) for row, _ in pool], batch_size=16,
                                       show_progress_bar=False)
                order = sorted(range(len(pool)), key=lambda j: -scores[j])
                hits = [[pool[j][0], float(scores[j])] for j in order]
            else:
                hits = []
            out.write(json.dumps({"qid": run["qid"], "ms": run["ms"], "leaks": run.get("leaks", 0), "hits": hits}) + "\n")
            if i % 100 == 0:
                print(f"{i}/{len(runs)} queries, {time.time() - started:.0f}s", file=sys.stderr, flush=True)
    print(f"wrote {out_path} in {(time.time() - started) / 60:.1f} min", file=sys.stderr)


if __name__ == "__main__":
    main()
