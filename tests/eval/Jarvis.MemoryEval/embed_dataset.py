"""Precomputes embeddings for the eval dataset with a local open model, so the C# eval can exercise the semantic path
without a paid provider. Usage: python3 embed_dataset.py <model> <out.json>   (needs sentence-transformers)."""
import json, os, sys
from sentence_transformers import SentenceTransformer

model_name, out = sys.argv[1], sys.argv[2]
# DATASET overrides the built-in set (same file you pass to the eval with --dataset).
data = json.load(open(os.environ.get("DATASET", __file__.rsplit("/", 1)[0] + "/dataset.json")))
model = SentenceTransformer(model_name)
# e5 models are trained with these role prefixes; other models ignore them.
q, p = ("query: ", "passage: ") if "e5" in model_name else ("", "")
passages = [m["content"] for m in data["memories"]] + data["otherOwnerMemories"]
queries = [x["text"] for key in ("queries", "heldOutQueries", "updateStatements") for x in data.get(key, [])]
if len(sys.argv) > 3:
    queries += sorted({q for v in json.load(open(sys.argv[3])).values() for q in v} - set(queries))
vectors = {}
for texts, prefix in ((passages, p), (queries, q)):
    embedded = model.encode([prefix + t for t in texts], normalize_embeddings=True, batch_size=32)
    vectors.update({t: [round(float(v), 5) for v in e] for t, e in zip(texts, embedded)})
json.dump({"model": model_name, "dimensions": len(next(iter(vectors.values()))), "vectors": vectors}, open(out, "w"))
print(model_name, len(vectors), "texts")
