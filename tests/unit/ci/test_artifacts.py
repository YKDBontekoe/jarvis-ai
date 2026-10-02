import importlib.util
import io
import json
import tarfile
import tempfile
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('image_manifest', ROOT / 'scripts/ci/image-manifest.py')
manifest_tools = importlib.util.module_from_spec(spec)
spec.loader.exec_module(manifest_tools)


class ArtifactIdentityTests(unittest.TestCase):
    def test_modified_archive_cannot_be_promoted(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / 'image.tar'
            index = json.dumps({'manifests': [{'digest': 'sha256:' + 'a' * 64}]}).encode()
            with tarfile.open(archive, 'w') as tar:
                info = tarfile.TarInfo('index.json')
                info.size = len(index)
                tar.addfile(info, io.BytesIO(index))
            manifest = {'sha': 'b' * 40, 'digest': 'sha256:' + 'a' * 64, 'archiveSha256': '0' * 64}
            with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
                manifest_tools.validate(archive, manifest)
