import importlib.util
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "release_candidate_gate", ROOT / "scripts/release_candidate_gate.py"
)
assert SPEC and SPEC.loader
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)


SHA = "a" * 40
VERSION = "0.1.0"


def issue():
    fields = {
        "Release version": f"v{VERSION}",
        "Source revision": SHA,
        "Owner review date": "2026-10-27",
        "Scope": "official pre-1.0 release candidate",
        "Verification evidence": "exact-candidate CI and artifact qualification",
        "Findings": "none",
        "Finding dispositions": "none required",
        "Known limitations": "documented in the repository and release notes",
        "Independent human review": "not performed",
        "Recommendation": "approved",
        "Blocking high-severity findings": "none",
    }
    return {
        "state": "closed",
        "labels": [{"name": "release-candidate-approved"}],
        "body": "\n".join(f"{name}: {value}" for name, value in fields.items()),
    }


def comments(association="OWNER"):
    return [{
        "body": f"APPROVE RC v{VERSION} {SHA}",
        "author_association": association,
        "user": {"login": "maintainer"},
    }]


class ReleaseCandidateGateTests(unittest.TestCase):
    def test_happy_path_components(self):
        gate.require_exact_main({"commit": {"sha": SHA}}, SHA, "main", "branch")
        gate.require_checks({"check_runs": [
            {"name": name, "status": "completed", "conclusion": "success"}
            for name in gate.REQUIRED_CHECKS
        ]})
        self.assertEqual(
            "maintainer",
            gate.require_readiness_record(issue(), comments(), VERSION, SHA),
        )

    def test_candidate_must_be_exact_main(self):
        with self.assertRaises(gate.GateError):
            gate.require_exact_main({"commit": {"sha": "b" * 40}}, SHA, "main", "branch")
        with self.assertRaises(gate.GateError):
            gate.require_exact_main({"commit": {"sha": SHA}}, SHA, "feature", "branch")

    def test_every_required_check_must_succeed(self):
        runs = [
            {"name": name, "status": "completed", "conclusion": "success"}
            for name in gate.REQUIRED_CHECKS
        ]
        runs[0]["conclusion"] = "failure"
        with self.assertRaises(gate.GateError):
            gate.require_checks({"check_runs": runs})
        with self.assertRaises(gate.GateError):
            gate.require_checks({"total_count": len(runs) + 1, "check_runs": runs})
        runs[0]["conclusion"] = "success"
        runs[0]["status"] = "in_progress"
        with self.assertRaises(gate.GateError):
            gate.require_checks({"check_runs": runs})

    def test_latest_check_attempt_controls_retry_result(self):
        runs = [
            {"id": index, "name": name, "status": "completed", "conclusion": "success"}
            for index, name in enumerate(gate.REQUIRED_CHECKS, 100)
        ]
        retried = runs[0]["name"]
        runs.append({"id": 1, "name": retried, "status": "completed", "conclusion": "failure"})
        gate.require_checks({"check_runs": runs})
        runs.append({"id": 1000, "name": retried, "status": "in_progress", "conclusion": None})
        with self.assertRaises(gate.GateError):
            gate.require_checks({"check_runs": runs})

    def test_readiness_record_rejects_untrusted_approval_or_unresolved_finding(self):
        with self.assertRaises(gate.GateError):
            gate.require_readiness_record(issue(), comments("NONE"), VERSION, SHA)
        record = issue()
        record["body"] = record["body"].replace(
            "Blocking high-severity findings: none", "Blocking high-severity findings: one"
        )
        with self.assertRaises(gate.GateError):
            gate.require_readiness_record(record, comments(), VERSION, SHA)

    def test_readiness_record_requires_independent_review_disclosure(self):
        record = issue()
        record["body"] = record["body"].replace("Independent human review: not performed\n", "")
        with self.assertRaises(gate.GateError):
            gate.require_readiness_record(record, comments(), VERSION, SHA)
        record = issue()
        record["body"] = record["body"].replace(
            "Independent human review: not performed",
            "Independent human review: not performed | completed: URL_OR_ISSUE",
        )
        with self.assertRaises(gate.GateError):
            gate.require_readiness_record(record, comments(), VERSION, SHA)

    def test_readiness_record_rejects_template_placeholders(self):
        record = issue()
        record["body"] = record["body"].replace(
            "Verification evidence: exact-candidate CI and artifact qualification",
            "Verification evidence: REQUIRED",
        )
        with self.assertRaises(gate.GateError):
            gate.require_readiness_record(record, comments(), VERSION, SHA)

    def test_version_is_pre_one_and_matches_source(self):
        with tempfile.TemporaryDirectory() as directory:
            props = Path(directory) / "Directory.Build.props"
            props.write_text(
                "<Project><PropertyGroup><VersionPrefix>0.1.0</VersionPrefix></PropertyGroup></Project>"
            )
            gate.require_release_candidate_version(VERSION, props)
            for invalid in (
                "v0.1.0", "0.1.0-rc.1", "0.1", "0.0.0", "0.01.0", "1.0.0", "1.2.3"
            ):
                with self.subTest(invalid=invalid), self.assertRaises(gate.GateError):
                    gate.require_release_candidate_version(invalid, props)


if __name__ == "__main__":
    unittest.main()
