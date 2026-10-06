#!/usr/bin/env python3
"""Embeds every chunk of an indexed configuration and every query with a local open model.

Writes <lib>/idx-<tag>/emb-<name>.{f32,json} (rows in chunks.jsonl order) and <lib>/qemb-<name>.{f32,json}; `embed-load`
puts the chunk vectors into the database. Vectors are L2-normalised float32. Work is checkpointed in shards, so an
interrupted run resumes. Texts longer than the model's window are truncated by the model, exactly as a provider would.

Usage: embed.py LIB TAG MODEL_ALIAS [--limit N] [--threads 4]
Aliases: qwen3-0.6b (Qwen3-Embedding-0.6B, 1024d), gemma-300m (EmbeddingGemma, 768d), minilm  (all-MiniLM-L6-v2, 384d, 256 tokens), bge-small (bge-small-en-v1.5, 384d, 512 tokens),
         e5-small (multilingual-e5-small, 384d, 512 tokens), mminilm (paraphrase-multilingual-MiniLM-L12-v2, 128 tokens)
"""
import argparse
import json
import sys
import time
from pathlib import Path

import numpy as np

# alias -> (hub name, query prefix, passage prefix, max tokens, sentence-transformers prompt names (query, document))
MODELS = {
    "minilm": ("sentence-transformers/all-MiniLM-L6-v2", "", "", 256, (None, None)),
    "bge-small": ("BAAI/bge-small-en-v1.5", "Represent this sentence for searching relevant passages: ", "", 512, (None, None)),
    "e5-small": ("intfloat/multilingual-e5-small", "query: ", "passage: ", 512, (None, None)),
    "mminilm": ("sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2", "", "", 128, (None, None)),
    "qwen3-0.6b": ("Qwen/Qwen3-Embedding-0.6B", "", "", 512, ("query", None)),
    "gemma-300m": ("unsloth/embeddinggemma-300m", "", "", 512, ("query", "document")),
}
SHARD = 2048


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("lib")
    ap.add_argument("tag")
    ap.add_argument("alias", choices=MODELS)
    ap.add_argument("--limit", type=int)
    ap.add_argument("--threads", type=int, default=4)
    args = ap.parse_args()

    import torch
    from sentence_transformers import SentenceTransformer

    torch.set_num_threads(args.threads)
    name, query_prefix, passage_prefix, max_len, (query_prompt, doc_prompt) = MODELS[args.alias]
    model = SentenceTransformer(name, device="cpu")
    model.max_seq_length = max_len
    lib = Path(args.lib)
    idx = lib / f"idx-{args.tag}"

    queries = [json.loads(l)["text"] for l in open(lib / "queries.jsonl", encoding="utf-8")]
    if not (lib / f"qemb-{args.alias}.f32").exists() and args.limit is None:
        q = model.encode([query_prefix + t for t in queries], batch_size=64, normalize_embeddings=True, convert_to_numpy=True,
                         prompt_name=query_prompt).astype(np.float32)
        (lib / f"qemb-{args.alias}.f32").write_bytes(q.tobytes())
        (lib / f"qemb-{args.alias}.json").write_text(json.dumps({"model": name, "dim": int(q.shape[1]), "n": len(queries)}))
        print(f"embedded {len(queries)} queries", file=sys.stderr)

    texts = [json.loads(l)["content"] for l in open(idx / "chunks.jsonl", encoding="utf-8")]
    if args.limit:
        texts = texts[: args.limit]
    parts = idx / f"emb-{args.alias}.parts"
    parts.mkdir(exist_ok=True)
    started = time.time()
    done_tokens = 0
    for shard, begin in enumerate(range(0, len(texts), SHARD)):
        path = parts / f"{shard:05d}.npy"
        if path.exists() and args.limit is None:
            continue
        batch = [passage_prefix + t for t in texts[begin: begin + SHARD]]
        vectors = model.encode(batch, batch_size=16, normalize_embeddings=True, convert_to_numpy=True,
                               prompt_name=doc_prompt).astype(np.float32)
        if args.limit is None:
            np.save(path, vectors)
        done_tokens += len(batch)
        rate = done_tokens / (time.time() - started)
        print(f"shard {shard}: {begin + len(batch)}/{len(texts)} chunks, {rate:.1f} chunks/s", file=sys.stderr, flush=True)
    if args.limit is not None:
        print(f"throughput {done_tokens / (time.time() - started):.1f} chunks/s (not saved)")
        return
    matrix = np.concatenate([np.load(p) for p in sorted(parts.glob("*.npy"))])
    assert len(matrix) == len(texts), (len(matrix), len(texts))
    (idx / f"emb-{args.alias}.f32").write_bytes(matrix.tobytes())
    (idx / f"emb-{args.alias}.json").write_text(json.dumps({"model": name, "dim": int(matrix.shape[1]), "n": len(texts)}))
    print(f"done: {len(texts)} chunks in {(time.time() - started) / 60:.1f} min", file=sys.stderr)


if __name__ == "__main__":
    main()
