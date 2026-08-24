# Architecture

## Status

This document describes the architecture for `request-guard-mcp-dotnet`, a from-scratch C#/.NET
port of [`request-guard-mcp`](https://github.com/rhamenator/request-guard-mcp) (Rust), as the design
record required by [`DO-178C-ASSURANCE-POLICY.md`](DO-178C-ASSURANCE-POLICY.md). All planned
delivery phases are implemented; the maintained requirement-to-evidence mapping is in
[`REQUIREMENTS-TRACEABILITY.md`](REQUIREMENTS-TRACEABILITY.md).

**Implementation refinements from the original sketch:** `AppState`, `BuildInfo`, and the shared
`HealthCheck` computation live in `RequestGuardMcp.Core` rather than the Host project — `Core` is
the one project every other project can depend on, and both the raw `GET /health` endpoint (Mcp)
and the `health` tool (Tools) need the same logic, so it has to sit below both. The application
error type is named `AppErrorException` (not `AppError`) to satisfy .NET's naming convention for
`Exception` subclasses (CA1710). The `model_info` tool reports exactly the tools actually
registered in the `ToolRegistry`; the completed registry contains all 23 contracts and reports
truthful feature/backend availability.

`AnomalyEngine` and `Policy` (src/engines/anomaly.rs, src/engines/policy.rs)
are **not** ported — `grep` across the Rust source confirms neither is referenced anywhere outside
its own module; they are dead code in the source project, and porting unused code would violate
this project's own "no code beyond what's needed" standard. `RuleEngine.Evaluate` and
`Scorer.Score` are static methods (both engines are pure functions over their input, with no
instance state — the .NET analyzers flag stateless instance methods as CA1822, and the fix is
correct here, not a suppression). The in-process cache (`ICacheStore`/`InMemoryCacheStore`) is a
hand-rolled bounded TTL cache rather than a port of the `moka` crate: it's simple enough to write
directly on `ConcurrentDictionary`, avoiding a dependency for something this small, at the cost of
FIFO eviction instead of moka's LRU — swappable later behind the interface if that ever matters.
TLS fingerprints are validated and normalized, but affect scoring and cache identity only after a
fresh request-bound HMAC attestation verifies. The attestation is cleared before cache/persistence.

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
                                               │  │  └── TLS rules          │     │
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
| `RequestGuardMcp.Core` | Config, shared runtime state, errors/DTOs, health, rules/scoring/explanation, cache, TLS attestation, metrics | `src/config.rs`, `src/state.rs`, `src/error.rs`, `src/models/`, `src/tools/health.rs`, `src/engines/`, `src/util/` |
| `RequestGuardMcp.Mcp` | JSON-RPC 2.0 types, WebSocket + HTTP transports, tool registry, auth, concurrency/limits, endpoint mapping | `src/mcp/`, `src/auth.rs`, `src/limits.rs` |
| `RequestGuardMcp.Tools` | All 23 MCP tool implementations and complete registry factory | `src/tools/` |
| `RequestGuardMcp.Integrations` | Redis, PostgreSQL, and MaxMind adapters behind interfaces | `src/integrations/` |
| `RequestGuardMcp.Host` | ASP.NET Core `Program.cs` composition root: config binding/validation, DI wiring, Kestrel binding, telemetry — the executable | `src/main.rs`, `src/telemetry.rs` |

Redis, PostgreSQL, and MaxMind stay **optional at runtime**, selected via configuration flags, so a
dependency-free build/run path exists for the backend-free tools — the same shape as the Rust
project's `redis-integration`/`postgres-integration` Cargo features.

## Concurrency model

- **Async runtime**: the .NET thread pool via `async`/`await` (Kestrel's default hosting model).
- **Global concurrency**: `System.Threading.SemaphoreSlim` with a configurable limit (default 256).
- **Per-tool timeout**: each dispatch is wrapped with a linked `CancellationTokenSource`.
- **Cache**: bounded in-process TTL cache plus optional shared Redis cache.
- **Backpressure**: the semaphore blocks new requests when all permits are taken.

## Observability

- **Logging**: `Microsoft.Extensions.Logging` with the JSON console formatter.
- **Metrics**: thread-safe counters and histograms rendered as Prometheus text at `/metrics`:
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

All eight phases are implemented: governance, protocol, engines, integrations, complete tool/TLS/UA
handling, observability, deployment, and parity/release verification. Release certification is not
claimed; the independent human gate remains documented separately.
