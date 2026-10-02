#!/usr/bin/env python3
"""Point a production env file at the Sentry release that matches the uploaded symbols."""

from __future__ import annotations

import argparse
from pathlib import Path


def upsert_release(text: str, release: str) -> str:
    if not release or any(character in release for character in "\r\n"):
        raise ValueError("Sentry release must be a single non-empty line.")
    lines = text.splitlines()
    found = False
    updated: list[str] = []
    for line in lines:
        stripped = line.strip()
        if stripped.startswith("SENTRY_RELEASE=") or stripped.startswith("export SENTRY_RELEASE="):
            prefix = "export " if stripped.startswith("export ") else ""
            updated.append(f"{prefix}SENTRY_RELEASE={release}")
            found = True
        else:
            updated.append(line)
    if not found:
        updated.append(f"SENTRY_RELEASE={release}")
    body = "\n".join(updated)
    if text.endswith("\n") or text == "":
        body += "\n"
    return body


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--env-file", type=Path, required=True)
    parser.add_argument("--release", required=True)
    args = parser.parse_args()
    path: Path = args.env_file
    if not path.is_file():
        raise SystemExit(f"Production env file not found: {path}")
    original = path.read_text(encoding="utf-8")
    path.write_text(upsert_release(original, args.release), encoding="utf-8")


if __name__ == "__main__":
    main()
