import unittest
from contract import normalize


class EnvelopeMigrationTests(unittest.TestCase):
    def test_historical_null_fields_are_an_explicit_baseline_exception(self):
        legacy = {'jsonrpc': '2.0', 'id': 1, 'result': None,
                  'error': {'code': -400, 'message': 'INVALID_REQUEST', 'data': None}}
        current = {'jsonrpc': '2.0', 'id': 1,
                   'error': {'code': -400, 'message': 'INVALID_REQUEST'}}
        self.assertEqual(normalize(legacy, legacy_envelope=True), normalize(current))
        with self.assertRaises(AssertionError):
            normalize(legacy)

    def test_neither_nonnull_branch_can_be_normalized_away(self):
        invalid = {'jsonrpc': '2.0', 'id': 1, 'result': {}, 'error': {'code': -1}}
        for legacy in (False, True):
            with self.assertRaises(AssertionError):
                normalize(invalid, legacy_envelope=legacy)

    def test_application_null_fields_are_preserved(self):
        value = {'jsonrpc': '2.0', 'id': 1, 'result': {'error': None, 'value': None}}
        self.assertEqual(normalize(value), value)
