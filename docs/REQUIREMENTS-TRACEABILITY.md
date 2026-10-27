# Requirements Traceability Matrix

This matrix is the project-level traceability record for the sole-maintainer, DO-178C-inspired
assurance process. It is engineering evidence, not a claim of certification or airborne fitness.

| ID | Verifiable requirement | Design / implementation | Verification evidence |
|---|---|---|---|
| HLR-001 | Accept JSON-RPC 2.0 over HTTP POST and WebSocket at `/mcp`, including strict UTF-8, validated MCP envelopes and exclusive result/error fields. | `McpEndpoints`, `WebSocketConnectionHandler`, `McpDispatcher`, `McpMessage` | `McpMessageTests`, `McpDispatcherTests`, differential core transport phase |
| HLR-002 | Require a configured Bearer token before MCP dispatch. | `BearerAuth`, `McpEndpoints.Authorize` | `BearerAuthTests`, differential HTTP/WebSocket auth phase |
| HLR-003 | Bound request bytes, concurrent work (reject saturation without queueing), batch size, and execution time. | `McpDispatcher`, `BatchClassifyTool`, `AppConfig.Limits` | `McpDispatcherTests`, `Phase3ToolsEndToEndTests`, `AppConfigTests`, database-lock timeout differential |
| HLR-004 | Provide all 23 request-guard-mcp tool contracts. | `ToolRegistryFactory`, `RequestGuardMcp.Tools` | `CompleteRegistryTests.FactoryRegistersAllTwentyThreeContracts`, all-tool differential phase |
| HLR-005 | Produce behaviorally compatible classification signals, score, verdict, explanation, UA enrichment, and replay. | engines, `ClassifyService`, `WootheeUaParser`, replay tool | engine tests (including scanner boundaries and bounded encoded-path regressions), 238-case Woothee corpus verification, core and integration differential phases |
| HLR-006 | Keep cached decisions isolated by authenticated caller and all scoring inputs. | `BearerAuth`, `RequestFingerprint`, `ClassifyService` | unit cache tests (field/header delimiter collisions, all decision inputs, recursive object ordering) plus two-token/two-key Redis differential assertion |
| HLR-007 | Treat Redis, PostgreSQL, and MaxMind as optional; never synthesize backend data. | integration interfaces/null clients, concrete clients, backend-dependent tools | `CompleteRegistryTests.BackendDependentToolFailsTruthfullyWhenDisabled` |
| HLR-008 | Persist decisions and feedback and support deterministic replay, drift, and calibration. | `PostgresClient`, reporting/replay/feedback tools | unit tests and disposable-PostgreSQL differential round trip |
| HLR-009 | Use Redis for distributed cache, reputation, canaries, and queue telemetry. | `RedisClient`, `ReputationClient`, security/queue tools | unit tests and seeded disposable-Redis differential phase |
| HLR-010 | Use configured MaxMind databases for City, ASN, and anonymous-IP enrichment. | `GeoipClient`, `EnrichIpTool`, `EnrichAsnTool` | checksum-pinned official MaxMind test databases in differential CI |
| HLR-011 | Ignore TLS fingerprints unless a fresh request-bound HMAC attestation verifies. | `TlsAttestation`, `ClassifyService`, `RuleEngine` | `TlsAttestationTests`, signed/tampered differential phase |
| HLR-012 | Never persist or cache the TLS attestation secret. | `ClassifyService` consumes and clears the token before fingerprint/persistence | `TlsAttestationTests.ClassificationConsumesSecretAndAppliesKnownBadRule` |
| HLR-013 | Expose liveness, readiness, Prometheus metrics, JSON logs, and optional OTLP traces. | `HealthCheck`, `McpEndpoints`, `AppMetrics`, OpenTelemetry host wiring | `CompleteRegistryTests.MetricsRenderPrometheusFamilies`, smoke procedure |
| HLR-014 | Build reproducibly from locked dependencies with warnings and analyzers as errors. | central package versions, lock files, `Directory.Build.props`, CI | `make ci`, GitHub CI/CodeQL/dependency review |
| HLR-015 | Provide least-privilege container and Kubernetes deployment examples. | `docker/`, `deploy/k8s/` | Docker build and manifest validation procedure |
| HLR-016 | Load the same CONFIG_FILE formats and snake_case keys as Rust, with environment overrides. | `RustConfigFileParser`, Host composition root | parser tests and JSON/INI/TOML/YAML/RON/JSON5/dotenv differential phases |
| HLR-017 | Warm engines and cache before accepting traffic. | Host startup, `WarmupTool` | startup inspection plus warmup differential case |

## Derived requirements and assumptions

- DR-001: Authenticated caller cache scopes use HMAC rather than a bare token hash to resist
  offline guessing from shared Redis keys.
- DR-002: Attestation keys and caller-scope HMAC keys are distinct secrets with at least 32 UTF-8
  bytes. Rotation accepts one previous TLS key.
- DR-003: A configured backend that cannot initialize prevents startup. A backend that fails
  later makes readiness unhealthy and produces an upstream error, while Redis cache operations
  degrade to the local cache.
- DR-004: MaxMind accuracy is bounded by the supplied database and must not be interpreted as a
  precise physical address.
- DR-005: PostgreSQL schema creation requires DDL permission at startup. Operators needing stricter
  separation should pre-create the documented tables and revoke DDL afterward.

Update this matrix whenever a requirement, implementation path, or verification artifact changes.

