# Rust / .NET parity maintenance matrix

Assessment date: 2026-10-22. This is a source audit and work queue, not a claim of
current full parity or independent verification. No stable .NET release is published.

## Controlled references

- .NET starting main: `76c4de4aab291ffc72b049ab63930ee02b75094a`.
- Existing differential baseline: `4170649920190e989175755369215d4f29a7427b`
  (August 2026). Keep this gate until the newer contracts are implemented; passing
  it does **not** establish compatibility with current Rust.
- Rust main initially inspected: `6dc433d9cc9cf1805e5074e8a8bf95a65dddfc30`.
  Follow-up main: `740eac2` (2026-10-22), including merged #160 and #162.
- Published Rust v0.1.5 tag: `f76ff9d2558fac705f091d9b858d3bc180b4f0bd`.
  GitHub's latest-release selector currently returns v0.1.4, so do not use that
  selector as the identity of the v0.1.5 artifacts.
- Private v1 candidate and subsequent Rust main fixes are moving targets.
  Rust #162 is now merged, not part of public v0.1.5. Recheck main and candidate commits before porting or claiming parity.

## Initial source-audit matrix (historical)

“Historical gate” refers only to the pinned August differential suite. “Pending”
requires both implementation review and execution against the named newer Rust
revision; matching tool names or copied tests are insufficient evidence.

| Area | Current Rust reference behavior | .NET finding / evidence | Closure requirement / priority |
|---|---|---|---|
| Tool surface | 23 inbound tools; direct calls and official MCP tool lifecycle | 23 direct tool contracts have historical gate; lifecycle absent in `ToolRegistry` | Add lifecycle schemas/wrappers, pagination/error semantics and SDK tests; P1 |
| Classifier rules | Scanner UA boundaries, internal/XML-RPC paths, four rounds of byte-wise percent/backslash normalization | PR #17 adds rules and independent expected-signal regressions; 31 running-build differential cases pass against both pinned v0.1.5 and inspected main | Scoped gate passes; broaden adversarial corpus before full classifier qualification |
| Cache identity | Structured fingerprint covers body, referer, accept, extra and verified TLS | Main still joins delimiters and omits fields; PR #18 replaces this with structured inputs and regression tests | PR #18 unit and historical integration gates pass; mixed-runtime cache byte identity remains unqualified; P0 |
| JSON-RPC envelope | Validates version, integer/string IDs, object params/results, invalid-request vs parse-error, notifications and responses | `McpMessage` permissive historical parser | Port envelope contract and run HTTP/WS negative matrix; P0 |
| HTTP negotiation | Version/content-type/Accept checks; stateless POST, GET 405 | New negotiation not implemented | Test duplicate/malformed headers and no-body notification status; P1 |
| Browser origin / auth | Allowed-origin checks, strict Bearer parsing and caller scopes | Bearer historical tests; new origin contract absent | Review auth diff, origin config validation and transport tests; P0 |
| Connection / timeout limits | TCP admission; #160 pre-auth first-request deadline; #162 HTTP/2 idle 60s, age 120s, exactly 128 admitted requests, synchronous 503 beyond cap and bounded GOAWAY drain; bounded WS admission and writes | Work semaphore and tool timeout exist; PR #20 adds immediate tool shedding; connection limits still absent | Test slow clients, saturation and recovery on running .NET host; P0 |
| WebSocket framing | UTF-8, byte bounds, close/ping handling and bounded lifetime | Historical differential covers basic WS only | Run newer adversarial WS corpus; P0 |
| stdio | Process-local newline-delimited JSON-RPC; logs off stdout | Host only starts ASP.NET Core | Add process transport and packaged SDK/CLI smoke; P1 |
| Backend behavior | Redis recovery, PostgreSQL TLS/recovery, scoped persistence/reporting | Historical Redis/Postgres/MaxMind tests; newer SQL/TLS and tenancy changes unreviewed | Audit integration diffs, cross-caller negatives and outage/recovery tests; P0 |
| Configuration / redaction | New transport, origin and limit settings; safer snapshots | Six file formats tested historically; newer fields absent | Map all fields/defaults/validation and test secret non-disclosure; P1 |
| Telemetry | New redaction/logging changes and resource limits | Historical metrics/OTLP checks only | Review log inputs, cardinality and sink failures; P1 |
| Native packaging | Five platform bundles, archive verification and smoke evidence | Release workflow lists four RIDs, no Linux musl lane; no published .NET assets | Add truthful install guide, artifact verification and per-platform smoke; P1 |
| Containers / deployment | Multiarch qualification, dependency scans, native/musl and backend TLS evidence | .NET scheduled 2026-10-19 scan failed on runtime advisories; PR #19 refreshes pinned SDK/runtime and adds read-only runtime smoke | First hosted replacement scan passed; final runtime smoke and updated-commit scan pending, then backend/platform qualification; P0 |
| Release gates | Owner review required for stable publication; private candidate qualification | Tag workflow publishes stable/`latest` without an explicit owner gate | Add enforced stable gate and prerelease handling; never tag stable before owner review; P0 |
| Companion skill / install | Rust skill and npm wrapper document native dependencies/client limits | No .NET companion skill or npm package | Decide distribution, provide .NET-specific setup and smoke; do not reuse Rust install claims; P2 |
| Client evidence | Inspector, Python SDK, Claude Code, Cursor CLI and ASD adapters with exact artifact evidence | No equivalent current .NET server client evidence | Test actual .NET server artifacts with supported clients; P1 |
| Outbound egress | Inbound classifier does not enforce outbound egress | No outbound enforcement implemented | Keep all claims limited to inbound classification; no egress parity claim |

