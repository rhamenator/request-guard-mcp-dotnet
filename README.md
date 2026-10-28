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
qualifies the JSON-response HTTP path and process-local `--stdio`. Negotiation/auth/origin
checks are exercised separately. SSE, sessions and OAuth discovery are not implemented.
No official .NET release candidate or stable release has been published.

The .NET port implements all 23 sibling Rust tool contracts, HTTP/WebSocket JSON-RPC transports,
Bearer authentication, bounded concurrency/timeouts, two-level caching, PostgreSQL persistence and
reporting, Redis threat/canary/queue services, MaxMind enrichment, HMAC-attested JA3/JA4 rules,
Prometheus metrics, optional OTLP traces, and container/Kubernetes deployment assets.

Behavioral and wire compatibility is continuously checked against the pinned Rust baseline by a
black-box differential suite. It exercises every tool, error envelopes, disabled features,
authentication, TLS attestation, both transports, all supported configuration formats, caller cache
and PostgreSQL isolation, privacy-minimized replay snapshots, transactional feedback replacement,
timeouts, Redis, PostgreSQL, and checksum-pinned official MaxMind test databases.

The assurance baseline is DO-178C-inspired and appropriate to a sole-maintainer project; it is not
a claim of certification or airborne suitability. Official pre-1.0 candidates require a durable
owner self-review record and disclose whether independent human review occurred; independent review
is not a publication blocker.

### Try it

```bash
cp .env.example .env
# replace every uncommented replace_me value in .env, then:
dotnet run --project src/RequestGuardMcp.Host
curl http://localhost:8085/health
curl -H "Authorization: Bearer your-strong-token" -X POST http://localhost:8085/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -d '{"jsonrpc":"2.0","id":1,"method":"classify","params":{"user_agent":"GPTBot/1.0","path":"/"}}'
```

### Standard MCP calls

Send authenticated POST requests to `/mcp` with `Content-Type: application/json` (UTF-8)
and `Accept: application/json, text/event-stream`. Unsupported media/charset returns 415;
missing/unacceptable Accept returns 406. After negotiation, send
`MCP-Protocol-Version: 2025-06-18`; a present unsupported, empty or repeated version returns
400. Missing version remains a compatibility path for initialization and existing direct
clients. Responses are JSON only; no SSE stream or server-side session is created.
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

### HTTP authentication and browser origins

The native/default listener binds `127.0.0.1`; set `MCP__HOST=0.0.0.0` explicitly for a
remote service. Container and Kubernetes examples retain explicit all-interface binding.
Use TLS termination and restrict direct service access for remote Bearer traffic.

One Authorization header is required when auth is enabled. The Bearer scheme is case
insensitive; tokens are case sensitive and use RFC6750's ASCII token alphabet (letters,
digits, `-._~+/`, optional trailing `=`). Repeated or malformed headers return 401 with a
Bearer challenge; a well-formed unknown token returns 403. Malformed configured tokens
fail startup. Token bytes are compared with `FixedTimeEquals`; caller HMAC scopes are unchanged.

HTTP POST and WebSocket upgrades validate any present Origin even when auth is disabled.
The default empty allowlist rejects every present Origin with 403; machine clients may
omit Origin. To allow a specific browser origin, configure for example:

```bash
MCP__AUTH__ALLOWED_ORIGINS__0=https://client.example
```

Entries must be bare HTTP(S) origins, with no slash/path, query, fragment, userinfo or
wildcard. Scheme/host case and default ports are normalized; other schemes/ports/hosts
remain distinct. Repeated, malformed and opaque `Origin: null` values are rejected.
An allowed origin still requires Bearer authentication. Origin checking does not enable
CORS, implement OAuth discovery or replace authentication; proxies must preserve the
original Origin.

### Transport output and WebSocket budgets

WebSocket upgrades negotiate `mcp` when requested; clients without a subprotocol remain
supported. `MCP__LIMITS__WEBSOCKET_IDLE_TIMEOUT_SECS` defaults to 60 and applies to receiving
one whole message, including every fragment. An idle peer or unfinished/trickled message
cannot reset that budget with partial frames. `limits.max_request_bytes` applies across
all fragments. Responses and close frames use a separate
`MCP__LIMITS__WEBSOCKET_WRITE_TIMEOUT_SECS` budget (default 10). Deadline expiry aborts the
socket and releases its connection slot; a peer close is answered without waiting for a
second close frame. Text and UTF-8 binary requests share the same dispatcher and auth scope.

HTTP MCP, health/readiness, metrics and auth-error output use
`MCP__LIMITS__HTTP_RESPONSE_WRITE_TIMEOUT_SECS` (default 10), starting when output begins.
If the writer ignores cancellation, the independent wait still ends and the connection
is aborted. A partially written response is never followed by a second JSON envelope.
Body-read, tool-execution and optional-cache budgets remain separate. Timeout values
must be positive and fit the cancellation timer's supported range (at most 4294967 seconds).

These controls bound tested I/O paths; full-duration TLS/ALPN, slow-consumer and reverse
proxy/IIS qualification remain separate work. The WebSocket test matrix uses a shorter
configured receive budget for deterministic expiry/recovery tests.

### Process-local stdio

```bash
dotnet src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll --stdio
```

Configure a subprocess-capable MCP client to launch that command with an absolute DLL
path. Build the host first with the pinned .NET SDK; a runtime or self-contained native
bundle is required on the client machine. Stdio opens no TCP listener, needs no Bearer
header or `AUTH_TOKENS`, and is authorized by ownership of the launched process. It
supports the same 23 tools, configuration, and feature flags as HTTP. Redis and MaxMind remain
optional in this mode. PostgreSQL is rejected because its durable rows cannot safely use stdio's
process-random caller identity; use authenticated HTTP/WebSocket for persistence tools.
Supply supported backend credentials through the child environment/secret manager; the subprocess
has the launching user's file/network permissions and configured backend costs.

