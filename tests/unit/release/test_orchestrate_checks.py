#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[3]
ORCHESTRATE_PATH = REPO_ROOT / "scripts" / "release" / "orchestrate_checks.py"


def load_orchestrate():
    spec = importlib.util.spec_from_file_location("orchestrate_checks", ORCHESTRATE_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {ORCHESTRATE_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


orchestrate = load_orchestrate()


def run(
    *,
    run_id: int,
    created_at: str,
    status: str = "completed",
    conclusion: str = "success",
    sha: str = "abc123",
    branch: str = "v1.2.3",
    event: str = "workflow_dispatch",
) -> dict:
    return {
        "databaseId": run_id,
        "headSha": sha,
        "headBranch": branch,
        "event": event,
        "status": status,
        "conclusion": conclusion,
        "createdAt": created_at,
        "url": f"https://example.test/runs/{run_id}",
    }


class FakeClock:
    def __init__(self) -> None:
        self.current = datetime(2026, 10, 2, tzinfo=timezone.utc)

    def now(self) -> datetime:
        return self.current

    def sleep(self, seconds: float) -> None:
        self.current += timedelta(seconds=seconds)


class FakeGh:
    def __init__(self, pages: dict[str, list[list[dict]]], meta: str) -> None:
        self.pages = pages
        self.calls = {workflow: 0 for workflow in pages}
        self.dispatches: list[tuple[str, str, dict[str, str]]] = []
        self.meta = meta
        self.downloaded: list[tuple[int, str]] = []

    def workflow_run(self, workflow: str, ref: str, fields: dict[str, str]) -> None:
        self.dispatches.append((workflow, ref, dict(fields)))

    def list_runs(self, workflow: str) -> list[dict]:
        series = self.pages[workflow]
        index = min(self.calls[workflow], len(series) - 1)
        self.calls[workflow] += 1
        return series[index]

    def download_artifact(self, run_id: int, name: str, dest: Path) -> None:
        self.downloaded.append((run_id, name))
        dest.mkdir(parents=True, exist_ok=True)
        (dest / "release-meta.json").write_text(self.meta, encoding="utf-8")


class OrchestrateChecksTests(unittest.TestCase):
    def test_selects_earliest_dispatch_after_watermark(self) -> None:
        runs = [
            run(run_id=3, created_at="2026-10-02T00:02:00Z"),
            run(run_id=2, created_at="2026-10-02T00:00:00Z"),
            run(run_id=1, created_at="2026-10-01T23:00:00Z"),
            run(run_id=4, created_at="2026-10-02T00:00:30Z", event="push"),
            run(run_id=5, created_at="2026-10-02T00:00:30Z", sha="other", branch="other"),
        ]
        chosen = orchestrate.select_dispatched_run(
            runs,
            head_sha="abc123",
            tag="v1.2.3",
            created_after=datetime(2026, 10, 2, tzinfo=timezone.utc) - timedelta(seconds=5),
        )
        self.assertIsNotNone(chosen)
        assert chosen is not None
        self.assertEqual(chosen["databaseId"], 2)

    def test_matches_tag_branch_when_commit_sha_differs(self) -> None:
        chosen = orchestrate.select_dispatched_run(
            [run(run_id=9, created_at="2026-10-02T00:00:00Z", sha="tag-object")],
            head_sha="commit",
            tag="v1.2.3",
            created_after=datetime(2026, 10, 1, 23, 59, tzinfo=timezone.utc),
        )
        self.assertIsNotNone(chosen)
        assert chosen is not None
        self.assertEqual(chosen["databaseId"], 9)

    def test_waits_until_both_checks_succeed(self) -> None:
        created = "2026-10-02T00:00:00Z"
        backend_running = [run(run_id=10, created_at=created, status="in_progress", conclusion="")]
        backend_done = [run(run_id=10, created_at=created)]
        ios_running = [run(run_id=20, created_at=created, status="in_progress", conclusion="")]
        ios_done = [run(run_id=20, created_at=created)]
        gh = FakeGh(
            {
                orchestrate.BACKEND_WORKFLOW: [backend_running, backend_done],
                orchestrate.IOS_WORKFLOW: [ios_running, ios_done],
            },
            json.dumps({"version": "1.2.3", "build": "10020030123"}),
        )
        clock = FakeClock()
        with tempfile.TemporaryDirectory() as tmp:
            result = orchestrate.orchestrate(
                gh,
                ref="refs/tags/v1.2.3",
                sha="abc123",
                now=clock.now,
                sleep=clock.sleep,
                timeout=timedelta(minutes=5),
                interval=timedelta(seconds=15),
                skew=timedelta(seconds=5),
                meta_dir=Path(tmp),
            )
        self.assertEqual(
            gh.dispatches,
            [
                (orchestrate.BACKEND_WORKFLOW, "v1.2.3", {"skip_deploy": "true"}),
                (orchestrate.IOS_WORKFLOW, "v1.2.3", {"skip_publish": "true"}),
            ],
        )
        self.assertEqual(result["backend_run_id"], "10")
        self.assertEqual(result["ios_run_id"], "20")
        self.assertEqual(result["version"], "1.2.3")
        self.assertEqual(result["build"], "10020030123")
        self.assertEqual(gh.downloaded, [(20, orchestrate.META_ARTIFACT)])
        self.assertGreaterEqual(clock.current, datetime(2026, 10, 2, 0, 0, 15, tzinfo=timezone.utc))

    def test_failed_check_stops_before_metadata_download(self) -> None:
        created = "2026-10-02T00:00:00Z"
        gh = FakeGh(
            {
                orchestrate.BACKEND_WORKFLOW: [
                    [run(run_id=10, created_at=created, conclusion="failure")]
                ],
                orchestrate.IOS_WORKFLOW: [[run(run_id=20, created_at=created)]],
            },
            "{}",
        )
        clock = FakeClock()
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(orchestrate.ReleaseCheckError) as caught:
                orchestrate.orchestrate(
                    gh,
                    ref="v1.2.3",
                    sha="abc123",
                    now=clock.now,
                    sleep=clock.sleep,
                    timeout=timedelta(minutes=5),
                    interval=timedelta(seconds=1),
                    skew=timedelta(seconds=5),
                    meta_dir=Path(tmp),
                )
        self.assertIn("deploy-backend.yml", str(caught.exception))
        self.assertIn("failure", str(caught.exception))
        self.assertEqual(gh.downloaded, [])

    def test_missing_run_reports_the_workflow(self) -> None:
        gh = FakeGh(
            {
                orchestrate.BACKEND_WORKFLOW: [[]],
                orchestrate.IOS_WORKFLOW: [[]],
            },
            "{}",
        )
        clock = FakeClock()
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(orchestrate.ReleaseCheckError) as caught:
                orchestrate.orchestrate(
                    gh,
                    ref="v1.2.3",
                    sha="abc123",
                    now=clock.now,
                    sleep=clock.sleep,
                    timeout=timedelta(minutes=5),
                    interval=timedelta(seconds=1),
                    skew=timedelta(seconds=5),
                    meta_dir=Path(tmp),
                    discover_within=timedelta(seconds=1),
                )
        self.assertIn("No workflow_dispatch run", str(caught.exception))

    def test_rejects_metadata_for_a_different_version(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "release-meta.json"
            path.write_text(json.dumps({"version": "9.9.9", "build": "1"}), encoding="utf-8")
            with self.assertRaises(orchestrate.ReleaseCheckError):
                orchestrate.parse_release_meta(path, tag="v1.2.3")

    def test_rejects_non_semver_ref(self) -> None:
        with self.assertRaises(orchestrate.ReleaseCheckError) as caught:
            orchestrate.tag_name_from_ref("refs/heads/main")
        self.assertIn("vMAJOR.MINOR.PATCH", str(caught.exception))

    def test_writes_github_output(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "output"
            summary = Path(tmp) / "summary"
            output.write_text("", encoding="utf-8")
            summary.write_text("", encoding="utf-8")
            with mock.patch.dict(
                "os.environ",
                {"GITHUB_OUTPUT": str(output), "GITHUB_STEP_SUMMARY": str(summary)},
            ):
                orchestrate.write_github_output(
                    {
                        "backend_run_id": "10",
                        "ios_run_id": "20",
                        "version": "1.2.3",
                        "build": "42",
                        "tag": "v1.2.3",
                        "backend_url": "https://example.test/runs/10",
                        "ios_url": "https://example.test/runs/20",
                    }
                )
            text = output.read_text(encoding="utf-8")
            self.assertIn("backend_run_id=10\n", text)
            self.assertIn("ios_run_id=20\n", text)
            self.assertIn("version=1.2.3\n", text)
            self.assertIn("build=42\n", text)
            self.assertIn("v1.2.3", summary.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
