#!/usr/bin/env python3
"""Trains small query routers (direct / decompose / agentic) and writes their predictions.

Routers: length-features tree, TF-IDF + logistic regression, frozen bge-small embeddings + logistic regression, and a
fine-tuned all-MiniLM-L6-v2 (22M parameters; Adaptive-RAG found T5-Small to Large about equal). Each is scored on held-out
dev questions, then predicts the executed test set, hand-written assistant-style queries (out of domain), and the
BEIR/FiQA-style queries of the file eval (single-hop retrieval questions).

Usage: train_routers.py ROUTE_DIR OUT_JSON [--beir-queries FILE] [--threads 3] [--epochs 3]
"""
import argparse
import json
import re
import time
from pathlib import Path

import numpy as np

LABELS = ["direct", "decompose", "agentic"]


def load(path):
    return [json.loads(l) for l in open(path, encoding="utf-8")]


def features(texts):
    rows = []
    for t in texts:
        w = t.split()
        rows.append([len(w), len(t), t.count(","), len(re.findall(r"\b(and|or)\b", t.lower())), len(re.findall(r"\b(of|the)\b", t.lower())),
                     sum(1 for x in w[1:] if x[:1].isupper()), len(re.findall(r"\b(that|who|which|whose|where)\b", t.lower())),
                     len(re.findall(r"\b(compare|difference|between|both|all|every|summar|why|how)\b", t.lower()))])
    return np.array(rows, dtype=np.float32)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("route_dir")
    ap.add_argument("out")
    ap.add_argument("--beir-queries")
    ap.add_argument("--threads", type=int, default=3)
    ap.add_argument("--epochs", type=int, default=3)
    args = ap.parse_args()
    d = Path(args.route_dir)
    train, dev, test = load(d / "router_train.jsonl"), load(d / "router_dev.jsonl"), load(d / "test.jsonl")
    ood = load(Path(__file__).parent / "ood_assistant.jsonl")
    beir = [json.loads(l)["text"] for l in open(args.beir_queries)] if args.beir_queries else []
    xt, yt = [r["text"] for r in train], np.array([LABELS.index(r["label"]) for r in train])
    xd, yd = [r["text"] for r in dev], np.array([LABELS.index(r["label"]) for r in dev])
    xs, ys = [r["question"] for r in test], np.array([LABELS.index(r["route"]) for r in test])
    xo, yo = [r["text"] for r in ood], np.array([LABELS.index(r["label"]) for r in ood])
    sets = {"dev": (xd, yd), "test": (xs, ys), "assistant": (xo, yo)}

    from sklearn.feature_extraction.text import TfidfVectorizer
    from sklearn.linear_model import LogisticRegression
    from sklearn.metrics import f1_score
    from sklearn.pipeline import make_pipeline
    from sklearn.tree import DecisionTreeClassifier
    import torch
    torch.set_num_threads(args.threads)

    predictors = {}
    tree = DecisionTreeClassifier(max_depth=5, random_state=0).fit(features(xt), yt)
    predictors["length-tree"] = lambda x: tree.predict(features(x))
    tfidf = make_pipeline(TfidfVectorizer(ngram_range=(1, 2), min_df=2, sublinear_tf=True), LogisticRegression(max_iter=2000, C=5))
    tfidf.fit(xt, yt)
    predictors["tfidf-lr"] = tfidf.predict

    from sentence_transformers import SentenceTransformer
    bge = SentenceTransformer("BAAI/bge-small-en-v1.5", device="cpu")
    embed = lambda x: bge.encode(list(x), batch_size=64, normalize_embeddings=True, show_progress_bar=False)
    lr = LogisticRegression(max_iter=3000, C=10).fit(embed(xt), yt)
    predictors["bge-small+lr"] = lambda x: lr.predict(embed(x))

    # fine-tune MiniLM-L6 as a sequence classifier
    from transformers import AutoModelForSequenceClassification, AutoTokenizer
    name = "sentence-transformers/all-MiniLM-L6-v2"
    tok = AutoTokenizer.from_pretrained(name)
    model = AutoModelForSequenceClassification.from_pretrained(name, num_labels=3)
    opt = torch.optim.AdamW(model.parameters(), lr=5e-5, weight_decay=0.01)
    steps = args.epochs * ((len(xt) + 31) // 32)
    sched = torch.optim.lr_scheduler.LambdaLR(opt, lambda s: min(1.0, (s + 1) / 50) * max(0.0, 1 - s / steps))
    rng = np.random.default_rng(0)
    started, step = time.time(), 0
    model.train()
    for epoch in range(args.epochs):
        order = rng.permutation(len(xt))
        for b in range(0, len(order), 32):
            idx = order[b: b + 32]
            batch = tok([xt[i] for i in idx], padding=True, truncation=True, max_length=64, return_tensors="pt")
            loss = model(**batch, labels=torch.tensor(yt[idx])).loss
            loss.backward()
            torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            opt.step(); sched.step(); opt.zero_grad(); step += 1
            if step % 100 == 0:
                print(f"minilm-ft epoch {epoch} step {step}/{steps} loss {loss.item():.3f} {time.time() - started:.0f}s", flush=True)
    model.eval()

    def predict_ft(x):
        out = []
        with torch.inference_mode():
            for b in range(0, len(x), 64):
                batch = tok(list(x[b: b + 64]), padding=True, truncation=True, max_length=64, return_tensors="pt")
                out.extend(model(**batch).logits.argmax(-1).tolist())
        return np.array(out)

    predictors["minilm-ft"] = predict_ft

    result = {"labels": LABELS, "metrics": {}, "predictions": {}}
    for name, fn in predictors.items():
        result["metrics"][name] = {}
        for sname, (x, y) in sets.items():
            p = np.asarray(fn(x))
            result["metrics"][name][sname] = {
                "accuracy": float((p == y).mean()), "macro_f1": float(f1_score(y, p, average="macro")),
                "confusion": [[int(((y == a) & (p == b)).sum()) for b in range(3)] for a in range(3)]}
        t = time.time()
        fn(xs[:30])
        result["metrics"][name]["ms_per_query"] = (time.time() - t) / 30 * 1000
        result["predictions"][name] = {
            "test": {r["qid"]: LABELS[int(p)] for r, p in zip(test, fn(xs))},
            "assistant": [LABELS[int(p)] for p in fn(xo)],
            "beir": [LABELS[int(p)] for p in fn(beir)] if beir else []}
        print(name, {k: (round(v["accuracy"], 3), round(v["macro_f1"], 3)) for k, v in result["metrics"][name].items() if isinstance(v, dict)}, flush=True)
    Path(args.out).write_text(json.dumps(result, indent=1))


if __name__ == "__main__":
    main()
