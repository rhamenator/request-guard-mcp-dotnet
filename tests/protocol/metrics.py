"""Prometheus text helpers shared by running-artifact qualification."""

import re


def normalize_metrics(text: str) -> str:
    """Normalize platform-native Prometheus line endings for exact regex checks."""
    return text.replace('\r\n', '\n').replace('\r', '\n')


def metric_value(metrics: str, family: str, labels: list[tuple[str, str]]) -> float:
    expected = dict(labels)
    normalized = normalize_metrics(metrics)
    for match in re.finditer(
            rf'^{re.escape(family)}\{{([^}}]+)\}} ([0-9]+(?:\.[0-9]+)?)$',
            normalized, re.MULTILINE):
        actual = dict(re.findall(r'(\w+)="([^"]*)"', match.group(1)))
        if actual == expected:
            return float(match.group(2))
    raise AssertionError((family, expected))
