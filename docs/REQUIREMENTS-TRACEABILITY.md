# Requirements Traceability Matrix

This matrix is the project-level traceability record for the sole-maintainer, DO-178C-inspired
assurance process. It is engineering evidence, not a claim of certification or airborne fitness.

| ID | Verifiable requirement | Design / implementation | Verification evidence |
|---|---|---|---|
| HLR-001 | Accept JSON-RPC 2.0 over HTTP POST and WebSocket at `/mcp`. | `McpEndpoints`, `WebSocketConnectionHandler`, `McpDispatcher` | `McpMessageTests`, `McpDispatcherTests` |
| HLR-002 | Require a configured Bearer token before MCP dispatch. | `BearerAuth`, `McpEndpoints.Authorize` | `BearerAuthTests` |
| HLR-003 | Bound request bytes, concurrent work, batch size, and execution time. | `McpDispatcher`, `BatchClassifyTool`, `AppConfig.Limits` | `McpDispatcherTests`, `Phase3ToolsEndToEndTests`, `AppConfigTests` |
| HLR-004 | Provide all 23 request-guard-mcp tool contracts. | `ToolRegistryFactory`, `RequestGuardMcp.Tools` | `CompleteRegistryTests.FactoryRegistersAllTwentyThreeContracts` |
| HLR-005 | Produce behaviorally compatible classification signals, score, verdict, and explanation. | `RuleEngine`, `Scorer`, `ExplainEngine`, `ClassifyService` | `RuleEngineTests`, `ScorerTests`, `Phase3ToolsEndToEndTests` |
| HLR-006 | Keep cached decisions isolated by authenticated caller and all scoring inputs. | `BearerAuth`, `RequestFingerprint`, `ClassifyService` | `BearerAuthTests`, `RequestFingerprintTests`, cache end-to-end test |
| HLR-007 | Treat Redis, PostgreSQL, and MaxMind as optional; never synthesize backend data. | integration interfaces/null clients, concrete clients, backend-dependent tools | `CompleteRegistryTests.BackendDependentToolFailsTruthfullyWhenDisabled` |
| HLR-008 | Persist decisions and feedback and support deterministic replay, drift, and calibration. | `PostgresClient`, reporting/replay/feedback tools | unit contract tests plus backend integration procedure in `VERIFICATION.md` |
| HLR-009 | Use Redis for distributed cache, reputation, canaries, and queue telemetry. | `RedisClient`, `ReputationClient`, security/queue tools | unit contract tests plus backend integration procedure in `VERIFICATION.md` |
| HLR-010 | Use configured MaxMind databases for City, ASN, and anonymous-IP enrichment. | `GeoipClient`, `EnrichIpTool`, `EnrichAsnTool` | adapter tests where licensed test MMDB data is available; startup/readiness checks |
| HLR-011 | Ignore TLS fingerprints unless a fresh request-bound HMAC attestation verifies. | `TlsAttestation`, `ClassifyService`, `RuleEngine` | `TlsAttestationTests` |
| HLR-012 | Never persist or cache the TLS attestation secret. | `ClassifyService` consumes and clears the token before fingerprint/persistence | `TlsAttestationTests.ClassificationConsumesSecretAndAppliesKnownBadRule` |
| HLR-013 | Expose liveness, readiness, Prometheus metrics, JSON logs, and optional OTLP traces. | `HealthCheck`, `McpEndpoints`, `AppMetrics`, OpenTelemetry host wiring | `CompleteRegistryTests.MetricsRenderPrometheusFamilies`, smoke procedure |
| HLR-014 | Build reproducibly from locked dependencies with warnings and analyzers as errors. | central package versions, lock files, `Directory.Build.props`, CI | `make ci`, GitHub CI/CodeQL/dependency review |
| HLR-015 | Provide least-privilege container and Kubernetes deployment examples. | `docker/`, `deploy/k8s/` | Docker build and manifest validation procedure |

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
