import re
import unittest

from metrics import metric_value, normalize_metrics


class MetricsTests(unittest.TestCase):
    def test_windows_line_endings_support_exact_metric_checks(self):
        windows = (
            '# TYPE mcp_requests_total counter\r\n'
            'mcp_requests_total{tool="health",status="ok"} 3\r\n'
            'mcp_active_connections 0\r\n'
        )

        normalized = normalize_metrics(windows)

        self.assertNotIn('\r', normalized)
        self.assertEqual(
            3,
            metric_value(
                windows,
                'mcp_requests_total',
                [('tool', 'health'), ('status', 'ok')],
            ),
        )
        self.assertRegex(normalized, re.compile(r'^mcp_active_connections 0$', re.MULTILINE))


if __name__ == '__main__':
    unittest.main()
