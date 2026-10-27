# Release-Readiness Human Review

## Purpose

Independent human review is a release-readiness activity for this solo-maintained repository, not a required approval on every development pull request.

Complete this review before either:

- the first stable release;
- declaring the project complete for its intended scope; or
- representing the software as independently reviewed for downstream or production use.

Until then, development records may cite self-review and automated verification but must state that independent human review is pending.

## Review packet

Provide the reviewer with:

1. the intended scope, requirements, assumptions, and documented exclusions;
2. the architecture, trust boundaries, interfaces, and important design decisions;
3. traceability from requirements or objectives to implementation and verification;
4. automated and manual test evidence, coverage claims, and known test limitations;
5. security analysis, dependency state, static-analysis results, and unresolved alerts;
6. known defects, deferred work, compatibility constraints, and residual risks;
7. reproducible build and release instructions plus artifact provenance; and
8. the exact source revision and release candidate being reviewed.

## Review record

Archive the following in a release issue, release notes, or another durable repository record:

- reviewer name and relevant qualifications;
- review date, scope, source revision, and release candidate;
- findings and their severity;
- resolution or explicit risk acceptance for each finding;
- limitations of the review; and
- the final recommendation: approved, approved with limitations, or not approved.

Do not claim independent review while blocking high-severity findings remain unresolved or without an explicit, documented risk disposition.

## Enforced stable publication setup

The repository owner must create a GitHub Actions environment named `stable-release` before any
stable run. Configure it with all of the following:

1. required reviewers containing at least one named **user** with repository read access;
2. **Prevent self-review** enabled;
3. **Allow administrators to bypass configured protection rules** disabled; and
4. deployment branches restricted to protected branches (the stable workflow additionally
   requires the exact current `main` commit).

An absent or weaker environment can let GitHub start the job, but `stable_release_gate.py` reads
the environment through the GitHub API and fails before building or publishing. Team-only reviewer
rules are not accepted because the durable issue approval must be attributable to a named account.
The workflow token needs `actions:read`, `contents:write`, `packages:write`, `checks:read`,
`deployments:read`, and `issues:read`; no long-lived registry password is required.

Create the review issue with the **Stable release readiness review** template. Fill every field,
including the exact 40-character source revision and stable version. A configured environment
reviewer who did not start the workflow must post exactly:

```text
APPROVE STABLE vX.Y.Z FULL_40_CHARACTER_SHA
```

Resolve or disposition every finding, set `Blocking high-severity findings: none`, select an
approved recommendation, add the `release-review-approved` label, and close the issue. Dispatch
**Stable Release** from `main` with the version (without `v`) and issue number. GitHub first asks
an environment reviewer to approve the job. The in-job gate then independently revalidates the
environment, current main SHA, `VersionPrefix`, exact-candidate check runs, issue fields, reviewer
identity and approval comment. It rejects prerelease versions and existing tags/releases/container
versions. Do not create a tag manually; the workflow creates it only after all local build,
checksum, container smoke and vulnerability gates pass. Dispatch within the native workflow's
seven-day retention window: stable publication downloads the exact four matching-platform
qualified archives and verifies their source, RID, report results and checksums instead of
rebuilding untested replacement archives.
