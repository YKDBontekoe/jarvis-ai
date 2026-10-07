#!/usr/bin/env python3
"""Hybrid search over the routing corpus, shared by all three routes.

Loads OUT/passages.jsonl into a scratch PostgreSQL database (pg_textsearch BM25 + pgvector with bge-small-en-v1.5), then
serves  GET /search?qid=<question id>&q=<query>&k=5  on localhost. Ranking is the best pipeline from the file eval: BM25 and
dense lists fused with reciprocal rank fusion. Every call is appended to OUT/calls.jsonl, which is where the cost of a route
(number of searches per question) and its support recall come from.

Usage: search_service.py OUT_DIR [--port 8765] [--pg "host=localhost port=5433 user=jarvis password=... dbname=fe_route"]
"""
import argparse
import json
import re
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

import numpy as np
import psycopg2
from pgvector.psycopg2 import register_vector

QUERY_PREFIX = "Represent this sentence for searching relevant passages: "
STOP = set("a an the and or but if of to in on at for with from by about as into is am are was were be been being do does did "
           "have has had i me my you your we our he she it its they them their this that these those what which who whom when "
           "where why how can could should would will shall may might must".split())


def terms(text):
    return [t for t in re.findall(r"[a-z0-9]+", text.lower()) if len(t) > 1 and t not in STOP]


class Service:
    def __init__(self, out: Path, pg: str):
        from sentence_transformers import SentenceTransformer
        self.out = out
        self.lock = threading.Lock()
        self.model = SentenceTransformer("BAAI/bge-small-en-v1.5", device="cpu")
        self.model.max_seq_length = 256
        admin = psycopg2.connect(pg.replace("dbname=fe_route", "dbname=postgres"), options="-c client_encoding=UTF8")
        admin.autocommit = True
        cur = admin.cursor()
        cur.execute("SELECT 1 FROM pg_database WHERE datname = 'fe_route'")
        if not cur.fetchone():
            cur.execute("CREATE DATABASE fe_route")
        admin.close()
        self.db = psycopg2.connect(pg, options="-c client_encoding=UTF8")
        self.db.autocommit = True
        c = self.db.cursor()
        c.execute("CREATE EXTENSION IF NOT EXISTS vector")
        c.execute("CREATE EXTENSION IF NOT EXISTS pg_textsearch")
        register_vector(self.db)
        c.execute("SELECT to_regclass('passages') IS NOT NULL")
        if not c.fetchone()[0]:
            self._load()

    def _load(self):
        rows = [json.loads(l) for l in open(self.out / "passages.jsonl", encoding="utf-8")]
        c = self.db.cursor()
        c.execute("CREATE TABLE passages (pid text PRIMARY KEY, title text, body text, content text, embedding vector(384))")
        content = [f"{r['title']}. {r['text']}" for r in rows]
        cache = self.out / "passages.bge-small.npy"
        if cache.exists():
            vectors = np.load(cache)
        else:
            t = time.time()
            vectors = self.model.encode(content, batch_size=32, normalize_embeddings=True, show_progress_bar=False)
            np.save(cache, vectors)
            print(f"embedded {len(rows)} passages in {time.time() - t:.0f}s", flush=True)
        for r, text, v in zip(rows, content, vectors):
            c.execute("INSERT INTO passages VALUES (%s, %s, %s, %s, %s)", (r["pid"], r["title"], r["text"], text, v))
        c.execute("CREATE INDEX ix_passages_bm25 ON passages USING bm25(content) WITH (text_config='english')")
        c.execute("CREATE INDEX ix_passages_hnsw ON passages USING hnsw (embedding vector_cosine_ops)")
        c.execute("ANALYZE passages")

    def search(self, qid: str, query: str, k: int = 5):
        with self.lock:
            c = self.db.cursor()
            literal = " ".join(terms(query))
            lex = []
            if literal:
                c.execute("SELECT pid FROM passages ORDER BY content <@> to_bm25query(%s, 'ix_passages_bm25') LIMIT 30", (literal,))
                lex = [r[0] for r in c.fetchall()]
            vec = self.model.encode([QUERY_PREFIX + query], normalize_embeddings=True)[0]
            c.execute("SELECT pid FROM passages ORDER BY embedding <=> %s LIMIT 30", (vec,))
            sem = [r[0] for r in c.fetchall()]
            score = {}
            for ranking in (lex, sem):
                for rank, p in enumerate(ranking):
                    score[p] = score.get(p, 0) + 1 / (60 + rank + 1)
            top = [p for p, _ in sorted(score.items(), key=lambda x: -x[1])[:k]]
            c.execute("SELECT pid, title, body FROM passages WHERE pid = ANY(%s)", (top,))
            by = {r[0]: r for r in c.fetchall()}
            hits = [by[p] for p in top]
            with open(self.out / "calls.jsonl", "a", encoding="utf-8") as f:
                f.write(json.dumps({"t": time.time(), "qid": qid, "q": query, "pids": top}) + "\n")
            return hits


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--pg", default="host=localhost port=5433 user=jarvis password=evalpass dbname=fe_route")
    args = ap.parse_args()
    service = Service(Path(args.out), args.pg)

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            url = urlparse(self.path)
            if url.path != "/search":
                self.send_error(404)
                return
            q = parse_qs(url.query)
            query = q.get("q", [""])[0].strip()
            if not query:
                body = "Provide a non-empty q parameter."
            else:
                hits = service.search(q.get("qid", ["?"])[0], query, min(int(q.get("k", ["5"])[0]), 10))
                body = "\n\n".join(f"[{i + 1}] {title}: {text}" for i, (_, title, text) in enumerate(hits)) or "No results."
            data = body.encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def log_message(self, *_):
            pass

    print(f"serving on {args.port}", flush=True)
    ThreadingHTTPServer(("127.0.0.1", args.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
