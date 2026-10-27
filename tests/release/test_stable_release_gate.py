import importlib.util
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("stable_release_gate", ROOT / "scripts/stable_release_gate.py")
assert SPEC and SPEC.loader
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)


SHA = "a" * 40
VERSION = "1.2.3"


def environment(**overrides):
    value = {
        "can_admins_bypass": False,
        "deployment_branch_policy": {"protected_branches": True, "custom_branch_policies": False},
        "protection_rules": [{
            "type": "required_reviewers",
            "prevent_self_review": True,
            "reviewers": [{"type": "User", "reviewer": {"login": "reviewer"}}],
        }],
    }
    value.update(overrides)
    return value


def issue():
    fields = {
        "Release version": f"v{VERSION}",
        "Source revision": SHA,
        "Reviewer qualifications": "security and release engineering",
        "Review date": "2026-10-27",
        "Scope": "stable candidate",
        "Findings": "none",
        "Finding dispositions": "none required",
        "Review limitations": "documented",
        "Recommendation": "approved",
        "Blocking high-severity findings": "none",
    }
    return {
        "state": "closed",
        "labels": [{"name": "release-review-approved"}],
        "body": "\n".join(f"{name}: {value}" for name, value in fields.items()),
    }


def comments():
    return [{
        "body": f"APPROVE STABLE v{VERSION} {SHA}",
        "author_association": "COLLABORATOR",
        "user": {"login": "reviewer"},
    }]


class StableReleaseGateTests(unittest.TestCase):
    def test_happy_path_components(self):
        self.assertEqual({"reviewer"}, gate.required_reviewers(environment()))
        gate.require_exact_main({"commit": {"sha": SHA}}, SHA, "main", "branch")
        gate.require_checks({"check_runs": [
            {"name": name, "status": "completed", "conclusion": "success"}
            for name in gate.REQUIRED_CHECKS
        ]})
        self.assertEqual(
            "reviewer",
            gate.require_review_record(issue(), comments(), {"reviewer"}, VERSION, SHA, "maintainer"),
        )

    def test_environment_fails_closed(self):
        cases = [
            environment(can_admins_bypass=True),
            environment(deployment_branch_policy={"protected_branches": False}),
            environment(protection_rules=[]),
            environment(protection_rules=[{
                "type": "required_reviewers", "prevent_self_review": False,
                "reviewers": [{"type": "User", "reviewer": {"login": "reviewer"}}],
            }]),
        ]
        for value in cases:
            with self.subTest(value=value), self.assertRaises(gate.GateError):
                gate.required_reviewers(value)
        malformed = environment(protection_rules=[{
            "type": "required_reviewers", "prevent_self_review": True, "reviewers": None,
        }])
        with self.assertRaises(gate.GateError):
            gate.required_reviewers(malformed)

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

    def test_review_record_rejects_self_or_unresolved_finding(self):
        with self.assertRaises(gate.GateError):
            gate.require_review_record(issue(), comments(), {"reviewer"}, VERSION, SHA, "reviewer")
        record = issue()
        record["body"] = record["body"].replace(
            "Blocking high-severity findings: none", "Blocking high-severity findings: one"
        )
        with self.assertRaises(gate.GateError):
            gate.require_review_record(record, comments(), {"reviewer"}, VERSION, SHA, "maintainer")

    def test_version_is_stable_and_matches_source(self):
        with tempfile.TemporaryDirectory() as directory:
            props = Path(directory) / "Directory.Build.props"
            props.write_text("<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>")
            gate.require_stable_version(VERSION, props)
            for invalid in ("v1.2.3", "1.2.3-rc.1", "1.2"):
                with self.subTest(invalid=invalid), self.assertRaises(gate.GateError):
                    gate.require_stable_version(invalid, props)


if __name__ == "__main__":
    unittest.main()
