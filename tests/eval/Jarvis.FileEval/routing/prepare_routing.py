#!/usr/bin/env python3
"""Data for the query-routing benchmark.

Three routes, labelled by how many retrieval hops a question needs (the labelling Adaptive-RAG uses, with hop count instead of
dataset origin for the multi-hop classes):
  direct     1 hop    natural single-hop questions (SQuAD)
  decompose  2 hops   MuSiQue 2-hop questions
  agentic    3-4 hops MuSiQue 3- and 4-hop questions

Writes OUT/passages.jsonl (the pooled corpus: every paragraph of the test questions plus distractors from other questions),
OUT/test.jsonl (the questions that are executed through every route), OUT/router_train.jsonl, OUT/router_dev.jsonl (labelled
questions for the classifiers; none of them are test questions) and OUT/gold.jsonl (answers and supporting passages, kept
apart from everything the executing agents may read).

Usage: prepare_routing.py RAW_DIR OUT_DIR [--seed 0]
"""
import argparse
import hashlib
import json
import random
import re
from pathlib import Path

import pandas as pd

ROUTES = {1: "direct", 2: "decompose", 3: "agentic", 4: "agentic"}


def pid(title, text):
    return "p" + hashlib.md5((title + "\n" + text).encode()).hexdigest()[:12]


def usable_squad(question: str) -> bool:
    """Self-contained enough to answer without its passage: long enough and names something."""
    words = question.split()
    if len(words) < 6:
        return False
    named = any(w[0].isupper() for w in words[1:]) or any(c.isdigit() for c in question)
    return named and not re.search(r"\b(this|these|the passage|the text|the article|the author)\b", question, re.I)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("raw")
    ap.add_argument("out")
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--per-class", type=int, default=30)
    ap.add_argument("--extra-musique", type=int, default=250)
    args = ap.parse_args()
    rng = random.Random(args.seed)
    raw, out = Path(args.raw), Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    dev = [json.loads(l) for l in open(raw / "musique_dev.jsonl")]
    by_hops = {2: [], 3: [], 4: []}
    for r in dev:
        by_hops[len(r["question_decomposition"])].append(r)
    for v in by_hops.values():
        rng.shuffle(v)
    n = args.per_class
    test_mq = by_hops[2][:n] + by_hops[3][: n // 2] + by_hops[4][: n - n // 2]
    used = {r["id"] for r in test_mq}
    others = [r for r in dev if r["id"] not in used]
    rng.shuffle(others)
    distractor_mq = others[: args.extra_musique]

    sq_dev = pd.read_parquet(raw / "squad_validation.parquet")
    sq_dev = sq_dev[sq_dev.question.map(usable_squad)].sample(frac=1.0, random_state=args.seed).reset_index(drop=True)
    test_sq = sq_dev.iloc[:n]

    passages, seen = [], set()

    def add(title, text):
        p = pid(title, text)
        if p not in seen:
            seen.add(p)
            passages.append({"pid": p, "title": title, "text": text})
        return p

    test, gold = [], []
    for r in test_mq:
        hops = len(r["question_decomposition"])
        support = [add(x["title"], x["paragraph_text"]) for x in r["paragraphs"] if x["is_supporting"]]
        for x in r["paragraphs"]:
            add(x["title"], x["paragraph_text"])
        test.append({"qid": "mq-" + r["id"], "question": r["question"], "route": ROUTES[hops], "hops": hops})
        gold.append({"qid": "mq-" + r["id"], "answers": [r["answer"]] + r["answer_aliases"], "support": support})
    for r in distractor_mq:
        for x in r["paragraphs"]:
            add(x["title"], x["paragraph_text"])
    for ctx_title, ctx in sq_dev[["title", "context"]].drop_duplicates("context").itertuples(index=False):
        add(ctx_title.replace("_", " "), ctx)
    for r in test_sq.itertuples(index=False):
        test.append({"qid": "sq-" + r.id, "question": r.question, "route": "direct", "hops": 1})
        gold.append({"qid": "sq-" + r.id, "answers": sorted(set(r.answers["text"])),
                     "support": [pid(r.title.replace("_", " "), r.context)]})
    rng.shuffle(test)
    test_ids = {t["qid"] for t in test}

    # router data: MuSiQue train (2 / 3-4 hops) and SQuAD train (1 hop); dev questions outside the test set for evaluation
    tr = [json.loads(l) for l in open(raw / "musique_train.jsonl")]
    sq_tr = pd.read_parquet(raw / "squad_train.parquet")
    sq_tr = sq_tr[sq_tr.question.map(usable_squad)]
    train, devset = [], []

    def pick(rows, label, k, sink):
        rows = list(rows)
        rng.shuffle(rows)
        sink.extend({"text": q, "label": label} for q in rows[:k])

    pick([r["question"] for r in tr if len(r["question_decomposition"]) == 2], "decompose", 4000, train)
    pick([r["question"] for r in tr if len(r["question_decomposition"]) >= 3], "agentic", 4000, train)
    pick(sq_tr.question.tolist(), "direct", 4000, train)
    rng.shuffle(train)
    pick([r["question"] for r in dev if len(r["question_decomposition"]) == 2 and "mq-" + r["id"] not in test_ids], "decompose", 500, devset)
    pick([r["question"] for r in dev if len(r["question_decomposition"]) >= 3 and "mq-" + r["id"] not in test_ids], "agentic", 500, devset)
    pick([q for q, i in zip(sq_dev.question, sq_dev.id) if "sq-" + i not in test_ids], "direct", 500, devset)

    def dump(name, rows):
        with open(out / name, "w", encoding="utf-8") as f:
            for r in rows:
                f.write(json.dumps(r, ensure_ascii=False) + "\n")

    dump("passages.jsonl", passages)
    dump("test.jsonl", test)
    dump("gold.jsonl", gold)
    dump("router_train.jsonl", train)
    dump("router_dev.jsonl", devset)
    from collections import Counter
    print(f"{len(passages)} passages; test {Counter(t['route'] for t in test)}; train {Counter(r['label'] for r in train)}; "
          f"dev {Counter(r['label'] for r in devset)}")


if __name__ == "__main__":
    main()
