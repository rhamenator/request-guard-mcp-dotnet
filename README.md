# request-guard-mcp-dotnet

[![CI](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/workflows/ci.yml)
[![License: GPL v3+](https://img.shields.io/badge/License-GPLv3%2B-blue.svg)](LICENSE)

A C#/.NET **Model Context Protocol (MCP) server** for request risk classification, enrichment, and
abuse-signal analysis — a from-scratch .NET port of
[`request-guard-mcp`](https://github.com/rhamenator/request-guard-mcp) (Rust), aiming for the same
tool surface and behavior over the same WebSocket/HTTP JSON-RPC 2.0 transports.

## Status

Compatibility evidence currently targets an August 2026 Rust revision, not current Rust main or
v0.1.5. Current parity work and unverified gaps are tracked in the
[parity matrix](docs/PARITY-MATRIX.md). The `2025-06-18` lifecycle methods (`initialize`, `ping`, `tools/list`, `tools/call`)
work alongside direct HTTP/WebSocket JSON-RPC calls. The pinned official Python SDK
qualifies the JSON-response HTTP path and process-local `--stdio`. Full HTTP negotiation,
browser-origin policy, SSE, sessions and OAuth discovery remain unqualified.
No stable .NET release has been published.

The .NET port implements all 23 sibling Rust tool contracts, HTTP/WebSocket JSON-RPC transports,
Bearer authentication, bounded concurrency/timeouts, two-level caching, PostgreSQL persistence and
reporting, Redis threat/canary/queue services, MaxMind enrichment, HMAC-attested JA3/JA4 rules,
Prometheus metrics, optional OTLP traces, and container/Kubernetes deployment assets.

Behavioral and wire compatibility is continuously checked against the pinned Rust baseline by a
black-box differential suite. It exercises every tool, error envelopes, disabled features,
authentication, TLS attestation, both transports, all supported configuration formats, caller cache
isolation, timeouts, Redis, PostgreSQL, and checksum-pinned official MaxMind test databases.

The assurance baseline is DO-178C-inspired and appropriate to a sole-maintainer project; it is not
a claim of certification or airborne suitability. Independent review remains required before the
first stable release.

### Try it

```bash
cp .env.example .env
# replace every uncommented replace_me value in .env, then:
dotnet run --project src/RequestGuardMcp.Host
curl http://localhost:8085/health
curl -H "Authorization: Bearer your-strong-token" -X POST http://localhost:8085/mcp \
  -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","id":1,"method":"classify","params":{"user_agent":"GPTBot/1.0","path":"/"}}'
```

### Standard MCP calls

Send authenticated POST requests to `/mcp` with `Content-Type: application/json`.
`initialize` counteroffers `2025-06-18`; `tools/list` returns 23 sorted definitions and
input schemas. For example, after initialization:

```json
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"classify","arguments":{"user_agent":"GPTBot/1.0","path":"/"}}}
```

Successful `tools/call` results include text `content`, the matching JSON
`structuredContent`, and `isError: false`. Tool failures retain the existing JSON-RPC
application error codes. Direct methods such as `classify` and `tools/classify` retain
their unwrapped results and Bearer permissions. No extra server-side session is created;
HTTP requests authenticate independently. Unknown notifications are ignored.

Migration: requests require `jsonrpc: "2.0"`, a string or integer ID (never null), and
object `params` when present. Invalid envelopes return `-32600`; invalid JSON returns
`-32700`. Responses now omit the unused `result`/`error` member and absent error `data`.
Accepted HTTP notifications/responses return empty **202**, and non-upgrade GET `/mcp`
returns **405** after authentication. Clients relying on the older 204 or redundant null
envelope fields must update. Tool payload null fields are unchanged.

Reproduce the SDK and transport contract with the commands in
[verification](docs/VERIFICATION.md). No .NET companion skill is distributed in this repository.

### Process-local stdio

```bash
dotnet src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll --stdio
```

Configure a subprocess-capable MCP client to launch that command with an absolute DLL
path. Build the host first with the pinned .NET SDK; a runtime or self-contained native
bundle is required on the client machine. Stdio opens no TCP listener, needs no Bearer
header or `AUTH_TOKENS`, and is authorized by ownership of the launched process. It
supports the same 23 tools, configuration, feature flags and optional backends as HTTP.
Supply backend credentials through the child environment/secret manager; the subprocess
has the launching user's file/network permissions and configured backend costs.

Each process gets a distinct cache scope. Send one UTF-8 JSON-RPC message per LF/CRLF
line, within `limits.max_request_bytes` bytes before LF. Output contains JSON messages
only; diagnostics go to stderr. Oversized lines are drained through LF and rejected
with -413; invalid UTF-8/JSON and unterminated EOF messages return -32700. Valid
notifications produce no output. EOF exits cleanly; Ctrl+C/SIGTERM cancels pending work.
Output writes have a five-second deadline; a stuck reader causes process failure.
This is a sequential transport: requests finish in input order, and notification-based
in-flight cancellation is not implemented. SDK/process evidence is currently Linux;
Windows/macOS packaged client qualification remains open.

## Why a .NET port?

The sibling Rust server defines the compatibility contract. This project re-implements that surface
on ASP.NET Core, using external packages only for Redis, PostgreSQL, MaxMind, OpenTelemetry, and the
test runner. See [docs/architecture.md](docs/architecture.md) for the dependency budget.

## Solution layout

```text
RequestGuardMcp.slnx
src/
  RequestGuardMcp.Core/          # config, AppState, errors, response models, health check, engines
  RequestGuardMcp.Mcp/           # JSON-RPC types, WS+HTTP transport, tool registry, auth, limits
  RequestGuardMcp.Tools/         # all 23 MCP tool implementations
  RequestGuardMcp.Integrations/  # Redis, Postgres, MaxMind adapters behind interfaces
  RequestGuardMcp.Host/          # ASP.NET Core Program.cs, DI wiring — the executable
tests/
  RequestGuardMcp.Tests/         # unit + contract tests (xUnit)
```

## Development

```bash
dotnet restore
dotnet build --configuration Release
dotnet test
```

Or, equivalently:

```bash
make ci
```

With the sibling Rust repository checked out next to this one, run the compatibility gates with:

```bash
make parity                 # protocol, tools, auth, TLS, and configuration
make parity-integration     # also disposable Redis/PostgreSQL and MaxMind fixtures
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full workflow.

## Tools

| Classification and explanation | Enrichment and security | Persistence and operations |
|---|---|---|
| `classify`, `batch_classify`, `explain` | `enrich_ip`, `enrich_asn`, `enrich_ua` | `feedback`, `replay_decision` |
| `score_breakdown`, `validate_payload` | `threat_lookup`, `canary_eval` | `drift_report`, `calibration_report` |
| `model_info`, `feature_flags` | `abuse_pattern_match`, `redact_preview` | `health`, `queue_status`, `warmup`, `config_snapshot`, `self_test` |

Backend-dependent tools remain registered for wire compatibility. They report
`INTEGRATION_UNAVAILABLE` when their feature/backend is absent and `UPSTREAM_ERROR` when a
configured dependency fails; they do not return synthetic success data.

## Documentation

- [docs/architecture.md](docs/architecture.md) — target architecture and delivery phases
- [docs/DO-178C-ASSURANCE-POLICY.md](docs/DO-178C-ASSURANCE-POLICY.md) — the change-control and
  verification baseline this repository follows
- [docs/RELEASE-READINESS-REVIEW.md](docs/RELEASE-READINESS-REVIEW.md) — the independent human
  review gate required before a first stable release
- [docs/REQUIREMENTS-TRACEABILITY.md](docs/REQUIREMENTS-TRACEABILITY.md) — requirements mapped to
  implementation and verification evidence
- [docs/VERIFICATION.md](docs/VERIFICATION.md) — automated, integration, and deployment checks
- [docs/VERIFICATION-EVIDENCE.md](docs/VERIFICATION-EVIDENCE.md) — current release-candidate gate
  results
- [docs/KNOWN-LIMITATIONS.md](docs/KNOWN-LIMITATIONS.md) — bounded compatibility and assurance
  limitations
- [docs/security-model.md](docs/security-model.md) and [docs/runbook.md](docs/runbook.md) — trust
  boundaries and operations

## Deployment

```bash
docker build -f docker/Dockerfile -t request-guard-mcp-dotnet:local .
# Set AUTH_TOKENS, CACHE_SCOPE_HMAC_KEY, REDIS_PASSWORD, and POSTGRES_PASSWORD first.
docker compose -f docker/docker-compose.yml up --build
```

Kubernetes examples are under `deploy/k8s`. Copy `secret.example.yaml` to `secret.yaml`, replace
every placeholder, and keep that generated file out of version control.

## Community

- [GitHub Discussions](https://github.com/rhamenator/request-guard-mcp-dotnet/discussions) for
  questions, design ideas, and anything not yet a concrete bug or proposal
- [Issue forms](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/new/choose) for bugs,
  feature proposals, documentation problems, and support questions
- [SECURITY.md](SECURITY.md) for private vulnerability reporting

## License

GPL-3.0-or-later — see [LICENSE](LICENSE).

### Overload behavior

When all configured tool execution permits are occupied, a new tool request returns the
JSON-RPC application error `-429` / `RATE_LIMIT_EXCEEDED` immediately. Clients should use
bounded retries with backoff. Unknown tool names use one `__unknown__` telemetry label and
are rejected before Redis operation bookkeeping. These tool-level controls do not yet
establish current Rust parity for TCP/HTTP/WebSocket connection and slow-client limits.

### Connection resource limits

The Kestrel host caps admitted TCP connections, including upgraded WebSockets, at
`MCP__LIMITS__MAX_CONNECTIONS` (default 256). A connection without a first complete
request is aborted after `HTTP_HEADER_TIMEOUT_SECS` (10) plus a 250 ms response grace;
HTTP/1 header parsing also has Kestrel's header deadline. MCP POST body reads have
`HTTP_BODY_TIMEOUT_SECS` (30) and return 408 on timeout. Use the full `MCP__LIMITS__`
prefix for each setting, as shown in `.env.example`; all values must be positive.

HTTP/2 connections have a 60-second idle deadline after completed work (configured by
`MCP__LIMITS__HTTP2_IDLE_TIMEOUT_SECS`), a fixed 120-second age budget starting with
the first request, 16 concurrent streams and at most 128 admitted requests. Requests
beyond the count cap receive 503 before authentication or tool dispatch. Limit expiry
requests Kestrel's graceful close/GOAWAY; the socket is aborted after a drain budget of
body timeout + the larger tool timeout + 2 seconds. Clients must tolerate reconnects.

These controls follow Rust development-main PRs #160/#162, not the public v0.1.5
contract. Kestrel's default cleartext listener uses HTTP/1; it does not auto-detect
HTTP/2 prior knowledge on the same cleartext port as Rust does. The socket tests use
an explicit HTTP/2 listener. TLS/ALPN, proxy/IIS deployments, slow response consumers
and full current-Rust transport qualification remain unverified. Existing
`config_snapshot` keeps its historical schema; these new limits are configured via
startup settings and are not yet listed by that tool. No outbound enforcement is added.

### Best-effort Redis operation records

Redis queue start/finish records each have a separate 500 ms wait budget, even when a
backend ignores cancellation. Tool execution retains its configured timeout after start
bookkeeping. Consequently the tool timeout is not a total wall-clock response SLA:
bookkeeping can add up to one second, plus transport/scheduling overhead. A missing start
record skips its matching finish record. Caller cancellation stops waiting for a start
record instead of dispatching new work.

These bounds follow the merged Rust #163 fix for issue #159. Abandoned
StackExchange.Redis operations may complete later; queue telemetry is best-effort.
Optional cache waits have separate bounds described below.

### Optional Redis cache waits

Classification gives each distributed-cache read and write at most 500 ms, inside the
tool execution budget. A cache-only timeout falls back to the local cache or newly
computed decision; caller cancellation still propagates. PostgreSQL persistence and
backend-dependent tools retain their error contracts. This does not bound every Redis
lookup, health check or network reconnect.

The [Redis outage gate](docs/REDIS-OUTAGE-QUALIFICATION.md) runs a real disposable Redis
backend through healthy, paused, stopped and recovered phases, with cold cache keys,
expected classifier responses, external wall-clock limits, and an old-build negative
control. See the exact workflow/PR evidence for qualification status; adding a gate is
not a claim that it has passed or that all Rust behavior/clients/platforms match.
