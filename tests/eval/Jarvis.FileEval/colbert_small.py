#!/usr/bin/env python3
"""answerai-colbert-small-v1 (33M parameters, 96-dimension token vectors): late interaction without a vector database.

Encodes queries and chunks, scores exact MaxSim over every chunk, and also uses it as a reranker over the candidates of a
lexical run. Writes runs colbert-small (exhaustive), colbert-small-rr (rerank --lex candidates) and colbert-small+lex (RRF).
Simplifications versus the reference implementation: no query [MASK] augmentation, documents up to 512 tokens (trained at
300), punctuation tokens skipped for documents. Latency is numpy wall time per query and not comparable with database runs.

Usage: colbert_small.py LIB TAG --lex RUN_NAME [--pool 100] [--threads 3]
"""
import argparse
import json
import string
import sys
import time
from pathlib import Path

import numpy as np

REPO = "answerdotai/answerai-colbert-small-v1"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("lib")
    ap.add_argument("tag")
    ap.add_argument("--lex", required=True)
    ap.add_argument("--pool", type=int, default=100)
    ap.add_argument("--threads", type=int, default=3)
    args = ap.parse_args()

    import torch
    from huggingface_hub import hf_hub_download
    from safetensors.torch import load_file
    from transformers import AutoModel, AutoTokenizer

    torch.set_num_threads(args.threads)
    tok = AutoTokenizer.from_pretrained(REPO)
    bert = AutoModel.from_pretrained(REPO).eval()
    linear = torch.nn.Linear(384, 96, bias=False)
    linear.weight.data = load_file(hf_hub_download(REPO, "model.safetensors"))["linear.weight"]
    skip = {tok.convert_tokens_to_ids(c) for c in string.punctuation}
    q_marker, d_marker = tok.convert_tokens_to_ids("[unused0]"), tok.convert_tokens_to_ids("[unused1]")

    def encode(texts, marker, max_len, drop_punct, label):
        out = [None] * len(texts)
        order = np.argsort([-len(t) for t in texts])
        started = time.time()
        for begin in range(0, len(texts), 16):
            ids = order[begin: begin + 16]
            batch = tok([texts[i] for i in ids], padding=True, truncation=True, max_length=max_len - 1, return_tensors="pt")
            input_ids = torch.cat([batch["input_ids"][:, :1], torch.full((len(ids), 1), marker), batch["input_ids"][:, 1:]], 1)
            mask = torch.cat([batch["attention_mask"][:, :1], torch.ones(len(ids), 1, dtype=torch.long), batch["attention_mask"][:, 1:]], 1)
            with torch.inference_mode():
                vec = torch.nn.functional.normalize(linear(bert(input_ids=input_ids, attention_mask=mask).last_hidden_state), dim=-1)
            for row, i in enumerate(ids):
                keep = mask[row].bool()
                if drop_punct:
                    keep &= torch.tensor([t not in skip for t in input_ids[row].tolist()])
                out[i] = vec[row][keep].numpy()
            if (begin // 16) % 20 == 0:
                print(f"{label}: {begin + len(ids)}/{len(texts)} in {time.time() - started:.0f}s", file=sys.stderr, flush=True)
        return out

    lib, idx = Path(args.lib), Path(args.lib) / f"idx-{args.tag}"
    runs = lib / f"runs-{args.tag}"
    qrows = [json.loads(l) for l in open(lib / "queries.jsonl", encoding="utf-8")]
    chunks = [json.loads(l)["content"] for l in open(idx / "chunks.jsonl", encoding="utf-8")]
    qv = encode([q["text"] for q in qrows], q_marker, 64, False, "queries")
    dv = encode(chunks, d_marker, 512, True, "chunks")
    lengths = np.array([len(v) for v in dv])
    offsets = np.concatenate([[0], np.cumsum(lengths)])
    flat = np.concatenate(dv).astype(np.float32)

    lex = {r["qid"]: r for r in (json.loads(l) for l in open(runs / f"{args.lex}.jsonl"))}
    full, rerank, fused = [], [], []
    ms = []
    for qi, q in enumerate(qrows):
        t = time.time()
        sims = qv[qi] @ flat.T
        score = np.maximum.reduceat(sims, offsets[:-1], axis=1).sum(0) / len(qv[qi])
        ms.append((time.time() - t) * 1000)
        top = np.argsort(-score)[:30]
        full.append([(int(c), float(score[c])) for c in top])
        cand = [int(r) for r, _ in lex[q["qid"]]["hits"][: args.pool]]
        rerank.append(sorted(((c, float(score[c])) for c in cand), key=lambda x: -x[1])[:30])
        fuse = {}
        for rank, (c, _) in enumerate(full[-1]):
            fuse[c] = fuse.get(c, 0) + 1 / (60 + rank + 1)
        for rank, (c, _) in enumerate(lex[q["qid"]]["hits"][:30]):
            fuse[int(c)] = fuse.get(int(c), 0) + 1 / (60 + rank + 1)
        fused.append(sorted(fuse.items(), key=lambda x: -x[1])[:30])

    def write(name, ranked, times):
        with open(runs / f"{name}.jsonl", "w") as f:
            for i, q in enumerate(qrows):
                f.write(json.dumps({"qid": q["qid"], "ms": times[i], "leaks": 0, "hits": [[c, s] for c, s in ranked[i]]}) + "\n")

    write("colbert-small", full, ms)
    write("colbert-small-rr", rerank, [lex[q["qid"]]["ms"] for q in qrows])
    write("colbert-small+lex", fused, ms)
    print("wrote colbert-small, colbert-small-rr, colbert-small+lex")


if __name__ == "__main__":
    main()
