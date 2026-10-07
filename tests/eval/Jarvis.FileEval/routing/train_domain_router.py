#!/usr/bin/env python3
"""Trains routers on assistant-style queries (written by models, ~30% Dutch) and tests them where it matters.

Training data: gen_*.jsonl (one file per life area). One area is held out entirely ("unseen area"), so the score shows
how a router copes with a topic it has not seen. Hand-written test sets (never shown to the data writers): 36 English and
18 Dutch assistant requests. Optionally mixes in Wikipedia-style multi-hop questions (the earlier training set) to see
whether that helps or hurts.

Routers: TF-IDF (word + character n-grams) + logistic regression; frozen multilingual-e5-small embeddings + logistic
regression; fine-tuned paraphrase-multilingual-MiniLM-L12-v2.

Usage: train_domain_router.py GEN_DIR PRIV_DIR OUT_JSON [--holdout health_travel] [--threads 3] [--epochs 4]
"""
import argparse
import json
import re
import time
from pathlib import Path

import numpy as np

LABELS = ["direct", "decompose", "agentic"]


def load(path):
    return [json.loads(l) for l in open(path, encoding="utf-8") if l.strip()]


def norm(t):
    return re.sub(r"\W+", " ", t.lower()).strip()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("gen_dir")
    ap.add_argument("priv_dir")
    ap.add_argument("out")
    ap.add_argument("--holdout", default="health_travel")
    ap.add_argument("--threads", type=int, default=3)
    ap.add_argument("--epochs", type=int, default=4)
    ap.add_argument("--wiki", type=int, default=2000, help="Wikipedia-style questions per class in the mixed variant")
    ap.add_argument("--beir-queries")
    args = ap.parse_args()
    here = Path(__file__).parent
    gen = {f.stem[4:]: load(f) for f in sorted(Path(args.gen_dir).glob("gen_*.jsonl"))}
    held_out_text = {norm(r["text"]) for f in ("ood_assistant.jsonl", "ood_assistant_nl.jsonl") for r in load(here / f)}
    seen, clean = set(held_out_text), {}  # nothing that appears in the hand-written test sets may be trained on
    for area, rows in gen.items():
        keep = []
        for r in rows:
            if r.get("label") in LABELS and norm(r["text"]) not in seen:
                seen.add(norm(r["text"]))
                keep.append(r)
        clean[area] = keep
    print({a: len(r) for a, r in clean.items()}, flush=True)
    train = [r for a, rows in clean.items() if a != args.holdout for r in rows]
    unseen = clean.get(args.holdout, [])
    test_en, test_nl = load(here / "ood_assistant.jsonl"), load(here / "ood_assistant_nl.jsonl")
    wiki = load(Path(args.priv_dir) / "router_train.jsonl")
    wiki_dev = load(Path(args.priv_dir) / "router_dev.jsonl")
    rng = np.random.default_rng(0)
    wiki_sample = []
    for label in LABELS:
        rows = [r for r in wiki if r["label"] == label]
        wiki_sample += [rows[i] for i in rng.permutation(len(rows))[: args.wiki]]
    beir = [json.loads(l)["text"] for l in open(args.beir_queries)] if args.beir_queries else []

    def xy(rows, key="text"):
        return [r[key] for r in rows], np.array([LABELS.index(r["label"]) for r in rows])

    sets = {"unseen_area": xy(unseen), "assistant_en": xy(test_en), "assistant_nl": xy(test_nl), "wiki_dev": xy(wiki_dev)}
    variants = {"assistant-only": train, "assistant+wiki": train + wiki_sample}

    from sklearn.feature_extraction.text import TfidfVectorizer
    from sklearn.linear_model import LogisticRegression
    from sklearn.metrics import f1_score
    from sklearn.pipeline import make_pipeline, make_union
    import torch
    torch.set_num_threads(args.threads)
    from sentence_transformers import SentenceTransformer
    from transformers import AutoModelForSequenceClassification, AutoTokenizer

    e5 = SentenceTransformer("intfloat/multilingual-e5-small", device="cpu")
    embed = lambda x: e5.encode(["query: " + t for t in x], batch_size=64, normalize_embeddings=True, show_progress_bar=False)

    result = {"labels": LABELS, "sizes": {"train_assistant": len(train), "unseen_area": len(unseen), "wiki_mix": len(wiki_sample)},
              "metrics": {}, "predictions": {}}

    def record(name, predict, ms):
        result["metrics"][name] = {"ms_per_query": ms}
        for sname, (x, y) in sets.items():
            p = np.asarray(predict(x))
            result["metrics"][name][sname] = {"accuracy": float((p == y).mean()), "macro_f1": float(f1_score(y, p, average="macro")),
                                              "confusion": [[int(((y == a) & (p == b)).sum()) for b in range(3)] for a in range(3)]}
        result["predictions"][name] = {"beir": [LABELS[int(i)] for i in predict(beir)] if beir else []}
        m = result["metrics"][name]
        print(name, {k: round(m[k]["accuracy"], 3) for k in sets}, f"{ms:.1f} ms", flush=True)

    for vname, rows in variants.items():
        x, y = xy(rows)
        tfidf = make_pipeline(make_union(TfidfVectorizer(ngram_range=(1, 2), sublinear_tf=True, min_df=2),
                                         TfidfVectorizer(analyzer="char_wb", ngram_range=(2, 5), sublinear_tf=True, min_df=3)),
                              LogisticRegression(max_iter=3000, C=10)).fit(x, y)
        t = time.time(); tfidf.predict(sets["assistant_en"][0]); ms = (time.time() - t) / len(sets["assistant_en"][0]) * 1000
        record(f"tfidf-lr [{vname}]", tfidf.predict, ms)
        lr = LogisticRegression(max_iter=3000, C=20).fit(embed(x), y)
        sample = sets["assistant_en"][0]
        t = time.time(); lr.predict(embed(sample)); ms = (time.time() - t) / len(sample) * 1000
        record(f"e5-small+lr [{vname}]", lambda q: lr.predict(embed(q)), ms)

        name = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
        tok = AutoTokenizer.from_pretrained(name)
        model = AutoModelForSequenceClassification.from_pretrained(name, num_labels=3)
        opt = torch.optim.AdamW(model.parameters(), lr=4e-5, weight_decay=0.01)
        steps = args.epochs * ((len(x) + 31) // 32)
        sched = torch.optim.lr_scheduler.LambdaLR(opt, lambda s: min(1.0, (s + 1) / 30) * max(0.0, 1 - s / steps))
        step, started = 0, time.time()
        model.train()
        for epoch in range(args.epochs):
            order = rng.permutation(len(x))
            for b in range(0, len(order), 32):
                idx = order[b: b + 32]
                batch = tok([x[i] for i in idx], padding=True, truncation=True, max_length=64, return_tensors="pt")
                loss = model(**batch, labels=torch.tensor(y[idx])).loss
                loss.backward()
                torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
                opt.step(); sched.step(); opt.zero_grad(); step += 1
                if step % 50 == 0:
                    print(f"  ft[{vname}] step {step}/{steps} loss {loss.item():.3f} {time.time() - started:.0f}s", flush=True)
        model.eval()

        def predict(q, model=model, tok=tok):
            out = []
            with torch.inference_mode():
                for b in range(0, len(q), 64):
                    batch = tok(list(q[b: b + 64]), padding=True, truncation=True, max_length=64, return_tensors="pt")
                    out.extend(model(**batch).logits.argmax(-1).tolist())
            return np.array(out)

        t = time.time(); predict(sample); ms = (time.time() - t) / len(sample) * 1000
        record(f"minilm-multilingual-ft [{vname}]", predict, ms)
        Path(args.out).write_text(json.dumps(result, indent=1))
    Path(args.out).write_text(json.dumps(result, indent=1))


if __name__ == "__main__":
    main()
