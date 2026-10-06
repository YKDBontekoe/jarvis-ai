#!/usr/bin/env python3
"""BGE-M3 in one forward pass: dense vectors, learned sparse weights and ColBERT-style multi-vectors.

Writes next to the other embeddings of a configuration:
  idx-<tag>/emb-bge-m3.{f32,json}        dense, for `embed-load` (works with the normal pgvector path)
  idx-<tag>/m3-sparse.json               per chunk {token_id: weight}
  idx-<tag>/m3-colbert.f16 + .offsets.npy  per-token vectors (1024d, float16, concatenated; offsets in tokens)
  qemb-bge-m3.*, q-m3-sparse.json, q-m3-colbert.*   the same for the queries (library level)
score_m3.py turns these into run files (sparse, multi-vector, and their fusion).

Usage: embed_m3.py LIB TAG [--max-length 512] [--threads 4] [--limit N]
"""
import argparse
import json
import sys
import time
from pathlib import Path

import numpy as np


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("lib")
    ap.add_argument("tag")
    ap.add_argument("--max-length", type=int, default=512)
    ap.add_argument("--threads", type=int, default=4)
    ap.add_argument("--limit", type=int)
    args = ap.parse_args()

    import torch
    from huggingface_hub import hf_hub_download
    from transformers import AutoModel, AutoTokenizer

    torch.set_num_threads(args.threads)
    tok = AutoTokenizer.from_pretrained("BAAI/bge-m3")
    model = AutoModel.from_pretrained("BAAI/bge-m3").eval()
    colbert = torch.nn.Linear(1024, 1024)
    colbert.load_state_dict(torch.load(hf_hub_download("BAAI/bge-m3", "colbert_linear.pt"), map_location="cpu"))
    sparse = torch.nn.Linear(1024, 1)
    sparse.load_state_dict(torch.load(hf_hub_download("BAAI/bge-m3", "sparse_linear.pt"), map_location="cpu"))
    special = {tok.cls_token_id, tok.eos_token_id, tok.pad_token_id, tok.unk_token_id}

    def encode(texts, label):
        order = np.argsort([-len(t) for t in texts])  # long first keeps padding small
        dense = np.zeros((len(texts), 1024), np.float32)
        sparse_out = [None] * len(texts)
        colbert_out = [None] * len(texts)
        started = time.time()
        for begin in range(0, len(texts), 8):
            ids = order[begin: begin + 8]
            batch = tok([texts[i] for i in ids], padding=True, truncation=True, max_length=args.max_length, return_tensors="pt")
            with torch.inference_mode():
                hidden = model(**batch).last_hidden_state
                d = torch.nn.functional.normalize(hidden[:, 0], dim=-1)
                w = torch.relu(sparse(hidden)).squeeze(-1)
                c = torch.nn.functional.normalize(colbert(hidden[:, 1:]), dim=-1)
            for row, i in enumerate(ids):
                length = int(batch["attention_mask"][row].sum())
                dense[i] = d[row].numpy()
                weights = {}
                for token, weight in zip(batch["input_ids"][row][:length].tolist(), w[row][:length].tolist()):
                    if token not in special and weight > weights.get(token, 0):
                        weights[token] = weight
                sparse_out[i] = weights
                colbert_out[i] = c[row][: length - 1].numpy().astype(np.float16)  # drop the cls position, keep eos like FlagEmbedding
            if (begin // 8) % 25 == 0:
                print(f"{label}: {begin + len(ids)}/{len(texts)} {(begin + len(ids)) / (time.time() - started):.1f}/s", file=sys.stderr, flush=True)
        return dense, sparse_out, colbert_out

    def save(prefix, dense, sparse_out, colbert_out, meta_dir, dense_name):
        (meta_dir / f"{dense_name}.f32").write_bytes(dense.tobytes())
        (meta_dir / f"{dense_name}.json").write_text(json.dumps({"model": "BAAI/bge-m3", "dim": 1024, "n": len(dense)}))
        (meta_dir / f"{prefix}-sparse.json").write_text(json.dumps([{str(k): v for k, v in d.items()} for d in sparse_out]))
        lengths = np.array([len(v) for v in colbert_out])
        np.save(meta_dir / f"{prefix}-colbert.offsets.npy", np.concatenate([[0], np.cumsum(lengths)]))
        (meta_dir / f"{prefix}-colbert.f16").write_bytes(np.concatenate(colbert_out).tobytes())

    lib = Path(args.lib)
    idx = lib / f"idx-{args.tag}"
    if not (lib / "q-m3-sparse.json").exists() and args.limit is None:
        queries = [json.loads(l)["text"] for l in open(lib / "queries.jsonl", encoding="utf-8")]
        save("q-m3", *encode(queries, "queries"), lib, "qemb-bge-m3")
    texts = [json.loads(l)["content"] for l in open(idx / "chunks.jsonl", encoding="utf-8")]
    if args.limit:
        texts = texts[: args.limit]
    t0 = time.time()
    result = encode(texts, "chunks")
    if args.limit:
        print(f"throughput {len(texts) / (time.time() - t0):.2f} chunks/s")
        return
    save("m3", *result, idx, "emb-bge-m3")
    print(f"done: {len(texts)} chunks in {(time.time() - t0) / 60:.1f} min", file=sys.stderr)


if __name__ == "__main__":
    main()
