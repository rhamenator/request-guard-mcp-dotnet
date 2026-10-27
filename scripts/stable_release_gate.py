#!/usr/bin/env python3
"""Fail-closed validation for the manually approved stable-release job."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path


STABLE_VERSION = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+")
REQUIRED_CHECKS = {
    "assurance-policy",
    "Formatting",
    "Build",
    "Tests",
    "Dependency Audit",
    "Rust Differential Parity",
    "Current Rust Classifier (f76ff9d2558fac705f091d9b858d3bc180b4f0bd)",
    "Current Rust Classifier (6dc433d9cc9cf1805e5074e8a8bf95a65dddfc30)",
    "Real Redis pause, outage and recovery",
    "Native bundle (linux-x64)",
    "Native bundle (win-x64)",
    "Native bundle (osx-x64)",
    "Native bundle (osx-arm64)",
    "CodeQL security-extended (actions)",
    "CodeQL security-extended (csharp)",
    "codeql (actions)",
    "codeql (csharp)",
    "Container vulnerability scan",
}
REVIEW_FIELDS = (
    "Reviewer qualifications",
    "Review date",
    "Scope",
    "Findings",
    "Finding dispositions",
    "Review limitations",
    "Recommendation",
    "Blocking high-severity findings",
)


class GateError(RuntimeError):
    pass


def api_get(repository: str, path: str, token: str) -> object:
    request = urllib.request.Request(
        f"https://api.github.com/repos/{repository}/{path}",
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "request-guard-stable-release-gate",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except (urllib.error.URLError, json.JSONDecodeError) as error:
        raise GateError(f"GitHub API request failed for {path}: {error}") from error


def require_stable_version(version: str, props_path: Path) -> None:
    if STABLE_VERSION.fullmatch(version) is None:
        raise GateError("stable version must be exactly MAJOR.MINOR.PATCH")
    root = ET.parse(props_path).getroot()
    configured = root.findtext(".//VersionPrefix")
    if configured != version:
        raise GateError(f"version {version} does not match VersionPrefix {configured!r}")


def required_reviewers(environment: dict[str, object]) -> set[str]:
    if environment.get("can_admins_bypass") is not False:
        raise GateError("stable-release must disable administrator protection-rule bypass")
    policy = environment.get("deployment_branch_policy")
    if not isinstance(policy, dict) or policy.get("protected_branches") is not True:
        raise GateError("stable-release must allow only protected branches")
    rules = environment.get("protection_rules")
    if not isinstance(rules, list):
        raise GateError("stable-release has no protection rules")
    reviewer_rule = next(
        (rule for rule in rules if isinstance(rule, dict) and rule.get("type") == "required_reviewers"),
        None,
    )
    if not isinstance(reviewer_rule, dict):
        raise GateError("stable-release has no required-reviewer rule")
    if reviewer_rule.get("prevent_self_review") is not True:
        raise GateError("stable-release must prevent self-review")
    reviewers = reviewer_rule.get("reviewers")
    if not isinstance(reviewers, list):
        raise GateError("stable-release required-reviewer rule is malformed")
    users = {
        str(entry["reviewer"]["login"]).casefold()
        for entry in reviewers
        if isinstance(entry, dict)
        and entry.get("type") == "User"
        and isinstance(entry.get("reviewer"), dict)
        and entry["reviewer"].get("login")
    }
    if not users:
        raise GateError("stable-release needs at least one named user reviewer")
    return users


def require_exact_main(main: dict[str, object], sha: str, ref_name: str, ref_type: str) -> None:
    if ref_name != "main" or ref_type != "branch":
        raise GateError("stable release must be dispatched from the main branch")
    commit = main.get("commit")
    if not isinstance(commit, dict) or commit.get("sha") != sha:
        raise GateError("candidate is not the current exact main commit")


def require_checks(checks: dict[str, object]) -> None:
    runs = checks.get("check_runs")
    if not isinstance(runs, list):
        raise GateError("check-runs response is missing check_runs")
    if checks.get("total_count", len(runs)) > len(runs):
        raise GateError("check-runs response was truncated")
    results: dict[str, list[tuple[object, object]]] = {}
    for run in runs:
        if isinstance(run, dict) and isinstance(run.get("name"), str):
            results.setdefault(run["name"], []).append((run.get("status"), run.get("conclusion")))
    missing = sorted(REQUIRED_CHECKS - results.keys())
    failed = sorted(
        name for name in REQUIRED_CHECKS
        if any(status != "completed" or conclusion != "success" for status, conclusion in results.get(name, []))
    )
    if missing:
        raise GateError(f"required exact-candidate checks are missing: {', '.join(missing)}")
    if failed:
        raise GateError(f"required exact-candidate checks did not succeed: {', '.join(failed)}")


def _field(body: str, name: str) -> str | None:
    match = re.search(rf"(?im)^{re.escape(name)}:\s*(\S.*)$", body)
    return match.group(1).strip() if match else None


def require_review_record(
    issue: dict[str, object], comments: list[object], reviewers: set[str],
    version: str, sha: str, actor: str,
) -> str:
    if issue.get("state") != "closed":
        raise GateError("release-readiness issue must be closed")
    labels = issue.get("labels")
    if not isinstance(labels, list):
        raise GateError("release-readiness issue labels are malformed")
    label_names = {
        entry.get("name") for entry in labels if isinstance(entry, dict)
    }
    if "release-review-approved" not in label_names:
        raise GateError("release-readiness issue lacks release-review-approved label")
    body = issue.get("body")
    if not isinstance(body, str):
        raise GateError("release-readiness issue has no body")
    if _field(body, "Release version") != f"v{version}":
        raise GateError("review record release version does not match")
    if _field(body, "Source revision") != sha:
        raise GateError("review record source revision does not match")
    missing_fields = [name for name in REVIEW_FIELDS if not _field(body, name)]
    if missing_fields:
        raise GateError(f"review record fields are missing: {', '.join(missing_fields)}")
    recommendation = (_field(body, "Recommendation") or "").casefold()
    if recommendation not in {"approved", "approved with limitations"}:
        raise GateError("review recommendation is not approved")
    if (_field(body, "Blocking high-severity findings") or "").casefold() != "none":
        raise GateError("review record has unresolved blocking high-severity findings")

    approval = f"APPROVE STABLE v{version} {sha}"
    for comment in comments:
        if not isinstance(comment, dict) or not isinstance(comment.get("body"), str):
            continue
        if comment["body"].strip() != approval:
            continue
        user = comment.get("user")
        login = user.get("login") if isinstance(user, dict) else None
        if not isinstance(login, str) or login.casefold() not in reviewers:
            continue
        if login.casefold() == actor.casefold():
            continue
        if comment.get("author_association") not in {"OWNER", "MEMBER", "COLLABORATOR"}:
            continue
        return login
    raise GateError("no independent configured reviewer posted the exact approval comment")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", required=True)
    parser.add_argument("--sha", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--review-issue", required=True, type=int)
    parser.add_argument("--actor", required=True)
    parser.add_argument("--ref-name", required=True)
    parser.add_argument("--ref-type", required=True)
    parser.add_argument("--props", type=Path, default=Path("Directory.Build.props"))
    args = parser.parse_args()
    token = os.environ.get("GH_TOKEN")
    if not token:
        parser.error("GH_TOKEN is required")
    try:
        require_stable_version(args.version, args.props)
        environment = api_get(args.repository, "environments/stable-release", token)
        reviewers = required_reviewers(environment)  # type: ignore[arg-type]
        main_branch = api_get(args.repository, "branches/main", token)
        require_exact_main(main_branch, args.sha, args.ref_name, args.ref_type)  # type: ignore[arg-type]
        checks = api_get(args.repository, f"commits/{args.sha}/check-runs?per_page=100", token)
        require_checks(checks)  # type: ignore[arg-type]
        issue = api_get(args.repository, f"issues/{args.review_issue}", token)
        comments = api_get(args.repository, f"issues/{args.review_issue}/comments?per_page=100", token)
        if not isinstance(comments, list):
            raise GateError("issue comments response is not a list")
        reviewer = require_review_record(
            issue, comments, reviewers, args.version, args.sha, args.actor  # type: ignore[arg-type]
        )
    except (GateError, ET.ParseError, OSError) as error:
        print(f"stable release gate failed: {error}", file=sys.stderr)
        return 1
    print(f"stable release gate passed for v{args.version} at {args.sha}; reviewer={reviewer}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
