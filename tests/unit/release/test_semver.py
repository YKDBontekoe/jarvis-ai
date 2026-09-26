#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
SEMVER_PATH = REPO_ROOT / "scripts" / "release" / "semver.py"
PUBSPEC_PATH = REPO_ROOT / "apps" / "mobile" / "pubspec.yaml"


def load_semver():
    spec = importlib.util.spec_from_file_location("semver", SEMVER_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {SEMVER_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


semver = load_semver()


class SemVerTests(unittest.TestCase):
    def test_accepts_valid_core_versions(self) -> None:
        for version in ("0.0.0", "1.0.0", "1.2.3", "10.20.30"):
            self.assertTrue(semver.is_valid_semver(version))
            semver.parse_semver(version)

    def test_rejects_invalid_core_versions(self) -> None:
        for version in ("1", "1.2", "v1.2.3", "01.2.3", "1.2.3.4", "1.2.3-beta"):
            self.assertFalse(semver.is_valid_semver(version))
            with self.assertRaises(semver.SemVerError):
                semver.parse_semver(version)

    def test_parse_semver_tag(self) -> None:
        self.assertEqual(semver.parse_semver_tag("v1.2.3"), "1.2.3")
        with self.assertRaises(semver.SemVerError):
            semver.parse_semver_tag("v1.2")
        with self.assertRaises(semver.SemVerError):
            semver.parse_semver_tag("release-1.2.3")

    def test_resolve_from_tag(self) -> None:
        version, tag, build = semver.resolve_release(
            ref_type="tag",
            ref_name="v2.4.1",
            input_version=None,
            pubspec_path=PUBSPEC_PATH,
            run_number=42,
        )
        self.assertEqual(version, "2.4.1")
        self.assertEqual(tag, "v2.4.1")
        self.assertTrue(build.isdigit())

    def test_bump_helpers(self) -> None:
        self.assertEqual(semver.bump_semver("1.2.3", "patch"), "1.2.4")
        self.assertEqual(semver.bump_semver("1.2.3", "minor"), "1.3.0")
        self.assertEqual(semver.bump_semver("1.2.3", "major"), "2.0.0")

    def test_write_pubspec_version(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            pubspec = Path(tmp) / "pubspec.yaml"
            pubspec.write_text("name: demo\nversion: 0.1.0+1\n", encoding="utf-8")
            semver.write_pubspec_version(pubspec, "1.2.3", "1002003")
            self.assertIn("version: 1.2.3+1002003", pubspec.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
