#!/usr/bin/env python3
"""Compares routing policies using the executed route matrix (every test question was run through every route once).

A policy picks a route per question; its quality and cost are then read off the matrix, so any router can be evaluated
without re-running models. Policies: always-X, oracle (best F1, ties go to the cheapest), the hop-count label (what the
routers are trained to predict), each trained small router, and a zero-shot LLM router.

Cost is reported as LLM calls and searches per question (an agentic question's LLM calls are searches + 1). Quality is token F1
and "contains" (the gold answer appears in the prediction). 95% confidence intervals come from a bootstrap over questions.

Usage: route_policies.py MATRIX_JSON ROUTERS_JSON LLM_ROUTER_JSON [--out report.json]
"""
import argparse
import json
from collections import Counter

import numpy as np

ROUTES = ["direct", "decompose", "agentic"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("matrix")
    ap.add_argument("routers")
    ap.add_argument("llm_router")
    ap.add_argument("--out")
    ap.add_argument("--boot", type=int, default=2000)
    args = ap.parse_args()
    matrix = json.load(open(args.matrix))
    routers = json.load(open(args.routers))
    llm = json.load(open(args.llm_router))
    qids = sorted(matrix)
    rng = np.random.default_rng(0)

    def table(metric):
        return np.array([[matrix[q][r][metric] for r in ROUTES] for q in qids])

    f1, contains, calls, searches = table("f1"), table("contains"), table("llm_calls"), table("searches")
    truth = np.array([ROUTES.index(matrix[q]["route"]) for q in qids])
    n = len(qids)

    print("== each route on each true question class (F1 / contains / LLM calls / searches)")
    for c, name in enumerate(ROUTES):
        rows = np.where(truth == c)[0]
        print(f"true class {name:10s} n={len(rows)}")
        for r, rname in enumerate(ROUTES):
            print(f"    run as {rname:10s} F1 {f1[rows, r].mean():.3f}  contains {contains[rows, r].mean():.3f}  "
                  f"calls {calls[rows, r].mean():.2f}  searches {searches[rows, r].mean():.2f}")

    policies = {f"always-{r}": np.full(n, i) for i, r in enumerate(ROUTES)}
    policies["oracle (best F1, then cheapest)"] = np.array(
        [min(range(3), key=lambda r: (-f1[i, r], calls[i, r])) for i in range(n)])
    policies["hop-count label (training target)"] = truth
    for name, p in routers["predictions"].items():
        policies[f"router: {name}"] = np.array([ROUTES.index(p["test"][q]) for q in qids])
    policies["router: zero-shot LLM (haiku)"] = np.array([ROUTES.index(llm[q]) for q in qids])

    def evaluate(choice, idx):
        i = np.arange(len(idx))
        return (f1[idx, choice[idx]].mean(), contains[idx, choice[idx]].mean(),
                calls[idx, choice[idx]].mean(), searches[idx, choice[idx]].mean())

    print("\n== policies over all test questions")
    print(f"{'policy':40s} {'F1':>6s} {'95% CI':>15s} {'contains':>9s} {'LLM calls':>10s} {'searches':>9s} {'route mix d/c/a':>16s} {'agree w/ label':>15s}")
    boots = [rng.integers(0, n, n) for _ in range(args.boot)]
    base_f1 = {name: np.array([f1[b, choice[b]].mean() for b in boots]) for name, choice in policies.items()}
    report = {}
    for name, choice in policies.items():
        m = evaluate(choice, np.arange(n))
        lo, hi = np.percentile(base_f1[name], [2.5, 97.5])
        mix = Counter(choice.tolist())
        agree = (choice == truth).mean()
        print(f"{name:40s} {m[0]:6.3f} [{lo:.3f},{hi:.3f}] {m[1]:9.3f} {m[2]:10.2f} {m[3]:9.2f} "
              f"{mix.get(0, 0):4d}/{mix.get(1, 0):3d}/{mix.get(2, 0):3d}   {agree:14.2f}")
        report[name] = {"f1": m[0], "f1_ci": [lo, hi], "contains": m[1], "llm_calls": m[2], "searches": m[3],
                        "mix": [mix.get(0, 0), mix.get(1, 0), mix.get(2, 0)], "agrees_with_label": agree}

    print("\n== paired F1 difference against always-agentic (negative = worse; cost saved in LLM calls)")
    ref = policies["always-agentic"]
    for name, choice in policies.items():
        if name == "always-agentic":
            continue
        diffs = np.array([f1[b, choice[b]].mean() - f1[b, ref[b]].mean() for b in boots])
        lo, hi = np.percentile(diffs, [2.5, 97.5])
        saved = 1 - calls[np.arange(n), choice].mean() / calls[np.arange(n), ref].mean()
        print(f"{name:40s} dF1 {diffs.mean():+.3f} [{lo:+.3f},{hi:+.3f}]   LLM calls saved {saved:5.1%}")

    print("\n== router classification quality (accuracy / macro-F1)")
    for name, m in routers["metrics"].items():
        print(f"{name:14s} dev {m['dev']['accuracy']:.3f}/{m['dev']['macro_f1']:.3f}  test {m['test']['accuracy']:.3f}/{m['test']['macro_f1']:.3f}  "
              f"assistant-style (out of domain) {m['assistant']['accuracy']:.3f}/{m['assistant']['macro_f1']:.3f}  {m['ms_per_query']:.1f} ms/query")
    ood_true = [json.loads(l)["label"] for l in open(__file__.replace("route_policies.py", "ood_assistant.jsonl"))]
    llm_ood = [llm[f"assistant-{i}"] for i in range(len(ood_true))]
    print(f"{'zero-shot LLM':14s} test {np.mean([llm[q] == matrix[q]['route'] for q in qids]):.3f}  "
          f"assistant-style {np.mean([a == b for a, b in zip(llm_ood, ood_true)]):.3f}")

    print("\n== predicted route mix on BEIR-style single-hop questions (FiQA/NFCorpus), expected mostly direct")
    for name, p in routers["predictions"].items():
        c = Counter(p["beir"])
        total = sum(c.values()) or 1
        print(f"{name:14s} " + "  ".join(f"{r} {c.get(r, 0) / total:.0%}" for r in ROUTES))
    if args.out:
        json.dump(report, open(args.out, "w"), indent=1)


if __name__ == "__main__":
    main()
