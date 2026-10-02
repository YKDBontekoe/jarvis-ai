import importlib.util
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]

def load(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ReleaseGateTests(unittest.TestCase):
    def test_ambiguous_or_missing_release_selection_is_rejected(self):
        semver = load('semver_ci', 'scripts/release/semver.py')
        for body in ('', '- [x] **SemVer bump: minor**\n- [x] **SemVer bump: patch**'):
            with self.assertRaises(semver.SemVerError):
                semver.validate_pr_release(body, ['v2.0.0'], ROOT / 'apps/mobile/pubspec.yaml')

    def test_existing_or_older_explicit_tag_is_rejected(self):
        semver = load('semver_ci', 'scripts/release/semver.py')
        for tag in ('v1.9.0', 'v2.0.0'):
            with self.assertRaises(semver.SemVerError):
                semver.validate_pr_release(f'- [x] **SemVer bump: patch**\n**Intended release tag:** `{tag}`',
                                           ['v2.0.0'], ROOT / 'apps/mobile/pubspec.yaml')

    def test_template_has_no_accidental_release_override(self):
        semver = load('semver_ci', 'scripts/release/semver.py')
        body = (ROOT / '.github/pull_request_template.md').read_text()
        self.assertIsNone(semver.parse_intended_release_tag(body))

    def test_gate_detects_omitted_job_and_unpinned_action(self):
        policy = load('policy_ci', 'scripts/ci/check-policy.py')
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            workflow = root / '.github/workflows/ci.yml'
            workflow.parent.mkdir(parents=True)
            # Preserve source layout so only the injected workflow faults are under test.
            (root / 'src').symlink_to(ROOT / 'src', target_is_directory=True)
            workflow.write_text('jobs:\n  bridge:\n    runs-on: ubuntu-latest\n    timeout-minutes: 5\n    steps: [{uses: "actions/checkout@v4"}]\n  required:\n    runs-on: ubuntu-latest\n    timeout-minutes: 5\n    needs: []\n    if: always()\n    steps: [{run: "toJSON(needs)"}]\n')
            issues = policy.errors(root)
            self.assertTrue(any('every verification job' in issue for issue in issues))
            self.assertTrue(any('not pinned' in issue for issue in issues))
