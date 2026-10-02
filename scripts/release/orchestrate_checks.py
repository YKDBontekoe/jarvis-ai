#!/usr/bin/env python3
"""Start backend and iOS release builds and wait until both succeed.

The Release workflow is the only production approval. It dispatches the
backend and iOS workflows in check-only mode, waits for those builds, and
returns the run ids plus the iOS version baked into the IPA.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Callable, Protocol

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from semver import SemVerError, parse_semver_tag

BACKEND_WORKFLOW = "deploy-backend.yml"
IOS_WORKFLOW = "release-ios.yml"
META_ARTIFACT = "jarvis-release-meta"
RUN_FIELDS = (
    "databaseId,headSha,headBranch,event,status,conclusion,createdAt,url"
)


class ReleaseCheckError(RuntimeError):
    """Raised when a release check cannot be dispatched or does not succeed."""


class GhClient(Protocol):
    def workflow_run(self, workflow: str, ref: str, fields: dict[str, str]) -> None:
        """Dispatch workflow_dispatch for one workflow file."""

    def list_runs(self, workflow: str) -> list[dict[str, Any]]:
        """Return recent runs for a workflow file, newest first."""

    def download_artifact(self, run_id: int, name: str, dest: Path) -> None:
        """Download one artifact from a completed run into dest."""


def tag_name_from_ref(ref: str) -> str:
    """Return vMAJOR.MINOR.PATCH from a tag name or refs/tags/ name."""
    if ref.startswith("refs/") and not ref.startswith("refs/tags/"):
        raise ReleaseCheckError(
            f"Release must run on a vMAJOR.MINOR.PATCH tag, not '{ref}'."
        )
    name = ref.removeprefix("refs/tags/")
    try:
        parse_semver_tag(name)
    except SemVerError as exc:
        raise ReleaseCheckError(str(exc)) from exc
    return name


def parse_github_timestamp(value: str) -> datetime:
    text = value.strip()
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    parsed = datetime.fromisoformat(text)
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def select_dispatched_run(
    runs: list[dict[str, Any]],
    *,
    head_sha: str,
    tag: str,
    created_after: datetime,
) -> dict[str, Any] | None:
    """Pick the workflow_dispatch run this orchestrator just started."""
    matches: list[dict[str, Any]] = []
    for run in runs:
        if run.get("event") != "workflow_dispatch":
            continue
        created = parse_github_timestamp(str(run["createdAt"]))
        if created < created_after:
            continue
        same_commit = run.get("headSha") == head_sha or run.get("headBranch") == tag
        if not same_commit:
            continue
        matches.append(run)
    if not matches:
        return None
    matches.sort(key=lambda run: parse_github_timestamp(str(run["createdAt"])))
    return matches[0]


def run_finished_successfully(run: dict[str, Any]) -> bool:
    return run.get("status") == "completed" and run.get("conclusion") == "success"


def run_finished_unsuccessfully(run: dict[str, Any]) -> bool:
    return run.get("status") == "completed" and run.get("conclusion") != "success"


def parse_release_meta(path: Path, *, tag: str) -> tuple[str, str]:
    """Read the iOS workflow's version artifact and require it to match the tag."""
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ReleaseCheckError(f"Could not read iOS release metadata from {path}.") from exc
    version = str(payload.get("version", "")).strip()
    build = str(payload.get("build", "")).strip()
    if f"v{version}" != tag:
        raise ReleaseCheckError(
            f"iOS build version '{version}' does not match release tag '{tag}'."
        )
    if not build.isdigit():
        raise ReleaseCheckError(f"iOS build number '{build}' is not a positive integer.")
    return version, build


def _refresh_run(runs: list[dict[str, Any]], run_id: int) -> dict[str, Any] | None:
    for run in runs:
        if int(run["databaseId"]) == run_id:
            return run
    return None


def _fail_run(workflow: str, run: dict[str, Any]) -> None:
    url = run.get("url") or ""
    conclusion = run.get("conclusion") or "unknown"
    raise ReleaseCheckError(
        f"{workflow} finished with conclusion '{conclusion}'. {url}".strip()
    )


def orchestrate(
    gh: GhClient,
    *,
    ref: str,
    sha: str,
    now: Callable[[], datetime],
    sleep: Callable[[float], None],
    timeout: timedelta,
    interval: timedelta,
    skew: timedelta,
    meta_dir: Path,
    discover_within: timedelta = timedelta(minutes=10),
) -> dict[str, str]:
    """Dispatch both check workflows and return ids plus the iOS version."""
    tag = tag_name_from_ref(ref)
    watermark = now() - skew
    deadline = now() + timeout
    discover_deadline = now() + discover_within
    gh.workflow_run(BACKEND_WORKFLOW, tag, {"skip_deploy": "true"})
    gh.workflow_run(IOS_WORKFLOW, tag, {"skip_publish": "true"})

    selected: dict[str, dict[str, Any] | None] = {
        BACKEND_WORKFLOW: None,
        IOS_WORKFLOW: None,
    }

    while True:
        current = now()
        all_success = True
        for workflow in (BACKEND_WORKFLOW, IOS_WORKFLOW):
            runs = gh.list_runs(workflow)
            current_run = selected[workflow]
            if current_run is None:
                chosen = select_dispatched_run(
                    runs,
                    head_sha=sha,
                    tag=tag,
                    created_after=watermark,
                )
                if chosen is None:
                    all_success = False
                    if current > discover_deadline:
                        raise ReleaseCheckError(
                            f"No workflow_dispatch run for {workflow} appeared for {tag}."
                        )
                    continue
                current_run = chosen
                selected[workflow] = current_run
                print(
                    f"Watching {workflow} run {current_run['databaseId']} {current_run.get('url', '')}",
                    file=sys.stderr,
                )
            else:
                refreshed = _refresh_run(runs, int(current_run["databaseId"]))
                if refreshed is not None:
                    current_run = refreshed
                    selected[workflow] = current_run

            if run_finished_unsuccessfully(current_run):
                _fail_run(workflow, current_run)
            if not run_finished_successfully(current_run):
                status = current_run.get("status") or "unknown"
                print(
                    f"{workflow} run {current_run['databaseId']} is {status}.",
                    file=sys.stderr,
                )
                all_success = False

        if all_success and all(selected.values()):
            break
        if current > deadline:
            pending = [
                name
                for name, run in selected.items()
                if not run_finished_successfully(run or {})
            ]
            raise ReleaseCheckError(
                "Timed out waiting for release checks: " + ", ".join(pending)
            )
        sleep(interval.total_seconds())

    backend = selected[BACKEND_WORKFLOW]
    ios = selected[IOS_WORKFLOW]
    assert backend is not None and ios is not None
    ios_run_id = int(ios["databaseId"])
    meta_dir.mkdir(parents=True, exist_ok=True)
    gh.download_artifact(ios_run_id, META_ARTIFACT, meta_dir)
    version, build = parse_release_meta(meta_dir / "release-meta.json", tag=tag)
    return {
        "backend_run_id": str(int(backend["databaseId"])),
        "ios_run_id": str(ios_run_id),
        "backend_url": str(backend.get("url") or ""),
        "ios_url": str(ios.get("url") or ""),
        "version": version,
        "build": build,
        "tag": tag,
    }


