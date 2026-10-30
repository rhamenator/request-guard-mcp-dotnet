# Known Limitations

These are explicit scope boundaries, not untracked defects.

- The assurance process is DO-178C-inspired. This repository is not certified, has not undergone
  certification-authority review, and is not represented as suitable for airborne use.
- Independent human verification has not been performed. Official release-candidate records must
  disclose that fact, but owner self-review and the production-level technical evidence in
  `RELEASE-READINESS-REVIEW.md` are the publication gates. Self-review and automation are not
  represented as independent verification.
- Compatibility is established against the exact Rust revision in
  `tests/parity/rust-baseline.txt`. Advancing that revision requires review and a green differential
  suite; it is not assumed automatically.
- Language/runtime build metadata necessarily differs. The legacy `model_info.build_info.rust_version`
  wire key is retained for compatibility but contains the .NET runtime version in this port.
- `RustConfigFileParser` accepts the application configuration shapes supported by this repository
  in JSON, INI, TOML, YAML, RON, and JSON5. It is not intended as a general-purpose implementation
  of every feature in those data languages.
- The bounded local cache uses FIFO eviction, while Rust's `moka` cache uses an admission/eviction
  policy optimized for frequency. This can change cache hit rate under pressure, but not the
  classification result or caller-isolation contract; Redis remains the shared cache.
- MaxMind results are only as current and precise as the operator-provided databases. IP location
  must not be treated as an exact physical address.

The historical pinned suite originally found no behavioral or JSON wire difference within its tested
23-tool contract. The MCP lifecycle update intentionally changes response envelope omission,
notification HTTP 202 and GET405; the baseline migration is documented in `VERIFICATION.md`. Current Rust has since changed; known gaps and required new evidence are
tracked in [PARITY-MATRIX.md](PARITY-MATRIX.md). Any new
difference is a release blocker unless this document and the requirements/evidence are updated with
an explicit disposition.

Published Rust v0.1.5 uses the older broad sensitive-path expression. The .NET port follows the
controlled current-main grammar, which adds concrete administrative/configuration targets and
removes false positives such as `/docs/admin-guide` and `/products/configurable-shirt`. The exact
gate therefore applies 82 shared cases to both references and 83 expanded route cases only to
current main; this intentional revision boundary is not normalized away or described as v0.1.5
behavior.

Cache fingerprints use structured, recursively sorted JSON in this port. Keys from older
.NET builds are not reused; old entries expire according to their existing TTL. A rolling
upgrade can temporarily reduce cache hit rate. Cross-runtime cache-byte sharing is explicitly
unsupported: Rust and .NET intentionally include different build domains, so mixed-runtime
deployments must use separate Redis key prefixes. Query/fragment equivalence, caller isolation,
rule-input identity, normalized TLS-policy partitioning, and the distinct-prefix integration path
are tested; a common Rust/.NET cache serialization protocol is not claimed.

The configuration differential suite checks the complete stable redacted snapshot and broad scalar
mapping, but some connection controls and runtime-only settings are intentionally absent from that
snapshot. Telemetry cardinality has an exact 120-second running-artifact gate with bounded traffic
and process ceilings, but this is not a maximum-throughput, external-collector, memory-leak, or
every-log-field test. Parser validation and focused socket tests do not prove operational
equivalence for every other non-snapshot field.

Connection controls added for Rust main #160/#162 are tested separately from the August
differential baseline. Kestrel does not provide same-port cleartext HTTP/1 + HTTP/2
prior-knowledge auto-detection in this host. HTTP/2 tests explicitly select that protocol;
an actual Nginx TLS terminator now gates CA trust/rejection, TLS 1.3, ALPN-selected HTTP/2,
HTTP/1.1 and WebSocket proxying on Linux. Public-CA, mTLS, IIS and production-duration proxy
behavior remain outside that evidence. CI now exercises the HTTP/2 age limit against real Kestrel
for the full 120-second production budget under continuous traffic. That is not a packaged-artifact,
maximum-load, leak or broader resource-exhaustion record.

Native archives and installers target glibc Linux x64, Windows x64, Intel macOS and arm64 macOS.
Linux musl/Alpine and Linux/Windows arm64 artifacts and OS service registration are not provided.
Linux packages are DEB and RPM only. The Windows MSI does not modify `PATH`; macOS removal uses the
installed uninstall script because PKG has no native uninstall transaction. Packages are unsigned
and macOS packages are not notarized. Matching hosted runners qualify fresh portable extraction,
SDK clients and installer install/launch/upgrade/uninstall/residue; backend TLS, physical LAN/router,
enterprise deployment systems and every supported-distribution version remain separate evidence.
Development Actions artifacts are not official release candidates or stable releases. Checksums and
workflow manifests establish consistency and traceability, not signed publisher identity.

MCP Inspector 2.13.0 is qualified against a running Release build over process-local stdio. Its
remote HTTP mode is not supported because this server intentionally provides JSON responses without
Streamable HTTP sessions or SSE. The .NET port publishes server archives and installers, not an npm
server package or companion marketplace skill; clients launch the installed executable directly.

HTTP POST now requires UTF-8 application/json and explicit positive-quality
application/json plus text/event-stream in Accept, although responses remain JSON-only.
A present MCP-Protocol-Version must be 2025-06-18; absence is retained for initialization
and legacy direct callers. Default native binding is loopback. Present browser origins
are denied unless explicitly allowed; this policy does not implement CORS or OAuth.
These intentional differences from the August Rust pin are covered by dedicated tests;
the historical differential clients send the new headers and still compare tool payloads. Exact
v0.1.5/current-main jobs also execute the full protocol and stdio suites. Those gates explicitly
record, rather than erase, the references' last-key-wins duplicate JSON behavior, legacy omitted
media headers, repeated-equal Authorization handling, case-sensitive Bearer parsing and default
SIGTERM exit; none is inherited as a requirement to weaken the .NET boundary.

WebSocket receive budgets cover an entire message, not individual fragment activity;
expiry can abort the connection without a graceful peer close. Output deadlines likewise
abort a stalled HTTP/WS writer rather than returning a second error response. Expiry and
recovery tests shorten configured WS budgets and use cancellation-ignoring I/O doubles;
they do not establish default-duration endurance or every real slow-consumer/proxy case.
