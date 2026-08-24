# Requirements Traceability Matrix

This matrix is the project-level traceability record for the sole-maintainer, DO-178C-inspired
assurance process. It is engineering evidence, not a claim of certification or airborne fitness.

| ID | Verifiable requirement | Design / implementation | Verification evidence |
|---|---|---|---|
| HLR-001 | Accept JSON-RPC 2.0 over HTTP POST and WebSocket at `/mcp`, including strict UTF-8 and Rust-compatible null fields. | `McpEndpoints`, `WebSocketConnectionHandler`, `McpDispatcher`, `McpMessage` | `McpMessageTests`, `McpDispatcherTests`, differential core transport phase |
| HLR-002 | Require a configured Bearer token before MCP dispatch. | `BearerAuth`, `McpEndpoints.Authorize` | `BearerAuthTests`, differential HTTP/WebSocket auth phase |
| HLR-003 | Bound request bytes, concurrent work, batch size, and execution time. | `McpDispatcher`, `BatchClassifyTool`, `AppConfig.Limits` | `McpDispatcherTests`, `Phase3ToolsEndToEndTests`, `AppConfigTests`, database-lock timeout differential |
| HLR-004 | Provide all 23 request-guard-mcp tool contracts. | `ToolRegistryFactory`, `RequestGuardMcp.Tools` | `CompleteRegistryTests.FactoryRegistersAllTwentyThreeContracts`, all-tool differential phase |
| HLR-005 | Produce behaviorally compatible classification signals, score, verdict, explanation, UA enrichment, and replay. | engines, `ClassifyService`, `WootheeUaParser`, replay tool | engine tests, 238-case Woothee corpus verification, core and integration differential phases |
| HLR-006 | Keep cached decisions isolated by authenticated caller and all scoring inputs. | `BearerAuth`, `RequestFingerprint`, `ClassifyService` | unit cache tests plus two-token/two-key Redis differential assertion |
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
