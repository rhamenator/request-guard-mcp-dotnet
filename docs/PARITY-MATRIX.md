# Rust / .NET parity maintenance matrix

Assessment: 2026-10-30. **Full parity remains unverified.** Tool names, source similarity,
and historical test counts are not substitutes for running-build evidence. No official release
candidate or stable .NET release is published; pre-1.0 publication requires owner review and the
exact technical gates described in `RELEASE-READINESS-REVIEW.md`.

## Controlled references

- Initial .NET main: `76c4de4aab291ffc72b049ab63930ee02b75094a`.
- Merged maintenance baseline before the cache fix:
  `c028cdbf9408aac66d43db27b0eede3c4ab3fcc6` (#17–#22).
- Historical full differential baseline:
  `4170649920190e989175755369215d4f29a7427b` (August 2026).
- Published Rust v0.1.5 tag: `f76ff9d2558fac705f091d9b858d3bc180b4f0bd`.
  GitHub's latest-release selector returned v0.1.4 at inspection, so it is not used
  as the identity of the v0.1.5 artifacts.
- Current Rust main executable reference:
  `2660c1fbfd71b38108dfc7b9decd309e4a8e480c`.
- Scoped current-reference jobs pin v0.1.5 and that exact current-main revision. They run the
  complete HTTP/WebSocket/stdio protocol gate and the classifier/configuration/security gates,
  but do not establish full backend differential compatibility with current Rust. Reconcile new
  main/candidate fixes before claiming broader parity.

## Current matrix

| Area | .NET implementation / evidence | Remaining qualification / issue |
|---|---|---|
| Tool surface | All 23 direct inbound tools have the historical differential gate; .NET plus exact v0.1.5/current-main executables pass official Python SDK 1.30.0 initialization, schema validation and all-tool calls | Future schema/tool changes require exact-reference requalification; backend differential parity remains separately scoped |
| Classifier | The running-build gate checks 82 independently expected shared cases against v0.1.5/current main and 83 additional current-main cases. It covers every maintained UA marker, headers/methods, bounded byte-wise decoding, precise sensitive-route positives and ordinary-route false-positive controls, then compares the complete normalized result. | Future Rust revisions and corpus/endurance expansion remain controlled maintenance under [#29](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/29); this does not establish every transport/backend behavior. |
| Cache identity | Caller-bound keys now match current Rust safety semantics: only rule inputs partition identity, query/fragment text is ignored, and a schema/build/normalized-TLS-policy domain prevents stale verdict reuse. Unicode/numeric `extra` values are irrelevant because the classifier does not consume them. Mixed-runtime parity uses separate tested Redis prefixes. | Shared Rust/.NET cache-byte identity is explicitly unsupported because build domains differ; mixed runtimes must use distinct prefixes. |
| Optional cache failure isolation | #30 bounds each read/write at 500 ms, preserves caller cancellation and passes the real-Redis scope below | Scoped work tracked in [#23](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/23); broader lookup/health behavior is not covered |
| Dispatch admission / telemetry | #20 sheds saturated calls immediately and bounds unknown-method labels before Redis/tracing. Running-host CI proves unavailable credentialed OTLP isolation/redaction and runs the Release host plus both exact Rust references for 120 seconds at 25 unique-unknown/healthy pairs per second, requiring exact bounded metrics, probe-name log exclusion, and Linux process ceilings. | This is not maximum-throughput, memory-leak, external-collector, or every-log-field evidence; future production-load expansion is controlled maintenance. |
| Redis operation records | #22 bounds each start/finish wait at 500 ms and preserves the separate tool execution budget | Not a global response SLA or evidence for every Redis method |
| JSON-RPC envelopes | Strict version, ID, params, result/error and notification/response validation; valid request IDs survive invalid-params errors; rejected one-way HTTP envelopes return empty 400; .NET plus both exact Rust references pass real HTTP/WS negatives and recovery | .NET intentionally rejects duplicate JSON properties while the controlled Rust references use last-key-wins; this difference is asserted, not normalized |
| HTTP negotiation | UTF-8 JSON content-type, explicit JSON/SSE Accept and present-version checks; accepted messages return 202, rejected one-way input returns 400, authenticated GET405; exact-reference jobs run the complete profile; a real Nginx gate adds TLS 1.3, ALPN h2 and HTTP/1.1 | Missing version intentionally supports legacy direct clients. .NET retains stricter media/header handling and RFC case-insensitive Bearer parsing than the controlled Rust references; broader proxy/client qualification remains [#25](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/25) |
| Auth / browser origin | Repeated/malformed header rejection, case-insensitive Bearer scheme, fixed-time token matching; >=32-byte non-placeholder startup tokens, loopback-only no-auth binding and a 32-entry validated origin allowlist gated against exact Rust refs; default-deny with real direct and Nginx-proxied HTTP/WS tests | Broader browser/client and IIS qualification: [#25](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/25) |
| Connections / deadlines | #21 adds shared TCP/upgrade admission, pre-first-request deadline, body and response-write timeouts plus HTTP/2 idle/age/count/drain controls with real socket tests; CI holds real Kestrel HTTP/2 traffic across the fixed 120-second production age and qualifies Linux Nginx TLS termination, ALPN h2, HTTP/1.1 and WebSocket proxy recovery | Same-port cleartext HTTP/2 detection, packaged-artifact/load endurance, slow consumers, arbitrary proxies and IIS: [#25](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/25) |
| WebSocket framing / lifetime | Complete-message receive and write/close deadlines, mcp negotiation, fragmented UTF-8, oversize and recovery; fake cancellation-ignoring I/O and real slot recovery | Production-duration/slow-consumer and broader wire/proxy qualification: [#25](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/25) |
| stdio | Process-local --stdio, bounded newline/UTF-8 input, stderr diagnostics, EOF/signal shutdown, official SDK process smoke and explicit PostgreSQL rejection pass on .NET and both exact Rust references | Sequential transport has no notification-based in-flight cancellation; installed-binary lifecycle evidence remains in the native/installer jobs |
| Persistence / reporting / backend TLS | Historical Redis/PostgreSQL/MaxMind suite plus current caller-bound hashed decision keys, scoped replay/feedback/reports, minimized replay snapshots, serialized schema initialization, transactional feedback replacement, and a Docker-backed PostgreSQL `VerifyFull` trust/rejection plus pause/stop/recovery gate | Hosted exact-head evidence and broader platform/cloud/failover qualification: [#26](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/26) |
| Configuration / redaction | All six syntaxes/seven extensions plus dotenv map a broad scalar fixture to the complete stable redacted snapshot in the historical differential suite. Exact v0.1.5/current-main gates compare independently specified stable defaults and shared rejected-startup families; malformed present TLS age now fails instead of silently defaulting. | Operational behavior of fields absent from `config_snapshot`, .NET-only transport extensions, and production-duration limits remain bounded follow-up under [#29](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/29) and [#25](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/25). |
| Containers | #19 patches pinned SDK/runtime and passes non-root/read-only auth/classifier smoke and vulnerability scan | Broader platform/backend TLS and exact-artifact qualification: [#27](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/27) |
| Native packaging / installs | Four-RID locked native bundles plus DEB/RPM, MSI and PKG installers; matching-platform extraction, protocol, payload, install, official SDK stdio, upgrade, uninstall and residue qualification | Review exact-artifact run results; signing/notarization and additional architecture disposition: [#27](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/27) |
| Clients / companion distribution | Pinned official Python SDK exercises lifecycle, schemas and all 23 calls on a running build; installer jobs repeat the SDK stdio smoke through installed DEB, MSI and PKG executables after clean install and upgrade | Real Inspector/CLI/ASD and .NET-specific skill/npm disposition: [#27](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/27) |
| Release gates | Owner-reviewed `0.MINOR.PATCH` gate requires exact-SHA native bundle and installer evidence, then publishes immutable GitHub prereleases and versioned containers without moving `latest`; independent-review status is disclosed | Signing/notarization remains deferred under [#27](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/27); policy origin: [#28](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/28) |
| Outbound egress | Inbound classification only | No outbound egress enforcement is implemented or claimed |

## Evidence and boundaries

PRs #17–#22 merged after self-review, strict-base updates and exact-head green checks;
the PR descriptions preserve those source identities and workflow results. These are
maintainer/automation results, not independent human verification.

- Classifier: the initial [run 39732536875](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/runs/39732536875)
  established 31 scanner/encoded-path cases. The maintained gate now requires 82 shared cases at
  exact v0.1.5/current-main revisions and 83 additional cases for current main's expanded route
  grammar. Only measured latency/model build metadata is excluded; score, signals, verdict,
  confidence, category, request identity and processing metadata otherwise match.
- Connection controls follow Rust #160 (pre-auth incomplete first request) and #162
  (HTTP/2 idle 60 s, age 120 s, exact 128 admitted requests, synchronous excess 503 and
  bounded GOAWAY drain). .NET #21 tests actual sockets, including an unresponsive peer,
  capacity recovery and WebSocket admission. CI additionally runs the real 120-second Kestrel age
  under continuous traffic; it is not packaged-artifact, throughput or maximum-load evidence.
- Redis #163: upstream private-candidate paused samples of 2.507/2.005/2.005 s improved
  to reported fixed-development samples of 1.506/1.505/1.505 s. Those upstream results
  alone were not treated as .NET evidence. .NET #22's synthetic-client tests isolated
  bookkeeping; #30 separately qualified the optional cache path against real Redis.
- Redis cache: [run 39741361266](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/runs/39741361266)
  passed at `ca1cb71`, with merged pre-fix main `c028cdb` as negative control. Paused
  candidate median/worst were 1.503/1.533 s with 3/3 correct cold-key responses;
  stopped-backend median/worst were 1.503/1.503 s. The control had 0/3 valid paused
  responses at 2.503/2.511 s median/worst, then recovered. Fresh writes and actual Redis
  reads passed after unpause and restart. An earlier independent run also passed.
  See [commands, samples and scope](REDIS-OUTAGE-QUALIFICATION.md). This qualifies the
  tested classifier/cache path in hosted Ubuntu/.NET, not all Redis operations or clients.

Keep the historical differential pin until newer contracts pass their own executable
gates; do not normalize away security-relevant differences to advance it. Record exact
source/artifact identities, commands, failures and explicit dispositions. Keep August's
verification record historical. Stable review, full parity and platform/client claims
cannot inherit evidence from a different build or the Rust server.

The October 22 maintenance batch used hosted CI. Owner-authorized October 27 local
qualification additionally passed 179 tests, Docker auth/classification, actual Redis
pause/stop/recovery, and 40 correct Kali VM client calls on main `481d552`.
These are scoped functional checks, not router-path or production approval. Further
shared-host work is coordinated with the canonical Request Guard chat. All remaining matrix rows have tracked issues; full parity remains
unverified until their applicable gates pass.
