#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${repo_root}"
ci_python=python3
if [[ -x .venv-ci/bin/python3 ]]; then ci_python=.venv-ci/bin/python3; fi
"$ci_python" - <<'PY'
import importlib.util
import unittest
from pathlib import Path
suite = unittest.TestSuite()
for path in sorted(Path('tests/unit').rglob('test_*.py')):
    spec = importlib.util.spec_from_file_location('test_' + '_'.join(path.parts[:-1]) + path.stem, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    suite.addTests(unittest.defaultTestLoader.loadTestsFromModule(module))
if not suite.countTestCases():
    raise SystemExit('No Python tests discovered')
result = unittest.TextTestRunner(verbosity=2).run(suite)
raise SystemExit(not result.wasSuccessful())
PY
