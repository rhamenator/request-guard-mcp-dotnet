# Architecture

## Status

This document describes the architecture for `request-guard-mcp-dotnet`, a from-scratch C#/.NET
port of [`request-guard-mcp`](https://github.com/rhamenator/request-guard-mcp) (Rust), as the design
record required by [`DO-178C-ASSURANCE-POLICY.md`](DO-178C-ASSURANCE-POLICY.md). Phases 1-2 (below)
are implemented; sections describing later phases (engines, integrations, observability, deploy)
describe intent, not yet-built behavior, until each phase lands. Each phase's commit updates this
file to reflect what was actually implemented.

**Phase 2 refinements from the original sketch:** `AppState`, `BuildInfo`, and the shared
`HealthCheck` computation live in `RequestGuardMcp.Core` rather than the Host project — `Core` is
the one project every other project can depend on, and both the raw `GET /health` endpoint (Mcp)
and the `health` tool (Tools) need the same logic, so it has to sit below both. The application
error type is named `AppErrorException` (not `AppError`) to satisfy .NET's naming convention for
`Exception` subclasses (CA1710). The `model_info` tool reports exactly the tools actually
registered in the `ToolRegistry`, rather than the Rust server's fixed 22-tool list with an
`enabled` flag — honest about this port's in-progress tool surface rather than listing tools that
don't exist yet; this will converge with the Rust behavior once all 22 tools are implemented
(phase 5).

**Phase 3 notes:** `AnomalyEngine` and `Policy` (src/engines/anomaly.rs, src/engines/policy.rs)
are **not** ported — `grep` across the Rust source confirms neither is referenced anywhere outside
its own module; they are dead code in the source project, and porting unused code would violate
this project's own "no code beyond what's needed" standard. `RuleEngine.Evaluate` and
`Scorer.Score` are static methods (both engines are pure functions over their input, with no
instance state — the .NET analyzers flag stateless instance methods as CA1822, and the fix is
correct here, not a suppression). The in-process cache (`ICacheStore`/`InMemoryCacheStore`) is a
hand-rolled bounded TTL cache rather than a port of the `moka` crate: it's simple enough to write
directly on `ConcurrentDictionary`, avoiding a dependency for something this small, at the cost of
FIFO eviction instead of moka's LRU — swappable later behind the interface if that ever matters.
TLS fingerprint handling (validation, normalization, and the `tls_fingerprint_verified` rule) is
deferred as one unit to phase 5 alongside the attestation crypto it depends on; until then
`ClassifyRequest.TlsFingerprintVerified` is always `false` and TLS fields never affect scoring or
cache-key fingerprinting.

## Overview

`request-guard-mcp-dotnet` is a Model Context Protocol (MCP) server built on ASP.NET Core / Kestrel.
It provides the same request classification and enrichment API as the Rust original, over WebSocket
and HTTP POST JSON-RPC 2.0, so it is a drop-in alternative for any MCP-capable client that speaks
either transport.

```
┌──────────────────────────┐   WebSocket/MCP   ┌──────────────────────────────────┐
│  Any MCP-capable client   │ ─────────────────► │  request-guard-mcp-dotnet        │
│  or model adapter         │                   │  (.NET / ASP.NET Core / Kestrel) │
└──────────────────────────┘                   │                                  │
                                               │  ┌─────────────┐                 │
                                               │  │ Tool Registry│                 │
                                               │  └──────┬──────┘                 │
                                               │         │                        │
                                               │  ┌──────▼──────────────────┐     │
                                               │  │  Engines                │     │
                                               │  │  ├── RuleEngine         │     │
                                               │  │  ├── Scorer             │     │
                                               │  │  ├── Explain            │     │
                                               │  │  ├── Anomaly            │     │
                                               │  │  └── Policy             │     │
                                               │  └─────────────────────────┘     │
                                               │                                  │
                                               │  Integrations (optional)         │
                                               │  ├── Redis (cache/reputation)    │
                                               │  ├── PostgreSQL (decisions/FB)   │
                                               │  └── MaxMind GeoIP (MMDB)        │
                                               └──────────────────────────────────┘
```

## Request lifecycle

1. Client opens a WebSocket connection or sends an HTTP POST to `/mcp`.
2. Server authenticates the request using the `Authorization` header (Bearer scheme).
3. Client sends a JSON-RPC 2.0 message: `{ "jsonrpc": "2.0", "id": 1, "method": "classify", "params": {...} }`.
4. Server acquires a `SemaphoreSlim` permit (global concurrency control).
5. The tool is dispatched through the registry with a per-tool `CancellationTokenSource` timeout.
6. The rule engine extracts signals and the scorer produces the verdict.
7. Backend-dependent tools query Redis, PostgreSQL, or MaxMind as required.
8. The response is serialized over the selected transport and metrics/traces are emitted.

## Solution layout

| Project | Responsibility | Rust equivalent |
|---|---|---|
| `RequestGuardMcp.Core` | Config, shared runtime state (`AppState`, `BuildInfo`), `AppErrorException`, response DTOs, the `health` computation, engines (rules/scorer/explain/anomaly/policy, phase 3), shared utilities | `src/config.rs`, `src/state.rs`, `src/error.rs`, `src/models/`, `src/tools/health.rs`, `src/engines/`, `src/util/` |
| `RequestGuardMcp.Mcp` | JSON-RPC 2.0 types, WebSocket + HTTP transports, tool registry, auth, concurrency/limits, endpoint mapping | `src/mcp/`, `src/auth.rs`, `src/limits.rs` |
| `RequestGuardMcp.Tools` | The MCP tool implementations (`health`, `model_info` so far; 22 total planned) | `src/tools/` |
| `RequestGuardMcp.Integrations` | Redis, PostgreSQL, MaxMind adapters behind interfaces (phase 4) | `src/integrations/` |
| `RequestGuardMcp.Host` | ASP.NET Core `Program.cs` composition root: config binding/validation, DI wiring, Kestrel binding, telemetry — the executable | `src/main.rs`, `src/telemetry.rs` |

Redis, PostgreSQL, and MaxMind stay **optional at runtime**, selected via configuration flags, so a
dependency-free build/run path exists for the backend-free tools — the same shape as the Rust
project's `redis-integration`/`postgres-integration` Cargo features.

## Concurrency model

- **Async runtime**: the .NET thread pool via `async`/`await` (Kestrel's default hosting model).
- **Global concurrency**: `System.Threading.SemaphoreSlim` with a configurable limit (default 256).
- **Per-tool timeout**: each dispatch is wrapped with a linked `CancellationTokenSource`.
- **Cache**: `Microsoft.Extensions.Caching.Memory` in-process bounded cache.
- **Backpressure**: the semaphore blocks new requests when all permits are taken.

## Observability (planned, Phase 6)

- **Logging**: `Microsoft.Extensions.Logging` with the JSON console formatter.
- **Metrics**: `System.Diagnostics.Metrics` instruments exported as Prometheus text via
  `OpenTelemetry.Exporter.Prometheus.AspNetCore`, at `/metrics`:
  - `mcp_requests_total{tool, status}`
  - `mcp_request_duration_seconds{tool}` (histogram)
  - `mcp_active_connections`
  - `mcp_tool_errors_total{tool, error_code}`
- **Health**: `GET /health` (liveness), `GET /ready` (readiness).
- **OTLP**: optional batched OpenTelemetry trace export via configuration.

## Transport scope

WebSocket and HTTP POST JSON-RPC are the only supported MCP transports, matching the Rust original.
No gRPC listener is planned.

## Delivery phases

See the repository's commit history and `docs/DO-178C-ASSURANCE-POLICY.md` traceability
expectations. In summary: (1) scaffolding and governance — **done**; (2) MCP protocol core with no
backends (JSON-RPC types, WS/HTTP transports, Bearer auth, concurrency/timeout, `health`/`model_info`
tools) — **done**; (3) engines and backend-free tools (rule engine, scorer, explain engine,
in-process cache, `classify`/`batch_classify`/`explain`/`score_breakdown`/`validate_payload`/
`feature_flags`/`warmup`/`redact_preview`/`config_snapshot`/`self_test`) — **done**;
(4) Redis/PostgreSQL/MaxMind integrations; (5) remaining tools plus TLS/UA handling;
(6) observability; (7) Docker/Kubernetes deployment; (8) tests to parity and release automation.
