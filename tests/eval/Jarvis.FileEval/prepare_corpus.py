#!/usr/bin/env python3
"""Builds a large "personal library" for the file RAG eval from public BEIR retrieval sets.

BEIR corpora are short passages (abstracts, forum answers), but Jarvis indexes *files*, so passages are grouped into long
topical markdown documents. Grouping is TF-IDF + k-means, so a document is about one subject (like a real note or paper)
and the other passages of the same document are realistic hard distractors. Passage character offsets inside each document
are recorded so that retrieved chunks can be scored against the BEIR relevance judgments.

Sources (English: BeIR/*, Dutch: clips/beir-nl-*, machine translated):
  scifact   5,183 abstracts, 300 claim queries        (scientific, vocabulary gap between claim and abstract)
  nfcorpus  3,633 abstracts, 323 queries              (nutrition/medical, short topical queries, many relevant docs)
  fiqa     57,638 answers,   648 queries              (personal finance questions, the closest to assistant use)

Usage:
  python3 prepare_corpus.py RAW_DIR OUT_DIR LANG [--fiqa-distractors N] [--query-limit SRC=N,..] [--keep SRC=N,..] [--extra trec-covid] [--seed 0]
  RAW_DIR contains <lang>/<source>/{corpus.parquet|corpus.jsonl, queries.*, test.tsv} as fetched by README's commands.
Writes OUT_DIR/docs.jsonl and OUT_DIR/queries.jsonl.
"""
import argparse
import json
import random
import re
import sys
from collections import defaultdict
from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.cluster import MiniBatchKMeans
from sklearn.decomposition import TruncatedSVD
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.preprocessing import normalize

SOURCES = ["scifact", "nfcorpus", "fiqa"]
PASSAGES_PER_DOC = 40
MAX_PASSAGES_PER_DOC = 100


def load_source(raw: Path, lang: str, source: str, distractors_only: bool = False):
    base = raw / lang / source
    if distractors_only:
        corpus = pd.read_parquet(base / "corpus.parquet")
        corpus["_id"] = corpus["_id"].astype(str)
        corpus["title"] = corpus["title"].fillna("").astype(str)
        corpus["text"] = corpus["text"].fillna("").astype(str)
        return corpus, pd.DataFrame({"_id": [], "text": []}), pd.DataFrame({"query-id": [], "corpus-id": [], "score": []})
    if (base / "corpus.parquet").exists():
        corpus = pd.read_parquet(base / "corpus.parquet")
        queries = pd.read_parquet(base / "queries.parquet")
    else:
        corpus = pd.read_json(base / "corpus.jsonl", lines=True, dtype={"_id": str})
        queries = pd.read_json(base / "queries.jsonl", lines=True, dtype={"_id": str})
    corpus["_id"] = corpus["_id"].astype(str)
    queries["_id"] = queries["_id"].astype(str)
    corpus["title"] = corpus["title"].fillna("").astype(str)
    corpus["text"] = corpus["text"].fillna("").astype(str)
    qrels = pd.read_csv(base / "test.tsv", sep="\t", dtype={"query-id": str, "corpus-id": str})
    qrels = qrels[qrels["score"] > 0]
    return corpus, queries, qrels


