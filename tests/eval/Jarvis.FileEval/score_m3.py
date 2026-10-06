#!/usr/bin/env python3
"""Turns the BGE-M3 outputs of embed_m3.py into run files for evaluate.py (exact brute force, no database).

  m3-sparse      learned sparse weights (dot product over shared tokens)
  m3-colbert     multi-vector MaxSim over every chunk (late interaction)
  m3-all         dense*0.4 + sparse*0.2 + colbert*0.4 (the weights from the model card)
  m3-colbert-rr  MaxSim only over the top-N candidates of --lex (how late interaction is deployed cheaply)
  m3-all+lex     RRF of m3-all and the --lex run
Latency in these files is numpy wall time per query on CPU and is not comparable with the database runs.

Usage: score_m3.py LIB TAG --lex RUN_NAME [--pool 100]
"""
import argparse
import json
import time
from pathlib import Path

import numpy as np
import scipy.sparse as sp


def load_sparse(path, vocab=250_002):
    rows = json.load(open(path))
    indptr, indices, data = [0], [], []
    for r in rows:
        for k, v in r.items():
            indices.append(int(k))
            data.append(v)
        indptr.append(len(indices))
    return sp.csr_matrix((data, indices, indptr), shape=(len(rows), vocab), dtype=np.float32)


def load_colbert(prefix_path):
    offsets = np.load(f"{prefix_path}.offsets.npy")
    flat = np.fromfile(f"{prefix_path}.f16", dtype=np.float16).reshape(-1, 1024)
    return flat, offsets


def write_run(path, qids, ranked, ms):
    with open(path, "w") as f:
        for i, q in enumerate(qids):
            f.write(json.dumps({"qid": q, "ms": ms[i], "leaks": 0, "hits": [[int(r), float(s)] for r, s in ranked[i]]}) + "\n")


def top(scores, k=30):
    idx = np.argpartition(-scores, min(k, len(scores) - 1))[:k]
    idx = idx[np.argsort(-scores[idx])]
    return [(i, scores[i]) for i in idx]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("lib")
    ap.add_argument("tag")
    ap.add_argument("--lex", required=True)
    ap.add_argument("--pool", type=int, default=100)
    args = ap.parse_args()
    lib, idx = Path(args.lib), Path(args.lib) / f"idx-{args.tag}"
    runs = lib / f"runs-{args.tag}"
    qids = [json.loads(l)["qid"] for l in open(lib / "queries.jsonl")]
    n = sum(1 for _ in open(idx / "chunks.jsonl"))

    dim = 1024
    cd = np.fromfile(idx / "emb-bge-m3.f32", dtype=np.float32).reshape(n, dim)
    qd = np.fromfile(lib / "qemb-bge-m3.f32", dtype=np.float32).reshape(len(qids), dim)
    cs, qs = load_sparse(idx / "m3-sparse.json"), load_sparse(lib / "q-m3-sparse.json")
    cflat, coff = load_colbert(idx / "m3-colbert")
    qflat, qoff = load_colbert(lib / "q-m3-colbert")
    cflat = cflat.astype(np.float32)

    dense = qd @ cd.T
    sparse = (qs @ cs.T).toarray()
    colbert = np.zeros((len(qids), n), np.float32)
    ms_col = []
    for qi in range(len(qids)):
        t = time.time()
        q = qflat[qoff[qi]:qoff[qi + 1]].astype(np.float32)
        sims = q @ cflat.T                                   # query tokens x all chunk tokens
        per_chunk = np.maximum.reduceat(sims, coff[:-1], axis=1)  # best match per query token per chunk
        colbert[qi] = per_chunk.sum(0) / len(q)
        ms_col.append((time.time() - t) * 1000)
    allw = 0.4 * dense + 0.2 * sparse + 0.4 * colbert

    write_run(runs / "m3-dense.jsonl", qids, [top(dense[i]) for i in range(len(qids))], [0] * len(qids))
    write_run(runs / "m3-sparse.jsonl", qids, [top(sparse[i]) for i in range(len(qids))], [0] * len(qids))
    write_run(runs / "m3-colbert.jsonl", qids, [top(colbert[i]) for i in range(len(qids))], ms_col)
    write_run(runs / "m3-all.jsonl", qids, [top(allw[i]) for i in range(len(qids))], ms_col)

    # candidate-restricted late interaction and fusion with the lexical run
    lex = {r["qid"]: r for r in (json.loads(l) for l in open(runs / f"{args.lex}.jsonl"))}
    rr, fused = [], []
    for qi, q in enumerate(qids):
        cand = [int(r) for r, _ in lex[q]["hits"][: args.pool]]
        rr.append(sorted(((c, colbert[qi, c]) for c in cand), key=lambda x: -x[1])[:30])
        score = {}
        for rank, (c, _) in enumerate(top(allw[qi], 30)):
            score[c] = score.get(c, 0) + 1 / (60 + rank + 1)
        for rank, (c, _) in enumerate(lex[q]["hits"][:30]):
            score[int(c)] = score.get(int(c), 0) + 1 / (60 + rank + 1)
        fused.append(sorted(score.items(), key=lambda x: -x[1])[:30])
    write_run(runs / "m3-colbert-rr.jsonl", qids, rr, [lex[q]["ms"] for q in qids])
    write_run(runs / "m3-all+lex.jsonl", qids, fused, [0] * len(qids))
    print("wrote m3-dense, m3-sparse, m3-colbert, m3-all, m3-colbert-rr, m3-all+lex")


if __name__ == "__main__":
    main()
