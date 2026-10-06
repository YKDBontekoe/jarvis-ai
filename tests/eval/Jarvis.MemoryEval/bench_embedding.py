"""CPU performance benchmark for local embedding models, using the memory eval's own texts (1,000 Dutch/English
memories and 60 questions from dataset-large.json), so token lengths match what Jarvis embeds.

Each configuration runs in a fresh subprocess, so load time and memory (RSS) are not polluted by earlier configurations.

    python3 bench_embedding.py [--repeat 2] [--only name,name] out.json

Needs torch, sentence-transformers, psutil (and pillow for EmbeddingGemma 2). Run it on an otherwise idle machine.
"""
import json, os, resource, statistics, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
MINI = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
EG2 = "google/embeddinggemma-2"
# name: (model, dtype, quant, threads, text_only)
CONFIGS = {
    "minilm-fp32-t4": (MINI, "fp32", None, 4, True),
    "minilm-fp32-t1": (MINI, "fp32", None, 1, True),
    "minilm-int8-t4": (MINI, "fp32", "int8", 4, True),
    "eg2-fp32-t4": (EG2, "fp32", None, 4, True),
    "eg2-fp32-t1": (EG2, "fp32", None, 1, True),
    "eg2-int8-t4": (EG2, "fp32", "int8", 4, True),
    "eg2-multimodal-fp32-t4": (EG2, "fp32", None, 4, False),
}


def percentile(values, p):
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(round(p / 100 * (len(ordered) - 1))))]


def worker(name):
    import psutil, torch
    from sentence_transformers import SentenceTransformer

    model_name, dtype, quant, threads, text_only = CONFIGS[name]
    torch.set_num_threads(threads)
    process = psutil.Process()
    data = json.load(open(os.path.join(HERE, "dataset-large.json")))
    docs = [m["content"] for m in data["memories"]]
    queries = [q["text"] for q in data["queries"]]
    gemma = "embeddinggemma" in model_name

    kwargs = {}
    if gemma:
        kwargs["model_kwargs"] = {"torch_dtype": torch.bfloat16 if dtype == "bf16" else torch.float32}
        if text_only:
            kwargs["config_kwargs"] = {"vision_config": None, "audio_config": None}
    rss_before = process.memory_info().rss / 2**20
    started = time.perf_counter()
    model = SentenceTransformer(model_name, **kwargs)
    if quant == "int8":
        model = torch.quantization.quantize_dynamic(model, {torch.nn.Linear}, dtype=torch.qint8)
    load_s = time.perf_counter() - started
    rss_loaded = process.memory_info().rss / 2**20
    params = sum(p.numel() for p in model.parameters()) / 1e6

    q_args = {"prompt_name": "query"} if gemma else {}
    d_args = {"prompt_name": "document"} if gemma else {}

    # Token counts (what the model actually processes, prompt prefix included).
    def tokens(texts, args):
        prefix = model.prompts.get(args.get("prompt_name"), "") if args else ""
        ids = model.tokenizer([prefix + t for t in texts], add_special_tokens=True)["input_ids"]
        return [len(i) for i in ids]

    doc_tokens, query_tokens = tokens(docs, d_args), tokens(queries, q_args)

    with torch.inference_mode():
        for text in queries[:10]:  # warm-up: allocator, oneDNN kernels
            model.encode([text], **q_args)
        model.encode(docs[:32], batch_size=32, **d_args)

        # One search = one query embedding. Five passes over the 60 questions, one at a time.
        latencies = []
        for _ in range(5):
            for text in queries:
                t = time.perf_counter()
                model.encode([text], **q_args)
                latencies.append((time.perf_counter() - t) * 1000)

        # Indexing throughput: embedding memories in batches (the background indexer / re-embed after a model switch).
        throughput = {}
        for batch in (1, 8, 32):
            sample = docs[:200]
            t = time.perf_counter()
            model.encode(sample, batch_size=batch, **d_args)
            throughput[batch] = len(sample) / (time.perf_counter() - t)
        t = time.perf_counter()
        vectors = model.encode(docs, batch_size=32, **d_args)
        reindex_1000_s = time.perf_counter() - t

    return {
        "name": name, "model": model_name, "dtype": dtype, "quant": quant, "threads": threads,
        "text_only": text_only, "params_m": round(params, 1), "dimensions": int(vectors.shape[1]),
        "load_s": round(load_s, 2), "rss_before_mb": round(rss_before), "rss_loaded_mb": round(rss_loaded),
        "rss_model_mb": round(rss_loaded - rss_before), "rss_peak_mb": round(resource.getrusage(resource.RUSAGE_SELF).ru_maxrss / 1024),
        "doc_tokens_mean": round(statistics.mean(doc_tokens), 1), "doc_tokens_max": max(doc_tokens),
        "query_tokens_mean": round(statistics.mean(query_tokens), 1),
        "query_ms_p50": round(percentile(latencies, 50), 1), "query_ms_p95": round(percentile(latencies, 95), 1),
        "query_ms_mean": round(statistics.mean(latencies), 1),
        "docs_per_s": {str(k): round(v, 1) for k, v in throughput.items()},
        "reindex_1000_s": round(reindex_1000_s, 1),
    }


def main():
    args = sys.argv[1:]
    if args[0] == "--worker":
        print("RESULT " + json.dumps(worker(args[1])))
        return
    repeat, only = 1, None
    while args[0].startswith("--"):
        flag, value = args[0], args[1]
        repeat, only = (int(value), only) if flag == "--repeat" else (repeat, value.split(","))
        args = args[2:]
    runs = {}
    for round_ in range(repeat):
        for name in only or CONFIGS:
            out = subprocess.run([sys.executable, os.path.abspath(__file__), "--worker", name],
                                 capture_output=True, text=True, env={**os.environ, "TOKENIZERS_PARALLELISM": "false"})
            line = next((l for l in out.stdout.splitlines() if l.startswith("RESULT ")), None)
            if line is None:
                print(name, "FAILED", out.stderr[-600:], file=sys.stderr)
                continue
            runs.setdefault(name, []).append(json.loads(line[7:]))
            r = runs[name][-1]
            json.dump(runs, open(args[0], "w"), indent=1)  # keep partial results if the run is interrupted
            print(f"[{round_ + 1}/{repeat}] {name:24s} load {r['load_s']:5.1f}s  rss {r['rss_loaded_mb']:5d}/{r['rss_peak_mb']:5d} MB  "
                  f"query p50 {r['query_ms_p50']:6.1f} ms  p95 {r['query_ms_p95']:6.1f} ms  "
                  f"docs/s {r['docs_per_s']}  1000 docs {r['reindex_1000_s']}s", flush=True)
    json.dump(runs, open(args[0], "w"), indent=1)


if __name__ == "__main__":
    main()