Each process gets a distinct cache scope. Send one UTF-8 JSON-RPC message per LF/CRLF
line, within `limits.max_request_bytes` bytes before LF. Output contains JSON messages
only; diagnostics go to stderr. Oversized lines are drained through LF and rejected
with -413; invalid UTF-8/JSON and unterminated EOF messages return -32700. Valid
notifications produce no output. EOF exits cleanly; Ctrl+C/SIGTERM cancels pending work.
Output writes have a five-second deadline; a stuck reader causes process failure.
This is a sequential transport: requests finish in input order, and notification-based
in-flight cancellation is not implemented. SDK/process and installed-binary evidence runs on
Linux, Windows, Intel macOS and arm64 macOS hosted runners. OS-service and additional
client-specific qualification remain open.

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

## Native development bundles

No official release-candidate bundles are published yet. Build a development archive from the **committed**
Git revision with Python 3.12+ and the exact SDK in `global.json`:

```bash
python3 scripts/native_bundle.py --rid linux-x64 --output-dir out/native
```

Supported bundle targets are `linux-x64` (glibc), `win-x64`, `osx-x64` and `osx-arm64`.
Linux musl/Alpine and Linux/Windows arm64 bundles are not provided. Each bundle includes
its self-contained executable, setup/security documentation, placeholder `.env.example`
and a source/RID/SDK manifest with file checksums. A sibling SHA-256 file identifies the
archive. Extract the entire top-level folder, configure secrets through the environment,
and launch `request-guard-mcp-dotnet` (`.exe` on Windows), adding `--stdio` for a subprocess
client. A .NET installation is not required; operating-system native runtime libraries
still are.

Official release candidates also provide DEB and RPM installers for Linux x64, MSI for Windows x64,
and PKG installers for Intel and arm64 macOS. They are unsigned, do not register a service, and are
qualified for clean install, installed payload integrity, pinned official Python MCP SDK stdio use,
synthetic-version upgrade, uninstall and residue on matching disposable systems. Linux's RPM also
passes a raw stdio lifecycle in a digest-pinned Fedora container. See
[native installer instructions](docs/INSTALLERS.md), including package-manager dependencies, paths,
uninstall commands and the macOS notarization limitation.

The `ReleaseBundle` build profile uses separate committed `packages.release.lock.json`
files including all four RIDs and the pinned single-file task. Dependency drift fails
locked restore. The builder archives tracked source before publishing, so ignored local
configuration, credentials, build output and uncommitted changes cannot enter a bundle.
No existing archive is overwritten. To update release locks intentionally, review the
SDK/package changes, run `dotnet restore src/RequestGuardMcp.Host/RequestGuardMcp.Host.csproj
-p:ReleaseBundle=true`, review all five release lock files and rerun qualification.

The Native Bundle Qualification workflow builds `VersionPrefix` candidates, tests fresh extractions,
builds platform installers only from those qualified archives, and exercises the full installer
lifecycle on matching Linux, Windows, Intel macOS and arm64 macOS runners. Its seven-day Actions
artifacts are development candidates with exact archive/installer checksum and source records; this
does not publish an official release candidate, stable release or `latest`. See
[verification](docs/VERIFICATION.md) for scope and commands.

## Official release-candidate gate

Official release-candidate publication is manual and fail closed as described in
[the release-readiness review](docs/RELEASE-READINESS-REVIEW.md). The workflow accepts only a
`0.MINOR.PATCH` matching `VersionPrefix`, only from the exact current protected `main` commit, and
only after every named current-candidate CI, security, Redis, native-bundle and native-installer job
succeeds. It also requires a closed durable owner readiness issue, an exact approval comment and an
explicit statement of whether independent human review occurred. Self-review and automation are
never represented as independent review.

The workflow verifies exact qualified artifacts, runs the baseline and container smoke/scan, rejects
an existing tag/release/container version, then publishes an immutable versioned container and
GitHub prerelease containing the native archives and qualified unsigned installers. It never updates
`latest`. Stable 1.0 publication and signed/notarized packages are outside the current workflow.

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
- [docs/RELEASE-READINESS-REVIEW.md](docs/RELEASE-READINESS-REVIEW.md) — owner review, evidence and
  publication rules for an official pre-1.0 release candidate
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

When authentication and PostgreSQL are enabled together, `CACHE_SCOPE_HMAC_KEY` is required and
must contain at least 32 non-placeholder bytes. It keeps server-derived caller scopes stable across
restarts and replicas. PostgreSQL decision keys are SHA-256 digests of that scope plus the public
request ID; replay, feedback, drift, and calibration queries also require the matching scope.
Existing legacy rows without a caller scope remain available for offline migration but are not
visible through MCP tools.

For PostgreSQL across a host boundary, use `SSL Mode=VerifyFull` and a trusted CA; `Require`
encrypts without authenticating the server. For example, append
`SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres-ca.crt` to the Npgsql connection
string and mount that CA read-only. The local Compose URL is development-only. See the
[TLS and recovery gate](docs/POSTGRES-TLS-RECOVERY.md) for permissions, limitations, and a
reproducible trusted/untrusted/hostname/outage test.

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
startup settings and are not yet listed by that tool. Snapshots are always secret-safe:
`redact_secrets` may be true or omitted, while false is rejected. A configured OTLP endpoint
is reported only as `[REDACTED]`; an absent endpoint is null. No outbound enforcement is added.

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
