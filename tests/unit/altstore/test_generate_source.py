#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
GENERATOR_PATH = REPO_ROOT / "scripts" / "altstore" / "generate_source.py"
PUBSPEC_PATH = REPO_ROOT / "apps" / "mobile" / "pubspec.yaml"


def load_generator():
    spec = importlib.util.spec_from_file_location("generate_source", GENERATOR_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {GENERATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


gen = load_generator()


def write_dummy_ipa(directory: Path, payload: bytes = b"jarvis-ipa-fixture") -> Path:
    ipa_path = directory / "Jarvis.ipa"
    with zipfile.ZipFile(ipa_path, "w") as archive:
        archive.writestr("Payload/Runner.app/Info.plist", payload)
    return ipa_path


class GenerateSourceTests(unittest.TestCase):
    def test_parse_pubspec_version(self) -> None:
        version, build = gen.parse_pubspec_version(PUBSPEC_PATH)
        self.assertEqual(version, "1.0.0")
        self.assertEqual(build, "1")

    def test_builds_source_from_ipa(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            ipa = write_dummy_ipa(root)
            source = gen.build_source(
                ipa_path=ipa,
                download_url="https://example.com/Jarvis.ipa",
                icon_url="https://example.com/icon.png",
                website="https://github.com/example/jarvis-ai",
                version="1.0.0",
                build_version="1",
                date="2026-04-11T00:00:00Z",
                changelog="Initial sideload build",
                previous=None,
            )
            self.assertEqual(source["identifier"], "com.example.jarvis_mobile.source")
            app = source["apps"][0]
            self.assertEqual(app["bundleIdentifier"], "com.example.jarvis_mobile")
            self.assertEqual(app["appPermissions"]["entitlements"], [])
            self.assertIn("NSMicrophoneUsageDescription", app["appPermissions"]["privacy"])
            version = app["versions"][0]
            self.assertEqual(version["version"], "1.0.0")
            self.assertEqual(version["buildVersion"], "1")
            self.assertEqual(version["minOSVersion"], "15.0")
            self.assertEqual(version["size"], ipa.stat().st_size)
            self.assertEqual(version["sha256"], gen.sha256_file(ipa))
            self.assertEqual(version["downloadURL"], "https://example.com/Jarvis.ipa")

    def test_prepends_and_replaces_versions(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            ipa = write_dummy_ipa(root, b"first")
            first = gen.build_source(
                ipa_path=ipa,
                download_url="https://example.com/v1.ipa",
                icon_url="https://example.com/icon.png",
                website="https://github.com/example/jarvis-ai",
                version="1.0.0",
                build_version="1",
                date="2026-04-11T00:00:00Z",
                changelog="v1",
                previous=None,
            )
            second_ipa = write_dummy_ipa(root, b"second")
            second = gen.build_source(
                ipa_path=second_ipa,
                download_url="https://example.com/v2.ipa",
                icon_url="https://example.com/icon.png",
                website="https://github.com/example/jarvis-ai",
                version="1.1.0",
                build_version="2",
                date="2026-05-01T00:00:00Z",
                changelog="v2",
                previous=first,
            )
            versions = second["apps"][0]["versions"]
            self.assertEqual([entry["version"] for entry in versions], ["1.1.0", "1.0.0"])

            replaced_ipa = write_dummy_ipa(root, b"second-replaced")
            replaced = gen.build_source(
                ipa_path=replaced_ipa,
                download_url="https://example.com/v2-rebuild.ipa",
                icon_url="https://example.com/icon.png",
                website="https://github.com/example/jarvis-ai",
                version="1.1.0",
                build_version="2",
                date="2026-05-02T00:00:00Z",
                changelog="v2 rebuild",
                previous=second,
            )
            versions = replaced["apps"][0]["versions"]
            self.assertEqual([entry["version"] for entry in versions], ["1.1.0", "1.0.0"])
            self.assertEqual(versions[0]["downloadURL"], "https://example.com/v2-rebuild.ipa")
            self.assertEqual(versions[0]["sha256"], gen.sha256_file(replaced_ipa))

    def test_cli_writes_output(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            ipa = write_dummy_ipa(root)
            output = root / "source.json"
            rc = gen.main(
                [
                    "--ipa",
                    str(ipa),
                    "--download-url",
                    "https://example.com/Jarvis.ipa",
                    "--icon-url",
                    "https://example.com/icon.png",
                    "--website",
                    "https://github.com/example/jarvis-ai",
                    "--version",
                    "1.0.0",
                    "--build-version",
                    "1",
                    "--date",
                    "2026-04-11T00:00:00Z",
                    "--output",
                    str(output),
                ]
            )
            self.assertEqual(rc, 0)
            written = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual(written["apps"][0]["versions"][0]["version"], "1.0.0")


if __name__ == "__main__":
    unittest.main()
