from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from codex_executable import resolve_codex_executable


class CodexExecutableTests(unittest.TestCase):
    def test_prefers_managed_install_for_the_stock_cli(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            managed = Path(tmp) / "cli" / "bin" / "codex"
            managed.parent.mkdir(parents=True)
            managed.write_text("#!/bin/sh\n", encoding="utf-8")
            managed.chmod(0o755)
            resolved = resolve_codex_executable({
                "CODEX_HOME": tmp,
                "CODEX_EXECUTABLE_PATH": "codex",
            })
            self.assertEqual(resolved, str(managed))

    def test_keeps_a_custom_executable(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            managed = Path(tmp) / "cli" / "bin" / "codex"
            managed.parent.mkdir(parents=True)
            managed.write_text("#!/bin/sh\n", encoding="utf-8")
            managed.chmod(0o755)
            custom = str(Path(tmp) / "fake_codex_app_server.mjs")
            resolved = resolve_codex_executable({
                "CODEX_HOME": tmp,
                "CODEX_EXECUTABLE_PATH": custom,
            })
            self.assertEqual(resolved, custom)


if __name__ == "__main__":
    unittest.main()
