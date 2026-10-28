# Official Release-Candidate Readiness

## Purpose

This repository publishes official release candidates below 1.0 only after they meet the same
technical, security, compatibility, documentation, artifact and operational evidence standard
expected of a production release. A release candidate is still a prerelease: it does not claim
stable-v1 compatibility or move a `latest` tag.

The solo maintainer performs and records the owner review. Independent human review is welcome but
is not a publication blocker. The readiness record and release notes must state whether independent
human review was performed and must not describe self-review or automation as independent review.

## Owner review packet

Review the following for the exact candidate revision:

1. intended scope, supported platforms, requirements, assumptions and exclusions;
2. architecture, trust boundaries, interfaces and important design decisions;
3. traceability from requirements or objectives to implementation and verification;
4. automated and manual test evidence, coverage claims and known test limitations;
5. security analysis, dependency state, static-analysis results and unresolved alerts;
6. known defects, deferred work, compatibility constraints and residual risks;
7. reproducible build and release instructions plus artifact provenance;
8. native installer clean-install, launch, upgrade, uninstall and residue evidence for every
   supported platform; and
9. the exact source revision and proposed `0.MINOR.PATCH` release version.

## Durable readiness record

Create an issue from the official release-candidate readiness template. Record:

- release version and exact 40-character source revision;
- owner review date and scope;
- verification evidence;
- findings and their dispositions;
- known limitations;
- whether independent human review was performed;
- an `approved`, `approved with limitations` or `not approved` recommendation; and
- any blocking high-severity findings.

The repository owner or an authorized collaborator must post the exact approval comment shown in
the issue template. Close the issue and apply `release-candidate-approved` only after every blocking
high-severity finding is resolved or explicitly dispositioned. The workflow rejects incomplete,
open, mismatched or unapproved records.

## Enforced publication

The `Official Release Candidate` workflow is manually dispatched from `main`. Its in-job gate:

- accepts only a `0.MINOR.PATCH` version matching `Directory.Build.props`;
- requires the current exact `main` commit;
- requires successful exact-candidate build, test, parity, dependency, security, Redis, container,
  four-platform native-bundle and four-platform installer checks;
- validates the closed owner readiness issue and exact approval comment; and
- rejects an existing tag, GitHub release or container version.

After the gate passes, the workflow re-runs the controlled baseline, downloads the exact qualified
artifacts, verifies checksums and reports, rebuilds and scans the exact container, and publishes an
immutable versioned container plus a GitHub prerelease. It does not publish `latest`.

Installer generation and qualification are tracked in issue #27. Because the gate already requires
the four installer checks, official release-candidate publication remains fail closed until those
jobs and artifacts are implemented and pass for the exact revision.

Stable 1.0 publication is outside the current workflow and requires an explicit future policy
decision. Do not create release tags manually.
