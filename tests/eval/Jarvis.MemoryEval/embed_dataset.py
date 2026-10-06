"""Precomputes embeddings for the eval dataset with a local open model, so the C# eval can exercise the semantic path
without a paid provider. Usage: python3 embed_dataset.py <model> <out.json> [agent-queries.json]  (needs sentence-transformers).

Environment:
  DATASET        eval dataset (same file you pass to the eval with --dataset), default dataset.json
  EMBED_DIM      truncate to this many leading dimensions and re-normalise (Matryoshka models such as EmbeddingGemma)
  EMBED_PROMPTS  "auto" (default): the model's own query/document prompts; "none": raw text, to measure what they are worth
  EMBED_QUANT    "int8": dynamic int8 quantisation of the Linear layers (CPU)
"""
import json, os, sys
from sentence_transformers import SentenceTransformer

model_name, out = sys.argv[1], sys.argv[2]
# DATASET overrides the built-in set (same file you pass to the eval with --dataset).
data = json.load(open(os.environ.get("DATASET", __file__.rsplit("/", 1)[0] + "/dataset.json")))
dim = int(os.environ.get("EMBED_DIM", "0")) or None
gemma = "embeddinggemma" in model_name.lower()
kwargs = {}
if gemma:
    # Text-only: skip the vision and audio encoders (270M instead of 740M parameters). float16 gives NaNs, so float32.
    import torch
    kwargs = {"config_kwargs": {"vision_config": None, "audio_config": None}, "model_kwargs": {"torch_dtype": torch.float32}} \
        if "embeddinggemma-2" in model_name.lower() else {}
model = SentenceTransformer(model_name, **kwargs)
if os.environ.get("EMBED_QUANT") == "int8":
    import torch
    model = torch.quantization.quantize_dynamic(model, {torch.nn.Linear}, dtype=torch.qint8)
passages = [m["content"] for m in data["memories"]] + data["otherOwnerMemories"]
queries = [x["text"] for key in ("queries", "heldOutQueries", "updateStatements") for x in data.get(key, [])]
if len(sys.argv) > 3:
    queries += sorted({q for v in json.load(open(sys.argv[3])).values() for q in v} - set(queries))

# e5 models are trained with these role prefixes; EmbeddingGemma ships its own prompts; other models ignore them.
use_prompts = os.environ.get("EMBED_PROMPTS", "auto") != "none"
q, p = ("query: ", "passage: ") if "e5" in model_name and use_prompts else ("", "")
vectors = {}
for texts, prefix, prompt_name in ((passages, p, "document"), (queries, q, "query")):
    encode = dict(normalize_embeddings=True, batch_size=32)
    if gemma and use_prompts:
        encode["prompt_name"] = prompt_name
    if dim:
        encode["truncate_dim"] = dim
    embedded = model.encode([prefix + t for t in texts], **encode)
    vectors.update({t: [round(float(v), 5) for v in e] for t, e in zip(texts, embedded)})
json.dump({"model": model_name, "dimensions": len(next(iter(vectors.values()))), "vectors": vectors}, open(out, "w"))
print(model_name, len(vectors), "texts", f"dim={len(next(iter(vectors.values())))}")
