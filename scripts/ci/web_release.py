#!/usr/bin/env python3
"""Publish the Flutter web build under a revision path.

Cloudflare caches JavaScript and other static extensions for four hours and
replaces the origin Cache-Control header with max-age=14400. The homepage is
not cached. Putting each release at /r/<revision>/ makes the asset URLs
change when the git revision changes, so a refreshed browser loads that
release instead of the previous main.dart.js.
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

REVISION_RE = re.compile(r"^[A-Za-z0-9._-]{7,64}$")
REPO_ROOT = Path(__file__).resolve().parents[2]


def validate_revision(revision: str) -> str:
    if not REVISION_RE.fullmatch(revision) or ".." in revision:
        raise ValueError(
            "Web revision must be 7-64 characters of letters, numbers, dots, underscores, or hyphens."
        )
    return revision


def base_href(revision: str) -> str:
    return f"/r/{validate_revision(revision)}/"


def resolve_revision(
    explicit: str | None = None,
    github_sha: str | None = None,
    git_head: str | None = None,
) -> str:
    for candidate in (explicit, github_sha, git_head):
        if candidate and candidate.strip():
            return validate_revision(candidate.strip())
    raise ValueError("Set JARVIS_WEB_REVISION or GITHUB_SHA, or run from a git checkout.")


def stage_web_release(source: Path, destination: Path) -> str:
    stamp = source / ".jarvis-web-revision"
    if not stamp.is_file():
        raise ValueError("Web build is missing .jarvis-web-revision. Run scripts/ci/build-web.sh.")
    revision = validate_revision(stamp.read_text(encoding="utf-8").strip())
    expected_href = f'<base href="{base_href(revision)}">'
    index_path = source / "index.html"
    if not index_path.is_file() or expected_href not in index_path.read_text(encoding="utf-8"):
        raise ValueError(f"index.html must contain {expected_href}")
    for name in ("main.dart.js", "flutter_bootstrap.js"):
        file = source / name
        if not file.is_file() or file.stat().st_size == 0:
            raise ValueError(f"Web build is missing {name}")

    if destination.exists():
        for child in destination.iterdir():
            if child.is_dir() and not child.is_symlink():
                shutil.rmtree(child)
            else:
                child.unlink()
    else:
        destination.mkdir(parents=True)

    release_dir = destination / "r" / revision
    release_dir.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source, release_dir, ignore=_ignore_private_files)
    shutil.copy2(release_dir / "index.html", destination / "index.html")
    return revision


def _ignore_private_files(directory: str, names: list[str]) -> set[str]:
    del directory
    return {
        name
        for name in names
        if name == ".jarvis-web-revision" or name.endswith(".map")
    }


def _git_head() -> str | None:
    try:
        completed = subprocess.run(
            ["git", "-C", str(REPO_ROOT), "rev-parse", "HEAD"],
            check=True,
            capture_output=True,
            text=True,
        )
    except (OSError, subprocess.CalledProcessError):
        return None
    return completed.stdout.strip() or None


def _print_revision() -> None:
    revision = resolve_revision(
        os.environ.get("JARVIS_WEB_REVISION"),
        os.environ.get("GITHUB_SHA"),
        _git_head(),
    )
    sys.stdout.write(revision)


def main() -> None:
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("print-revision")
    stage = commands.add_parser("stage")
    stage.add_argument("--source", type=Path, required=True)
    stage.add_argument("--destination", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.command == "print-revision":
            _print_revision()
        else:
            revision = stage_web_release(args.source, args.destination)
            sys.stdout.write(revision + "\n")
    except ValueError as error:
        raise SystemExit(str(error)) from error


if __name__ == "__main__":
    main()
