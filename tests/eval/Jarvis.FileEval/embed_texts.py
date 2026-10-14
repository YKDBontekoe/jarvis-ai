"""Precomputes embeddings for the file eval with a local sentence-transformers model.

Usage:
  dotnet run --project tests/eval/Jarvis.FileEval -- after --dump-texts /tmp/texts.json
  python3 tests/eval/Jarvis.FileEval/embed_texts.py sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2 \
      /tmp/texts.json /tmp/file-emb.json
  dotnet run --project tests/eval/Jarvis.FileEval -- after --embeddings /tmp/file-emb.json
"""
import json
import sys

from sentence_transformers import SentenceTransformer

model_name, texts_path, out_path = sys.argv[1:4]
texts = json.load(open(texts_path, encoding="utf-8"))
model = SentenceTransformer(model_name)
vectors = model.encode(texts, normalize_embeddings=True, show_progress_bar=False)
json.dump({"model": "local:" + model_name, "vectors": {text: vector.tolist() for text, vector in zip(texts, vectors)}},
          open(out_path, "w", encoding="utf-8"))
print(f"Embedded {len(texts)} texts with {model_name}.")
