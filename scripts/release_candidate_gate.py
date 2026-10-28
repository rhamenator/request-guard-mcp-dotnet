#!/usr/bin/env python3
"""Fail-closed validation for an official pre-1.0 release candidate."""

from __future__ import annotations

import argparse
import datetime
import json
import os
import re
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path


RELEASE_CANDIDATE_VERSION = re.compile(r"0\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)")
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
    "Installer (linux-x64)",
    "Installer (win-x64)",
    "Installer (osx-x64)",
    "Installer (osx-arm64)",
    "CodeQL security-extended (actions)",
    "CodeQL security-extended (csharp)",
    "codeql (actions)",
    "codeql (csharp)",
    "Container vulnerability scan",
}
READINESS_FIELDS = (
    "Owner review date",
    "Scope",
    "Verification evidence",
    "Findings",
    "Finding dispositions",
    "Known limitations",
    "Independent human review",
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
            "User-Agent": "request-guard-release-candidate-gate",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except (urllib.error.URLError, json.JSONDecodeError) as error:
        raise GateError(f"GitHub API request failed for {path}: {error}") from error


def require_release_candidate_version(version: str, props_path: Path) -> None:
    match = RELEASE_CANDIDATE_VERSION.fullmatch(version)
    if match is None:
        raise GateError("official release-candidate version must be exactly 0.MINOR.PATCH")
    if match.group(1) == "0" and match.group(2) == "0":
        raise GateError("official release-candidate version must be greater than 0.0.0")
    root = ET.parse(props_path).getroot()
    configured = root.findtext(".//VersionPrefix")
    if configured != version:
        raise GateError(f"version {version} does not match VersionPrefix {configured!r}")


def require_exact_main(main: dict[str, object], sha: str, ref_name: str, ref_type: str) -> None:
    if ref_name != "main" or ref_type != "branch":
        raise GateError("release candidate must be dispatched from the main branch")
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


def require_readiness_record(
    issue: dict[str, object], comments: list[object], version: str, sha: str,
) -> str:
    if issue.get("state") != "closed":
        raise GateError("release-readiness issue must be closed")
    labels = issue.get("labels")
    if not isinstance(labels, list):
        raise GateError("release-readiness issue labels are malformed")
    label_names = {
        entry.get("name") for entry in labels if isinstance(entry, dict)
    }
    if "release-candidate-approved" not in label_names:
        raise GateError("readiness issue lacks release-candidate-approved label")
    body = issue.get("body")
    if not isinstance(body, str):
        raise GateError("release-readiness issue has no body")
    if _field(body, "Release version") != f"v{version}":
        raise GateError("readiness record release version does not match")
    if _field(body, "Source revision") != sha:
        raise GateError("readiness record source revision does not match")
    missing_fields = [
        name for name in READINESS_FIELDS
        if not _field(body, name)
        or (_field(body, name) or "").casefold() in {"required", "yyyy-mm-dd"}
    ]
    if missing_fields:
        raise GateError(f"readiness record fields are missing: {', '.join(missing_fields)}")
    recommendation = (_field(body, "Recommendation") or "").casefold()
    if recommendation not in {"approved", "approved with limitations"}:
        raise GateError("owner recommendation is not approved")
    if (_field(body, "Blocking high-severity findings") or "").casefold() != "none":
        raise GateError("readiness record has unresolved blocking high-severity findings")
    review_date = _field(body, "Owner review date") or ""
    try:
        datetime.date.fromisoformat(review_date)
    except ValueError as error:
        raise GateError("owner review date must be an ISO date (YYYY-MM-DD)") from error
    independent_review = (_field(body, "Independent human review") or "").casefold()
    if independent_review != "not performed" and not independent_review.startswith("completed: "):
        raise GateError(
            "independent human review must be 'not performed' or 'completed: RECORD'"
        )

    approval = f"APPROVE RC v{version} {sha}"
    for comment in comments:
        if not isinstance(comment, dict) or not isinstance(comment.get("body"), str):
            continue
        if comment["body"].strip() != approval:
            continue
        user = comment.get("user")
        login = user.get("login") if isinstance(user, dict) else None
        if not isinstance(login, str):
            continue
        if comment.get("author_association") not in {"OWNER", "MEMBER", "COLLABORATOR"}:
            continue
        return login
    raise GateError("no repository owner or collaborator posted the exact RC approval comment")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", required=True)
    parser.add_argument("--sha", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--readiness-issue", required=True, type=int)
    parser.add_argument("--ref-name", required=True)
    parser.add_argument("--ref-type", required=True)
    parser.add_argument("--props", type=Path, default=Path("Directory.Build.props"))
    args = parser.parse_args()
    token = os.environ.get("GH_TOKEN")
    if not token:
        parser.error("GH_TOKEN is required")
    try:
        require_release_candidate_version(args.version, args.props)
        main_branch = api_get(args.repository, "branches/main", token)
        require_exact_main(main_branch, args.sha, args.ref_name, args.ref_type)  # type: ignore[arg-type]
        checks = api_get(args.repository, f"commits/{args.sha}/check-runs?per_page=100", token)
        require_checks(checks)  # type: ignore[arg-type]
        issue = api_get(args.repository, f"issues/{args.readiness_issue}", token)
        comments = api_get(args.repository, f"issues/{args.readiness_issue}/comments?per_page=100", token)
        if not isinstance(comments, list):
            raise GateError("issue comments response is not a list")
        approver = require_readiness_record(
            issue, comments, args.version, args.sha  # type: ignore[arg-type]
        )
    except (GateError, ET.ParseError, OSError) as error:
        print(f"release candidate gate failed: {error}", file=sys.stderr)
        return 1
    print(
        f"release candidate gate passed for v{args.version} at {args.sha}; "
        f"owner_approver={approver}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