- DR-006: Unknown method names must be rejected before Redis bookkeeping and tracing;
  metric labels are limited to registered tool names plus `__unknown__`. Dispatcher overload
  and 100-name cardinality regressions verify this boundary.

## Connection-resource follow-up (Rust main 740eac2)

DR-007: Bound total TCP admission before authentication, incomplete first requests,
MCP body reads, and HTTP/2 idle/age/request-count retention. `ConnectionResourceGuard`
and Host wiring implement admission and monitoring; `McpEndpoints` applies the body
deadline. `ConnectionResourceGuardTests` exercises real Kestrel sockets and concurrent
128-winner admission, synchronous excess-request 503, recovery, idle/age rotation,
HTTP/1 survival and incomplete-body 408. `AppConfigTests` rejects zero limits. Production
HTTP/2 age is fixed at 120 seconds; socket tests shorten only that internal test budget.
Full transport/client parity and TLS/ALPN qualification remain open.

## Redis operation-record follow-up (Rust issue #159 / merged PR #163)

DR-008: Each optional dispatch start/finish queue record must stop delaying the response
after 500 ms even if its Redis implementation ignores cancellation. Tool execution has
its own deadline after start recording. `McpDispatcher` implements these boundaries;
`RedisBookkeepingTests` independently stalls start/finish, verifies classification and
timeout envelopes, caller cancellation and permit recovery. This requirement does not
establish distributed-cache outage/recovery parity.

## Optional Redis cache waits (issue #23)

DR-009: Each optional classification cache read/write has an independent 500 ms wait
boundary even when the client ignores cancellation. Only caller cancellation propagates;
cache-only failure falls back without synthesizing a decision. `ClassifyService` implements
this boundary; `RedisCacheDeadlineTests` covers stalled reads/writes/both and caller
cancellation in either phase. `tests/parity/redis_outage.py` is the running-backend gate,
including negative control, cold keys, expected output, three-sample latency distributions,
actual Redis cache-read proof, fresh recovered writes and resource cleanup. See
`REDIS-OUTAGE-QUALIFICATION.md` for commands, scope and exact evidence requirements.

## MCP lifecycle follow-up (issue #24)

DR-010: Validate the 2025-06-18 envelope before side effects. Reject malformed envelopes
with -32600, distinguish invalid JSON (-32700), preserve string/integer IDs, ignore
valid notifications/responses, and omit the unused response branch. Implement initialize,
ping, sorted tool definitions/schemas and named standard calls using the same scoped,
bounded dispatch as direct methods. `McpMessageTests`, `McpLifecycleTests` and
`tests/protocol/run.py` trace these behaviors through unit, real HTTP/WebSocket and official
Python SDK evidence. The historical envelope migration is explicit in `contract.py` and
`test_contract_envelopes.py`; the historical Rust pin is unchanged. Full HTTP negotiation remains tracked by #24.

DR-011: `--stdio` opens no network listener and uses process ownership instead of HTTP
Bearer headers. Bound UTF-8 newline-delimited input, drain oversized lines, keep stdout
JSON-only with stderr diagnostics, isolate each subprocess's cache scope, and exit on
EOF or shutdown without an unbounded stream wait. `StdioTransport`, Host mode selection,
`StdioTransportTests` and `tests/stdio/run.py` implement/verify the Linux process contract
and official SDK startup/calls. This transport is sequential and does not implement
notification-based in-flight cancellation; packaged platform evidence remains open.

DR-012: Build native archives from tracked source with a pinned SDK and separate locked
RID/single-file dependency graph. Reject dependency drift, exclude local config/debug
artifacts, include source/RID/SDK and file/archive checksums, and test the exact extracted
binary on matching operating systems before a platform claim. `ReleaseBundle`, release
locks, `native_bundle.py`, `qualify_bundle.py`, `lock_negative.py` and `native.yml` map this
requirement to build/provenance, negative-control and real-client evidence. No stable
publication or signing/installer qualification is implied.

DR-013: Before body dispatch, enforce UTF-8 JSON content-type, explicit acceptable JSON/SSE
response types and supported present protocol version. Reject repeated/malformed Bearer
headers, compare all allowed tokens with fixed-time comparison, and validate every present
HTTP/WS browser Origin against a bare-origin allowlist even when auth is disabled. Empty
allowlists deny every present Origin; missing Origin supports machine clients. Default
native binding is loopback. `BearerToken`, `HttpOrigin`, `OriginPolicy`, `HttpProfile`,
configuration validation and `McpEndpoints` implement this boundary. Auth/origin unit tests,
real Kestrel `HttpProfileTests` and `tests/protocol/run.py` (default and explicitly configured
origins) verify rejection, legitimate traffic and recovery before SDK calls. Missing protocol
version remains an intentional direct-client/initialization compatibility extension.

DR-014: Bound whole-message WebSocket reception (including unfinished/trickled fragments),
response/close writes and HTTP response writes independently of tool execution. Even an
I/O implementation ignoring cancellation must stop retaining the handler after its
observer deadline; abort instead of appending a second response. Answer close output
without awaiting another peer close, preserve client cancellation, release metrics/TCP
permits and reject unsupported timer values at startup. `WebSocketConnectionHandler`,
`McpEndpoints.WriteWithDeadlineAsync`, validated limits, `WebSocketDeadlineTests`,
`HttpResponseDeadlineTests`, real incomplete-message admission recovery and the running
protocol/native matrix trace these properties. Production-duration/slow-consumer and
proxy/TLS evidence remain separately required.
