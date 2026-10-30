# Verification Plan and Evidence

## Automated baseline

Run from a clean checkout with the SDK selected by `global.json`:

```bash
make ci
```

This performs locked restore, formatting verification, Release build with compiler/analyzer
warnings as errors, all xUnit tests, and a transitive vulnerability query. CI additionally runs
CodeQL, dependency review, and assurance-policy checks. Preserve the command log and source commit
with release evidence.

Run the pinned cross-language contract suite from a checkout adjacent to the Rust repository:

```bash
make parity
make parity-integration
```

`tests/parity/rust-baseline.txt` is the reviewed contract revision. The runner compares complete
tool payloads and error codes while normalizing generated IDs/times/latencies, server ports,
and language build metadata. The historical Rust pin's redundant null envelope members and its
`[REDACTED]` marker for an absent OTLP endpoint are explicit baseline-only migration exceptions:
.NET must omit the unused envelope branch and follow current Rust's null-for-absent snapshot.
The runner also expects historical Rust notification 204 versus .NET 202. Regression tests ensure
these exceptions cannot hide a candidate emitting both branches, discard application null fields,
or expose a configured endpoint. Its core phase covers every one of the 23 tools, malformed/missing input, disabled
features, HTTP and text/binary WebSocket transport, strict UTF-8, auth, TLS attestation, and all six
Rust config syntaxes through seven extensions (including YAML's `.yml` alias) plus dotenv. For each
configuration input it independently checks the complete stable redacted snapshot, including limits,
feature flags, backend settings, telemetry, and TLS policy. The integration phase launches pinned
Redis/PostgreSQL containers, downloads checksum-pinned Apache-licensed MaxMind upstream fixtures,
tests persistence/replay/reports/reputation/canaries/queues, verifies two caller cache and database
scopes, rejects cross-caller replay/feedback/report access, checks feedback invalidation on decision
replacement, inspects privacy-minimized hashed-key rows, and forces a PostgreSQL lock to prove the
tool deadline.
The integration phase assigns Rust and .NET distinct Redis prefixes. This is an intentional
deployment requirement: cached verdicts include runtime-specific build domains and are not a
cross-runtime serialization protocol. Unit tests separately prove build/TLS-policy partitioning,
rule-input-only identity, query/fragment equivalence, caller isolation, and ignored Unicode/numeric
metadata.

The scoped current-Rust jobs additionally require both v0.1.5 and the pinned current-main
reference to reject `config_snapshot(redact_secrets=false)`, return null for an absent OTLP
endpoint in a redacted snapshot, and match an independently specified complete stable default
snapshot after volatile fields are normalized. They also launch both implementations through a
broad rejected-startup matrix covering credentials, origins, cache and PostgreSQL keys, TLS
attestation age/keys/fingerprints, telemetry, Redis/PostgreSQL values, GeoIP paths, and common
resource limits. Current main alone gates its embedded-placeholder and HTTP/2-idle extensions.
The same running-build job checks 82 independently expected shared classifier cases against both
references and 83 additional current-main cases. Coverage includes every maintained AI/scraper
marker, word boundaries, header and method behavior, bounded percent decoding, the expanded
sensitive-route grammar, and ordinary-route/query/fragment false-positive controls. It compares the
complete classification result after removing only measured latency and runtime build metadata.
This security behavior intentionally supersedes the older differential pin.

PostgreSQL transport trust and running-backend recovery have a separate disposable gate:

```bash
python3 tests/parity/postgres_tls_recovery.py \
  --dotnet-dll src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll
```

It proves `VerifyFull` trusted-CA success, untrusted-CA and host-name rejection, an encrypted
session observed by PostgreSQL, bounded truthful failures during pause/stop, and replay recovery
after unpause/restart without restarting the MCP server. See
[`POSTGRES-TLS-RECOVERY.md`](POSTGRES-TLS-RECOVERY.md) for prerequisites, resource limits,
required observations, and exclusions.

## MCP lifecycle and envelope qualification

```bash
python3 -m venv /tmp/request-guard-sdk
/tmp/request-guard-sdk/bin/pip install -r tests/protocol/requirements.txt
dotnet build RequestGuardMcp.slnx --configuration Release
/tmp/request-guard-sdk/bin/python tests/protocol/run.py \
  --dotnet-dll src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll
python3 -m unittest discover -s tests/parity -p test_contract_envelopes.py
```

The runner starts a fresh loopback-only build with synthetic authentication, a temporary
working directory and no configured backends. It terminates the host even on failure. The
version-pinned official Python SDK `mcp==1.30.0` and pinned transitive dependencies qualify
initialization/version counteroffer, initialized notification, ping, 23 JSON Schemas and one
representative standard call for every tool. Calls are compared to direct contracts, including
truthful errors for unavailable backends and unchanged application error codes. It separately
checks 15 malformed envelope families, the duplicate-property disposition and invalid JSON over
HTTP and WebSocket, ignored messages, one-way HTTP 400 behavior, recovery and authenticated
GET405. Valid string/integer IDs are retained on invalid-params errors. xUnit covers parser and
lifecycle cases. CI runs both.

Scope: JSON-response HTTP and custom WebSocket; this is not full Streamable HTTP compliance.
Stateful sessions, SSE and OAuth discovery are not implemented. Negotiation/origin
handling is separately exercised below. Pinned SDK packages are test-only and not shipped with the
server. Requalification is required after SDK/dependency or transport changes.

## HTTP negotiation, authentication and origins

The protocol runner additionally checks 18 real HTTP negotiation/auth/origin cases,
including duplicate headers, content-type/charset, positive-quality Accept, protocol
version, malformed/unknown tokens, default-denied origins and valid-request recovery.
Run it both without an origin allowlist and with process configuration:

```bash
/tmp/request-guard-sdk/bin/python tests/protocol/run.py \
  --dotnet-dll src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll \
  --allowed-origin https://client.test
```

`HttpProfileTests` starts actual loopback Kestrel listeners, verifies the HTTP and WS
origin boundary, duplicate Authorization rejection, allowed-origin authentication and
negotiation status codes. Parser/configuration tests cover malformed tokens/origins,
minimum token length, the 32-origin cap, authentication-disabled listener confinement,
case/default-port normalization and fail-fast configuration. The exact-reference Rust jobs launch
v0.1.5 and current main through this complete HTTP/WebSocket/official-SDK runner as well as the
broader rejected-startup matrix described above, including
malformed and zero TLS attestation age rather than accepting an implicit default; current main also
gates embedded credential placeholders. The incomplete-body socket
test includes valid negotiation headers so it still exercises the 408 body deadline.
All repository clients/examples now send the required Accept header. The historical
pair runner explicitly binds both servers to loopback, rather than hiding the changed
.NET default through payload normalization; a default-configuration test checks it. Missing protocol
version is an explicit legacy/initialization compatibility path, not implicit support
for every MCP revision. The separate Nginx gate below qualifies TLS/ALPN and reverse proxying on
Linux. IIS, production-duration TLS and broader browser clients remain separate work in #25.

The runner asserts controlled differences rather than normalizing them away. .NET rejects
duplicate JSON properties, missing MCP media headers and duplicate Authorization headers, accepts
the case-insensitive Bearer scheme, and advertises only POST after a non-upgrade GET. The exact Rust
references use last-key-wins JSON, retain legacy missing-media compatibility, accept repeated equal
Authorization values, parse the scheme case-sensitively, and advertise their GET upgrade route.
These are scoped observations of the pinned executables, not requirements to weaken .NET.

For a locally built controlled Rust reference, substitute its executable directly; the runner
infers the Rust server identity and reference-specific asserted profile:

```bash
/tmp/request-guard-sdk/bin/python tests/protocol/run.py \
  --binary /absolute/path/to/request-guard-mcp --reference-rust
/tmp/request-guard-sdk/bin/python tests/stdio/run.py \
  --binary /absolute/path/to/request-guard-mcp --reference-rust
```

## Process-local stdio qualification

After building the host and installing the pinned SDK requirements above:

```bash
/tmp/request-guard-sdk/bin/python tests/stdio/run.py \
  --dotnet-dll src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll
```

The actual-process runner reserves the configured HTTP port to prove no network bind,
removes inherited server configuration and tokens, checks JSON-only stdout and stderr
diagnostics, oversized-line and invalid-UTF-8 recovery, EOF shutdown and termination
while waiting for input. The official SDK launches the server with `--stdio`, initializes,
lists all 23 tools, classifies and receives an expected tool error. `StdioTransportTests`
cover newline framing, invalid envelope/syntax handling, bounded input recovery,
unterminated EOF, independent stable subprocess scopes, and cancellation even when a
stream ignores it. The process runner also proves PostgreSQL configuration is rejected before
connection because durable rows cannot use the process-random stdio scope. CI runs the process
runner on Linux for .NET and both exact Rust references. The gate records the reference processes'
default SIGTERM exit and requires the .NET host's clean zero-status shutdown. The transport is sequential;
client cancellation notifications and OS service integration remain unqualified; portable
Windows/macOS SDK and installed-binary smoke are exercised by the native workflow.

CI separately runs the exact MCP Inspector CLI package against the same Release build:

```bash
python3 tests/stdio/inspector.py \
  --dotnet-dll src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll
```

The runner requires Node.js 22.19 or newer, pins Inspector 2.13.0 and verifies its published npm
integrity before execution. It requires the exact 23-tool list with descriptions and object input
schemas, then calls `health` and `classify` and checks their text/structured-content agreement and
expected classification signal. npm access is required to resolve and run the pinned package; no
server secret is passed on the command line. This evidence is Inspector-over-stdio only. Inspector
remote HTTP is unsupported because the server intentionally does not implement Streamable HTTP
sessions or SSE responses.

## WebSocket and output deadlines

```bash
/tmp/request-guard-sdk/bin/python tests/protocol/run.py \
  --dotnet-dll src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll \
  --allowed-origin https://client.test --ws-deadline-seconds 1
```

The running-build matrix verifies mcp subprotocol negotiation, UTF-8 split inside a
fragmented text message, idle and unfinished-message expiry, oversized binary rejection
and a fresh valid connection afterward. CI/native artifact qualification runs it with a
one-second configured receive budget. This is not a default 60-second endurance record.
`WebSocketDeadlineTests` verifies that cancellation-ignoring read/write/close operations
and indefinitely trickled fragments cannot retain the handler or active metric. Real
Kestrel tests expire an incomplete upgraded message and then recover the sole TCP permit.

`HttpResponseDeadlineTests` independently verifies that output writers ignoring
cancellation still end at the configured write budget and abort the response, while
caller cancellation is preserved. The production helper wraps MCP, health/readiness,
metrics and auth-error body writes. Configuration rejects nonpositive budgets and
values exceeding the cancellation timer's range. These tests do not establish every
slow-consumer/proxy/IIS behavior; those remain in #25.

CI separately opts into the real 120-second HTTP/2 age test:

```bash
REQUEST_GUARD_ENDURANCE=1 dotnet test \
  tests/RequestGuardMcp.Tests/RequestGuardMcp.Tests.csproj \
  --configuration Release --no-build \
  --filter FullyQualifiedName~ProductionDurationHttp2AgeRotatesRealConnection
```

It keeps a real Kestrel HTTP/2 connection active with requests every two seconds, requires the
connection identity to survive at least 115 seconds, and requires rotation by 135 seconds. This
uses the public `ConnectionResourceGuard` constructor and therefore its fixed 120-second production
budget. It is production-duration server evidence, but not packaged-artifact, throughput,
maximum-connection or leak evidence.

## TLS/ALPN reverse-proxy qualification

After building the Release host, run:

```bash
python3 tests/protocol/proxy_tls.py \
  --dotnet-dll src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll
```

The gate starts the backend on a reserved loopback port and an unprivileged Nginx process with an
ephemeral CA and server certificate. It requires untrusted-certificate rejection, trusted TLS 1.3,
ALPN-selected HTTP/2, HTTP/1.1 fallback, authenticated JSON-RPC, pre-dispatch auth/origin
rejections, an authenticated `mcp` WebSocket upgrade, and backend recovery. CI records the Nginx
version and installs the runner package only when it is absent. The CA, private keys, configuration
and logs live in a temporary directory and are deleted after the run.

This proves the documented TLS-termination topology on Linux; it does not qualify public CAs,
mutual TLS, forwarded-client identity, production-duration connections, arbitrary proxy
configurations or IIS.

## Native archive qualification

Use a clean committed revision (the builder always snapshots Git HEAD, not working-tree edits):

```bash
python3 scripts/native_bundle.py --rid linux-x64 --output-dir out/native
/tmp/request-guard-sdk/bin/python tests/protocol/qualify_bundle.py \
  --archive out/native/request-guard-mcp-dotnet-0.1.0-dev-linux-x64.tar.gz \
  --expected-rid linux-x64 --expected-source "$(git rev-parse HEAD)" \
  --report out/native/qualification.json
python3 tests/protocol/lock_negative.py
```

`native.yml` repeats the native archive checks on matching Linux x64, Windows x64,
Intel macOS and arm64 macOS hosted runners. It validates archive/file checksums,
source/RID provenance and safe single-root extraction, then runs the actual extracted
binary through the HTTP/WebSocket envelope and all-tool official SDK suite plus stdio
process/SDK tests. Runtime lookup paths point at an absent directory; the executable
runs directly without invoking `dotnet`. The dependency-drift negative control requires
NU1004. Test reports record the exact archive digest, source, RID and SDK. Development
candidate archives/reports are retained for seven days, without release/tag/registry
publication. Check current run results before making a platform qualification claim.

The four-RID `ReleaseBundle` profile pins dependencies separately from normal builds,
and `global.json` pins SDK10.0.402 with roll-forward disabled, matching the container.
The original locked single-file publish failed NU1004 for missing RID/ILLink entries;
local release-lock restore/publish succeeds without rewriting the five locks. SDK
updates require reviewing/regenerating native locks and rerunning all platforms.

The follow-on installer matrix builds DEB/RPM, MSI and PKG only from these qualified archives. On
matching disposable systems it verifies installer checksums/source linkage, clean install, installed
payload hashes, pinned official Python SDK stdio operation after clean install and upgrade, a
synthetic older-version upgrade, uninstall and residue. The Linux RPM lifecycle runs a raw stdio
smoke in a digest-pinned Fedora 42 container; the DEB supplies Linux's installed-package SDK evidence.
Synthetic older installers are discarded.
See [installer instructions](INSTALLERS.md). These tests do not qualify code signing, notarization,
OS service registration, physical LAN/router or backend TLS/recovery. Checksums/manifests provide
consistency and traceability, not signed publisher identity.

## Official release-candidate negative controls

```bash
python3 -m unittest discover -s tests/release -p 'test_*.py'
```

These tests exercise the fail-closed release predicate without publishing: non-main or stale
candidates, versions outside `0.MINOR.PATCH`, failed required checks, incomplete owner-readiness
records, untrusted approval comments and unresolved high-severity findings all fail. CI runs them
on every candidate. The real workflow additionally queries GitHub for exact current `main`, named
successful checks and the durable readiness issue/comment before any build or registry write. The
required check set includes all four installer jobs. A successful unit test does not substitute for
exact-candidate installer evidence or the completed owner readiness record.

Coverage may be collected with:

```bash
dotnet test --configuration Release --collect:"XPlat Code Coverage"
```

A percentage is not presently an assurance claim. Branch and structural-coverage gaps must be
reviewed against the requirements matrix before an official release candidate; MC/DC is not claimed.

## Backend integration verification

The automated `make parity-integration` target is the normative backend test. For exploratory
manual verification, use isolated Redis and PostgreSQL instances with non-production credentials:

1. Set `AUTH_TOKENS`, `CACHE_SCOPE_HMAC_KEY`, `REDIS_PASSWORD`, and `POSTGRES_PASSWORD`, then start
   `docker compose -f docker/docker-compose.yml up --build`.
2. Confirm `/ready` is HTTP 200 and `/health` reports Redis and PostgreSQL healthy.
3. Classify a request with a fixed `request_id`; submit feedback; replay it; request drift and
   calibration reports; and confirm no connection string or token appears in responses/logs.
4. Register test-only threat and canary records directly in the disposable Redis instance, invoke
   the corresponding tools, and verify queue telemetry ages out.
5. Restart one replica and confirm the shared cache and PostgreSQL decisions remain caller-scoped.
   Startup must reject authenticated PostgreSQL configuration without a stable
   `CACHE_SCOPE_HMAC_KEY`.

The automated runner fetches only MaxMind's public upstream test databases at a pinned commit and
verifies their SHA-256 hashes. Never substitute or commit commercial databases.

## System and deployment verification

```bash
docker build -f docker/Dockerfile -t request-guard-mcp-dotnet:test .
docker run --rm -p 8085:8085 -e AUTH_TOKENS=test-only-strong-token request-guard-mcp-dotnet:test
kubectl apply --dry-run=client -f deploy/k8s/
```

Exercise HTTP and WebSocket calls, malformed JSON, oversize bodies, cancellation, timeout,
authentication failure, graceful shutdown, metrics scraping, and OTLP collector loss. Container
image scanning belongs in release evidence.

CI starts the running Release host and each exact current-Rust reference with a credentialed OTLP
endpoint on a refused local port, generates traces, waits for the batch exporter to encounter the
unavailable sink, and requires subsequent MCP responses to remain healthy. It also rejects either
synthetic OTLP credential in captured JSON console logs. This proves failure isolation and
credential non-disclosure; it does not claim that response-latency metrics include asynchronous
exporter cleanup.

CI also starts a fresh Release host and each exact maintained Rust reference for a 120-second
telemetry endurance probe. At a deliberately bounded 25 unique-unknown/healthy request pairs per
second, the probe repeatedly scrapes Prometheus output and requires exact request/error/duration
counters, only `health` and `__unknown__` tool-label values, zero attacker-selected probe strings in
metrics or captured JSON logs, and a zero post-request WebSocket gauge. On Linux it additionally
requires RSS/high-water memory below 768 MiB, at most 256 threads, and at most 512 open file
descriptors while reporting observed maxima. These generous ceilings detect runaway behavior; they
are not a memory-leak proof, maximum-throughput result, external-collector load test, or latency
claim. Run a short local form with:

```bash
/tmp/mcp-sdk/bin/python tests/protocol/run.py \
  --dotnet-dll src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll \
  --telemetry-endurance-seconds 15
```

## Independence and residual limitations

Automated tests and maintainer self-review are not independent verification. The readiness record
must disclose whether independent human review occurred, but independent review is not an official
release-candidate publication blocker. See `RELEASE-READINESS-REVIEW.md` and
`KNOWN-LIMITATIONS.md` for the enforced gate and residual scope boundaries.

## Container dependency refresh

The security workflow runs on Dockerfile/security-workflow pull requests and can also be
started manually. It builds the digest-pinned image and rejects fixed HIGH/CRITICAL
findings with Trivy; a successful NuGet audit alone is not container evidence.
The 2026-10-19 scheduled scan found fixed advisories in the old .NET 10.0.11 runtime.
The replacement pins select SDK 10.0.402 and ASP.NET runtime 10.0.13 from Microsoft
Container Registry. Treat those versions as candidates until the hosted container scan
passes; record the exact workflow run rather than inheriting August's scan result.
No native bundles, official release candidate or stable release are published by this workflow.
