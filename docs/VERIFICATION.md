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
and language build metadata. The historical Rust pin's redundant null envelope members are
an explicit baseline-only migration exception: .NET must omit the unused branch. The runner
also expects historical Rust notification 204 versus .NET 202. Regression tests ensure this
exception cannot hide a candidate emitting both branches or discard application null fields. Its core phase covers every one of the 23 tools, malformed/missing input, disabled
features, HTTP and text/binary WebSocket transport, strict UTF-8, auth, TLS attestation, and all six
Rust config formats plus YAML's `.yml` alias and dotenv. The integration phase launches pinned
Redis/PostgreSQL containers, downloads checksum-pinned Apache-licensed MaxMind upstream fixtures,
tests persistence/replay/reports/reputation/canaries/queues, verifies two caller cache scopes, and
forces a PostgreSQL lock to prove the tool deadline.

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
checks 16 invalid envelopes and invalid JSON over HTTP and WebSocket, ignored messages,
recovery and authenticated GET405. xUnit covers parser and lifecycle cases. CI runs both.

Scope: JSON-response HTTP and custom WebSocket; this is not full Streamable HTTP compliance.
Stateful sessions, SSE and OAuth discovery are not implemented. Negotiation/origin
handling is separately exercised below. Pinned SDK packages are test-only and not shipped with the
server. Requalification is required after SDK/dependency or transport changes.

## HTTP negotiation, authentication and origins

The protocol runner additionally checks 17 real HTTP negotiation/auth/origin cases,
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
case/default-port normalization and fail-fast configuration. The incomplete-body socket
test includes valid negotiation headers so it still exercises the 408 body deadline.
All repository clients/examples now send the required Accept header. The historical
pair runner explicitly binds both servers to loopback, rather than hiding the changed
.NET default through payload normalization; a default-configuration test checks it. Missing protocol
version is an explicit legacy/initialization compatibility path, not implicit support
for every MCP revision. Reverse proxy/IIS, TLS/ALPN and broader browser clients remain
separate qualification work in #25.

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
stream ignores it. CI runs the process runner on Linux. The transport is sequential;
client cancellation notifications and OS service integration remain unqualified; portable
Windows/macOS SDK and installed-binary smoke are exercised by the native workflow.

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
payload hashes, stdio launch, a synthetic older-version upgrade, uninstall and residue. The Linux
RPM lifecycle runs in a digest-pinned Fedora 42 container; synthetic older installers are discarded.
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
5. Restart one replica and confirm the shared cache remains caller-scoped.

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
