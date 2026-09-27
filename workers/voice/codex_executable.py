"""Resolve the Codex CLI, preferring a Settings update installed under CODEX_HOME."""

from __future__ import annotations

import os
import shutil
from collections.abc import Mapping
from pathlib import Path

_STOCK_NAMES = {"codex", "codex.exe", "codex.cmd"}


def resolve_codex_executable(environ: Mapping[str, str] | None = None) -> str:
    env = os.environ if environ is None else environ
    configured = env.get("CODEX_EXECUTABLE_PATH", "codex")
    home = env.get("CODEX_HOME")
    if not home:
        user_home = env.get("HOME") or str(Path.home())
        home = str(Path(user_home) / ".codex")
    managed = Path(home) / "cli" / "bin" / "codex"
    if Path(configured).name in _STOCK_NAMES and managed.is_file() and os.access(managed, os.X_OK):
        return str(managed)
    return shutil.which(configured) or configured
