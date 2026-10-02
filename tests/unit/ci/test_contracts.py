import importlib.util
import unittest
from pathlib import Path
spec = importlib.util.spec_from_file_location('contracts', Path(__file__).resolve().parents[3] / 'scripts/ci/check-contracts.py')
contracts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(contracts)


class ContractTests(unittest.TestCase):
    def document(self, schema):
        return {'paths': {'/memory': {'post': {'requestBody': {'content': {'application/json': {'schema': schema}}},
                   'responses': {'200': {'content': {'application/json': {'schema': schema}}}}}}}}

    def test_optional_field_addition_is_compatible(self):
        before = self.document({'type': 'object', 'properties': {'name': {'type': 'string'}}})
        after = self.document({'type': 'object', 'properties': {'name': {'type': 'string'}, 'extra': {'type': 'string'}}})
        self.assertEqual([], contracts.compare(before, after))

    def test_required_input_and_removed_fields_are_rejected(self):
        before = self.document({'type': 'object', 'properties': {'name': {'type': 'string'}}})
        after = self.document({'type': 'object', 'required': ['extra'], 'properties': {'extra': {'type': 'string'}}})
        issues = contracts.compare(before, after)
        self.assertTrue(any('new required input' in issue for issue in issues))
        self.assertTrue(any('property removed' in issue for issue in issues))

    def test_removed_endpoint_is_rejected(self):
        self.assertTrue(contracts.compare(self.document({}), {'paths': {}}))