def slug(words):
    return "-".join(re.sub(r"[^a-z0-9]+", "", w.lower()) for w in words if w)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("raw")
    ap.add_argument("out")
    ap.add_argument("lang", choices=["en", "nl"])
    ap.add_argument("--fiqa-distractors", type=int, default=None,
                    help="keep every judged-relevant FiQA passage plus this many random others (default: all)")
    ap.add_argument("--query-limit", default="", help="SRC=N,... keep only N randomly chosen judged queries per source")
    ap.add_argument("--keep", default="", help="SRC=N,... keep every judged-relevant passage plus N random others for that source")
    ap.add_argument("--extra", action="append", default=[],
                    help="a corpus (RAW/<lang>/NAME/corpus.parquet) added as distractor documents without queries, for the scale test")
    ap.add_argument("--seed", type=int, default=0)
    args = ap.parse_args()
    rng = random.Random(args.seed)
    raw, out = Path(args.raw), Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    docs, queries = [], []
    limits = {k: int(v) for k, v in (kv.split("=") for kv in args.query_limit.split(",") if kv)}
    keeps = {k: int(v) for k, v in (kv.split("=") for kv in args.keep.split(",") if kv)}
    if args.fiqa_distractors is not None:
        keeps.setdefault("fiqa", args.fiqa_distractors)
    for source in SOURCES + args.extra:
        corpus, qtable, qrels = load_source(raw, args.lang, source, distractors_only=source in args.extra)
        if source in limits:
            judged = sorted(qrels["query-id"].unique())
            chosen = set(random.Random(args.seed).sample(judged, min(limits[source], len(judged))))
            qrels = qrels[qrels["query-id"].isin(chosen)]
        relevant_ids = set(qrels["corpus-id"])
        if source in keeps:
            others = [i for i in corpus["_id"] if i not in relevant_ids]
            keep = relevant_ids | set(rng.sample(others, min(keeps[source], len(others))))
            corpus = corpus[corpus["_id"].isin(keep)].reset_index(drop=True)
        print(f"{source}: {len(corpus)} passages, {qrels['query-id'].nunique()} judged queries", file=sys.stderr)

        # --- group passages into topical documents
        full_text = (corpus["title"] + " " + corpus["text"]).tolist()
        tfidf = TfidfVectorizer(max_features=40000, min_df=3, max_df=0.2, sublinear_tf=True, dtype=np.float32)
        x = tfidf.fit_transform(full_text)
        terms = np.array(tfidf.get_feature_names_out())
        k = max(2, len(corpus) // PASSAGES_PER_DOC)
        svd = TruncatedSVD(n_components=min(100, x.shape[1] - 1), random_state=args.seed)
        z = normalize(svd.fit_transform(x))
        km = MiniBatchKMeans(n_clusters=k, batch_size=4096, n_init=1, random_state=args.seed).fit(z)
        members = defaultdict(list)
        for index, label in enumerate(km.labels_):
            members[int(label)].append(index)
        centroid_terms = {}
        for label, idxs in members.items():
            weights = np.asarray(x[idxs].mean(axis=0)).ravel()
            centroid_terms[label] = terms[np.argsort(-weights)[:3]].tolist()

        # --- render documents with passage offsets
        for label, idxs in sorted(members.items()):
            idxs = sorted(idxs, key=lambda i: corpus["_id"][i])
            parts = [idxs[i:i + 50] for i in range(0, len(idxs), 50)] if len(idxs) > MAX_PASSAGES_PER_DOC else [idxs]
            for part_no, part in enumerate(parts):
                name = f"{slug(centroid_terms[label])}-{source}-{label}{'' if len(parts) == 1 else '-' + str(part_no)}.md"
                text = f"# {' '.join(centroid_terms[label]).title()} ({source} notes)\n\n"
                passages = []
                for i in part:
                    row = corpus.iloc[i]
                    start = len(text)
                    block = (f"## {row['title'].strip()}\n\n" if row["title"].strip() else "") + row["text"].strip() + "\n\n"
                    text += block
                    passages.append({"pid": f"{source}:{row['_id']}", "start": start, "end": len(text) - 2})
                docs.append({"docId": f"{source}-{label}-{part_no}", "name": name, "source": source,
                             "text": text, "passages": passages})

        # --- queries (test split only, judged relevant passages that are in the library)
        present = set(corpus["_id"])
        qtext = dict(zip(qtable["_id"], qtable["text"]))
        grades = defaultdict(dict)
        for row in qrels.itertuples(index=False):
            if row[1] in present:
                grades[row[0]][f"{source}:{row[1]}"] = int(row[2])
        for qid, rel in grades.items():
            if qid in qtext and rel:
                queries.append({"qid": f"{source}:{qid}", "source": source, "text": qtext[qid].strip(), "rel": rel})

    with open(out / "docs.jsonl", "w", encoding="utf-8") as f:
        for d in docs:
            f.write(json.dumps(d, ensure_ascii=False) + "\n")
    with open(out / "queries.jsonl", "w", encoding="utf-8") as f:
        for q in queries:
            f.write(json.dumps(q, ensure_ascii=False) + "\n")
    chars = [len(d["text"]) for d in docs]
    print(f"{len(docs)} documents (chars: mean {np.mean(chars):.0f}, median {np.median(chars):.0f}, max {max(chars)}, "
          f"total {sum(chars) / 1e6:.1f} MB), {len(queries)} queries", file=sys.stderr)


if __name__ == "__main__":
    main()
