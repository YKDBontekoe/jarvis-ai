"""Embedding-only retrieval metrics: ranks the (non-expired) memories by cosine similarity to each query, no keyword
path, no fusion. Isolates what the embedding model itself contributes. Same labels and metrics as the C# eval.

    python3 dense_metrics.py dataset-large.json emb.json [queries|heldOutQueries|updateStatements]
"""
import json, math, sys
import numpy as np

data = json.load(open(sys.argv[1]))
vectors = json.load(open(sys.argv[2]))["vectors"]
queries = data[sys.argv[3] if len(sys.argv) > 3 else "queries"]
live = [m for m in data["memories"] if not m.get("expired")]
matrix = np.array([vectors[m["content"]] for m in live], dtype=np.float32)
keys = [m["key"] for m in live]

recall3, recall8, hit1, mrr, ndcg, noise = [], [], [], [], [], []
for q in queries:
    scores = matrix @ np.array(vectors[q["text"]], dtype=np.float32)
    ranked = [keys[i] for i in np.argsort(-scores)[:8]]
    rel = {k: g for k, g in q["relevant"].items() if k in keys}
    if not rel:
        continue
    recall3.append(sum(k in rel for k in ranked[:3]) / len(rel))
    recall8.append(sum(k in rel for k in ranked) / len(rel))
    primary = [k for k, g in rel.items() if g == 2]
    if primary:
        hit1.append(float(ranked[0] in primary))
        rank = next((i + 1 for i, k in enumerate(ranked) if k in primary), None)
        mrr.append(1 / rank if rank else 0.0)
    dcg = sum(rel.get(k, 0) / math.log2(i + 2) for i, k in enumerate(ranked))
    ideal = sum(g / math.log2(i + 2) for i, g in enumerate(sorted(rel.values(), reverse=True)[:8]))
    ndcg.append(dcg / ideal if ideal else 0.0)
    noise.append(sum(k not in rel for k in ranked) / len(ranked))
mean = lambda v: round(float(np.mean(v)), 3)
print(json.dumps({"n": len(recall8), "recall@3": mean(recall3), "recall@8": mean(recall8), "hit@1": mean(hit1),
                  "mrr": mean(mrr), "ndcg@8": mean(ndcg), "noise@8": mean(noise)}))
