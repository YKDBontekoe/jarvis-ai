#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import subprocess
import tempfile
import threading
import unittest
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
SCRIPT = REPO_ROOT / "scripts" / "ci" / "web_release.py"
CHECK = REPO_ROOT / "scripts" / "deploy" / "check-web-client.mjs"


def _load():
    spec = importlib.util.spec_from_file_location("web_release", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def _fake_build(root: Path, revision: str, *, href: str | None = None) -> None:
    root.mkdir(parents=True)
    (root / ".jarvis-web-revision").write_text(revision + "\n", encoding="utf-8")
    base = href or f"/r/{revision}/"
    (root / "index.html").write_text(
        f'<html><head><base href="{base}"></head>'
        '<body><script src="flutter_bootstrap.js" async></script></body></html>\n',
        encoding="utf-8",
    )
    (root / "main.dart.js").write_text("console.log('jarvis');\n", encoding="utf-8")
    (root / "flutter_bootstrap.js").write_text("/* bootstrap */\n", encoding="utf-8")
    (root / "main.dart.js.map").write_text("{}\n", encoding="utf-8")
    assets = root / "assets"
    assets.mkdir()
    (assets / "AssetManifest.bin").write_bytes(b"manifest")


class _ReleaseHandler(SimpleHTTPRequestHandler):
    def end_headers(self) -> None:
        if self.path in {"/", "/index.html"}:
            self.send_header("Cache-Control", "no-cache")
        super().end_headers()

    def log_message(self, format: str, *args) -> None:
        del format, args


class _Serving:
    def __init__(self, directory: Path) -> None:
        self._directory = directory
        self._server: ThreadingHTTPServer | None = None
        self._thread: threading.Thread | None = None

    def __enter__(self) -> str:
        directory = self._directory

        def handler(*args, **kwargs):
            return _ReleaseHandler(*args, directory=str(directory), **kwargs)

        self._server = ThreadingHTTPServer(("127.0.0.1", 0), handler)
        self._thread = threading.Thread(target=self._server.serve_forever, daemon=True)
        self._thread.start()
        host, port = self._server.server_address
        return f"http://{host}:{port}"

    def __exit__(self, exc_type, exc, tb) -> None:
        assert self._server is not None
        assert self._thread is not None
        self._server.shutdown()
        self._thread.join(timeout=5)
        self._server.server_close()


class WebReleaseTests(unittest.TestCase):
    def test_revision_prefers_an_explicit_value(self) -> None:
        module = _load()
        self.assertEqual(
            module.resolve_revision("abc1234", "1234567890abcdef", "fedcba9876543210"),
            "abc1234",
        )

    def test_revision_rejects_path_traversal(self) -> None:
        module = _load()
        with self.assertRaises(ValueError):
            module.validate_revision("../secret")
        with self.assertRaises(ValueError):
            module.validate_revision("short")

    def test_stage_publishes_one_revision_and_an_uncached_homepage(self) -> None:
        module = _load()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source, destination = root / "source", root / "dest"
            revision = "2feff8565a92c3ea6cafc58ec647cafd22292b31"
            _fake_build(source, revision)
            self.assertEqual(module.stage_web_release(source, destination), revision)
            homepage = (destination / "index.html").read_text(encoding="utf-8")
            self.assertIn(f'<base href="/r/{revision}/">', homepage)
            release = destination / "r" / revision
            self.assertTrue((release / "main.dart.js").is_file())
            self.assertTrue((release / "flutter_bootstrap.js").is_file())
            self.assertTrue((release / "assets" / "AssetManifest.bin").is_file())
            self.assertFalse((release / "main.dart.js.map").exists())
            self.assertFalse((release / ".jarvis-web-revision").exists())
            self.assertFalse((destination / "main.dart.js").exists())
            extracted = subprocess.run(
                [
                    "sed",
                    "-n",
                    r's/.*<base href="\(\/r\/[A-Za-z0-9._-]\{7,64\}\/\)".*/\1/p',
                    str(destination / "index.html"),
                ],
                check=True,
                capture_output=True,
                text=True,
            )
            self.assertEqual(extracted.stdout.strip(), f"/r/{revision}/")

    def test_stage_rejects_a_homepage_for_a_different_revision(self) -> None:
        module = _load()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source, destination = root / "source", root / "dest"
            _fake_build(source, "abc1234567890", href="/")
            with self.assertRaises(ValueError):
                module.stage_web_release(source, destination)

    def test_live_check_accepts_a_versioned_release(self) -> None:
        module = _load()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source, destination = root / "source", root / "dest"
            revision = "abc1234567890"
            _fake_build(source, revision)
            module.stage_web_release(source, destination)
            with _Serving(destination) as origin:
                completed = subprocess.run(
                    ["node", str(CHECK), origin],
                    check=False,
                    capture_output=True,
                    text=True,
                )
            self.assertEqual(completed.returncode, 0, completed.stderr)

    def test_live_check_rejects_the_stable_bundle_url(self) -> None:
        module = _load()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source, destination = root / "source", root / "dest"
            revision = "abc1234567890"
            _fake_build(source, revision)
            module.stage_web_release(source, destination)
            (destination / "main.dart.js").write_text("stale\n", encoding="utf-8")
            with _Serving(destination) as origin:
                completed = subprocess.run(
                    ["node", str(CHECK), origin],
                    check=False,
                    capture_output=True,
                    text=True,
                )
            self.assertNotEqual(completed.returncode, 0)
            self.assertIn("Stable web bundle URL is still published", completed.stderr)

    def test_image_build_and_deploy_require_the_versioned_bundle(self) -> None:
        dockerfile = (REPO_ROOT / "infra" / "compose" / "Dockerfile").read_text(encoding="utf-8")
        deploy = (REPO_ROOT / "scripts" / "deploy" / "remote-up.sh").read_text(encoding="utf-8")
        self.assertIn("check-web-client.mjs", deploy)
        self.assertIn("/r/", dockerfile)
        self.assertNotIn('"/source/infra/web/dist/main.dart.js"', dockerfile)


if __name__ == "__main__":
    unittest.main()