class CommandGh:
    """GitHub CLI client used by the Release workflow."""

    def __init__(self, repository: str | None = None) -> None:
        self.repository = repository

    def _run(self, args: list[str]) -> str:
        command = ["gh", *args]
        env = os.environ.copy()
        if self.repository:
            env["GH_REPO"] = self.repository
        try:
            result = subprocess.run(
                command,
                check=False,
                text=True,
                capture_output=True,
                env=env,
            )
        except FileNotFoundError as exc:
            raise ReleaseCheckError("GitHub CLI (gh) is not installed.") from exc
        if result.returncode != 0:
            detail = (result.stderr or result.stdout).strip()
            raise ReleaseCheckError(detail or f"gh {' '.join(args)} failed.")
        return result.stdout

    def workflow_run(self, workflow: str, ref: str, fields: dict[str, str]) -> None:
        args = ["workflow", "run", workflow, "--ref", ref]
        for key, value in fields.items():
            args.extend(["--raw-field", f"{key}={value}"])
        self._run(args)

    def list_runs(self, workflow: str) -> list[dict[str, Any]]:
        raw = self._run(
            [
                "run",
                "list",
                "--workflow",
                workflow,
                "--limit",
                "40",
                "--json",
                RUN_FIELDS,
            ]
        )
        payload = json.loads(raw or "[]")
        if not isinstance(payload, list):
            raise ReleaseCheckError(f"Unexpected run list for {workflow}.")
        return payload

    def download_artifact(self, run_id: int, name: str, dest: Path) -> None:
        dest.mkdir(parents=True, exist_ok=True)
        self._run(
            [
                "run",
                "download",
                str(run_id),
                "--name",
                name,
                "--dir",
                str(dest),
            ]
        )


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


def write_github_output(result: dict[str, str]) -> None:
    output_path = os.environ.get("GITHUB_OUTPUT")
    if not output_path:
        raise ReleaseCheckError("GITHUB_OUTPUT is not set.")
    lines = (
        f"backend_run_id={result['backend_run_id']}\n",
        f"ios_run_id={result['ios_run_id']}\n",
        f"version={result['version']}\n",
        f"build={result['build']}\n",
    )
    with open(output_path, "a", encoding="utf-8") as handle:
        handle.writelines(lines)
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as handle:
            handle.write(
                "\n".join(
                    [
                        "## Release checks",
                        "",
                        f"- Tag: `{result['tag']}`",
                        f"- iOS: `{result['version']}+{result['build']}`",
                        f"- Backend run: {result['backend_url'] or result['backend_run_id']}",
                        f"- iOS run: {result['ios_url'] or result['ios_run_id']}",
                        "",
                    ]
                )
            )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ref", required=True, help="refs/tags/vX.Y.Z or vX.Y.Z")
    parser.add_argument("--sha", required=True, help="Commit SHA the tag points at")
    parser.add_argument("--repository", default="", help="owner/name override for gh")
    parser.add_argument("--timeout-seconds", type=int, default=10200)
    parser.add_argument("--interval-seconds", type=int, default=20)
    parser.add_argument("--skew-seconds", type=int, default=5)
    parser.add_argument(
        "--github-output",
        action="store_true",
        help="Append check outputs to GITHUB_OUTPUT",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    meta_dir = Path(os.environ.get("RUNNER_TEMP", "/tmp")) / "jarvis-release-meta"
    try:
        result = orchestrate(
            CommandGh(args.repository or None),
            ref=args.ref,
            sha=args.sha,
            now=_utc_now,
            sleep=time.sleep,
            timeout=timedelta(seconds=args.timeout_seconds),
            interval=timedelta(seconds=args.interval_seconds),
            skew=timedelta(seconds=args.skew_seconds),
            meta_dir=meta_dir,
        )
    except ReleaseCheckError as exc:
        print(exc, file=sys.stderr)
        return 1
    if args.github_output:
        try:
            write_github_output(result)
        except ReleaseCheckError as exc:
            print(exc, file=sys.stderr)
            return 1
    else:
        for key in ("backend_run_id", "ios_run_id", "version", "build"):
            print(f"{key}={result[key]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
