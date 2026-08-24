# Architecture

## Status

This document describes the **target** architecture for `request-guard-mcp-dotnet`, a from-scratch
C#/.NET port of [`request-guard-mcp`](https://github.com/rhamenator/request-guard-mcp) (Rust). It is
written ahead of implementation, as the design record required by
[`DO-178C-ASSURANCE-POLICY.md`](DO-178C-ASSURANCE-POLICY.md); sections describe intent, not yet-built
behavior, until the corresponding delivery phase lands. Each phase's pull request updates this file
to reflect what was actually implemented.

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
| `RequestGuardMcp.Core` | Domain models, engines (rules/scorer/explain/anomaly/policy), shared utilities | `src/models/`, `src/engines/`, `src/util/` |
| `RequestGuardMcp.Mcp` | JSON-RPC 2.0 types, WebSocket + HTTP transports, tool registry, auth, concurrency/limits | `src/mcp/`, `src/auth.rs`, `src/limits.rs` |
| `RequestGuardMcp.Tools` | The 22 MCP tool implementations | `src/tools/` |
| `RequestGuardMcp.Integrations` | Redis, PostgreSQL, MaxMind adapters behind interfaces | `src/integrations/` |
| `RequestGuardMcp.Host` | ASP.NET Core `Program.cs`, DI wiring, configuration, telemetry — the executable | `src/main.rs`, `src/config.rs`, `src/state.rs`, `src/telemetry.rs` |

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

See the repository's pull request history and `docs/DO-178C-ASSURANCE-POLICY.md` traceability
expectations. In summary: (1) scaffolding and governance, (2) MCP protocol core with no backends,
(3) engines and backend-free tools, (4) Redis/PostgreSQL/MaxMind integrations, (5) remaining tools
plus TLS/UA handling, (6) observability, (7) Docker/Kubernetes deployment, (8) tests to parity and
release automation.