## Evidence rules and execution order

1. Fix classifier/cache, envelope/auth/origin and admission/resource boundaries in
   bounded PRs with expected-outcome regressions independent of the Rust oracle.
2. Add current-Rust scoped differential jobs before replacing the historical gate.
   Do not normalize away security-relevant differences to make that gate pass.
3. Implement official lifecycle, HTTP profile and stdio, then test pinned real SDKs
   against a running .NET build. Document supported clients only after those pass.
4. Qualify fresh installs, native/container packaging and recovery. Record exact
   commits, artifact digests, commands and results, including failures.
5. Reconcile this matrix with Rust main and private v1 fixes. Full parity remains
   unverified until every applicable row has current executable evidence.

Hosted CI is preferred while shared-host backup I/O remains high. This assessment
started no VM, local Docker build or backend container. Coordinate any later
shared-resource work with the canonical Request Guard chat. The existing August
verification record remains historical and must not be relabeled as current.

## Initial hosted evidence

- PR #17 at `fb4d3b3`: [CI run 39732536875](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/runs/39732536875)
  passed formatting, warning-as-error build, dependency audit, 141 xUnit cases,
  the historical differential/integration gate and 31 independently expected
  classifier cases against each current reference above. CodeQL also passed.
  Only latency/model build metadata is excluded from the new classifier comparison;
  score, signals, verdict, confidence and category are compared.
- PR #18 at `e998077`: [CI run 39732674900](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/runs/39732674900)
  passed build, formatting, unit tests, dependency audit and historical integration
  differential. Cross-runtime cache serialization remains explicitly unqualified.
- PR #19 at `8e42920`: [security run 39732790439](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/runs/39732790439)
  passed its replacement-image scan. The later runtime-smoke addition is not covered
  by that result; require its own green run before merging.

These are maintainer/automation results, not independent human verification. The initial PRs remain inspectable as review records; subsequent merge/disposition
updates below supersede their initial status. Unimplemented matrix rows remain open.

## Connection-security follow-up — Rust main 740eac2

Rust PR #160 bounds incomplete HTTP/2/auto-protocol connections before authentication
or a first complete request. Merged PR #162 adds post-first-request HTTP/2 protection:
60-second idle timeout, 120-second lifetime, at most 128 admitted requests with a
synchronous 503 for excess requests, and bounded graceful GOAWAY draining. These are
development-main requirements, not guarantees of the published v0.1.5 artifact.
Equivalent .NET protections and direct socket tests are the next security priority.
Kestrel's protocol negotiation and graceful-close behavior need their own execution
evidence; matching timeout values alone will not close this row.

## Redis operation-record follow-up — issue #159 / merged Rust #163

Rust issue #159 confirmed optional start/finish record waits could extend response
latency beyond the dispatch timeout. The private rc.3 paused-Redis probe measured
2.507/2.005/2.005 seconds with a two-second classify timeout. Rust PR #163
caps each record wait at 500 ms; the reported fixed-development-build samples were
1.506/1.505/1.505 seconds. These are upstream-reported results, not .NET evidence,
and #163 was initially open when inspected (head `28103a7`). A follow-up GitHub
check confirmed its merge on 2026-10-22; it remains development behavior, not
public v0.1.5 evidence.

.NET has analogous pre/post-dispatch bookkeeping: its start record can consume the
whole tool timeout, and finish can add one second afterward. The concrete transaction
methods ignore cancellation. .NET PR #22 proposes independent 500 ms record waits,
caller-cancellation propagation and expected-outcome tests with cancellation-ignoring
record clients. It deliberately does not claim paused-backend parity: optional cache
reads/writes in `ClassifyService` also lack independent latency bounds and must be
qualified separately with healthy-backend, response-shape, repeated paused-backend
and recovery checks. Status remains **unverified for real Redis outage parity**.

## Current maintenance disposition and tracked gaps

PRs #17–#22 are merged after exact-head checks and strict-base updates. The
combined baseline is `c028cdbf9408aac66d43db27b0eede3c4ab3fcc6`. The cache-bound follow-up implements a
real-Redis gate but is still a candidate until its exact-head run passes; no real
paused-Redis parity is inferred from synthetic cancellation tests. The original audit
above is historical; use these issues and linked PR evidence for current status.

| Remaining qualification area | Tracked issue |
|---|---|
| Independent cache deadlines, real Redis pause/outage/recovery and negative control | [#23](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/23) |
| Envelopes, official lifecycle, HTTP negotiation, stdio and protocol SDK evidence | [#24](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/24) |
| Auth/origin, remaining WS/connection boundaries, same-port cleartext HTTP/2 and TLS/proxies | [#25](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/25) |
| Caller-bound persistence/reporting, PostgreSQL TLS, backend failure/recovery and MaxMind | [#26](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/26) |
| Native/container installs, supported platforms, real clients and distribution/skill docs | [#27](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/27) |
| Enforced owner-reviewed stable publication and exact-candidate release gates | [#28](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/28) |
| Broader classifier, mixed-runtime cache serialization, config/redaction and telemetry | [#29](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/29) |

Full parity remains **unverified**. The current maintenance reference includes Rust
#163 at `899a4b7264a34a5e6c2b60027cb26288e96be30d`; the scoped classifier jobs still pin
v0.1.5 and `6dc433d` and must not be described as a full differential against `899a4b7`.
No .NET stable tag, package publication or outbound-egress enforcement is authorized
by these maintenance results.
