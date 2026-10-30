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

Cache fingerprints use structured, recursively sorted JSON in this port. Keys from older
.NET builds are not reused; old entries expire according to their existing TTL. A rolling
upgrade can temporarily reduce cache hit rate. Cross-runtime cache-byte sharing is explicitly
unsupported: Rust and .NET intentionally include different build domains, so mixed-runtime
deployments must use separate Redis key prefixes. Query/fragment equivalence, caller isolation,
rule-input identity, normalized TLS-policy partitioning, and the distinct-prefix integration path
are tested; a common Rust/.NET cache serialization protocol is not claimed.

The configuration differential suite checks the complete stable redacted snapshot and broad scalar
mapping, but some connection controls and runtime-only settings are intentionally absent from that
snapshot. Their parser validation and focused socket tests do not constitute production-duration
endurance evidence or prove operational equivalence for every non-snapshot field.

Connection controls added for Rust main #160/#162 are tested separately from the August
differential baseline. Kestrel does not provide same-port cleartext HTTP/1 + HTTP/2
prior-knowledge auto-detection in this host. HTTP/2 tests explicitly select that protocol;
TLS/ALPN and reverse-proxy hosting need independent client qualification. The 120-second
production age limit is exercised with a shorter injected clock budget in tests; this
is not an exact 120-second artifact endurance record.

Native archives and installers target glibc Linux x64, Windows x64, Intel macOS and arm64 macOS.
Linux musl/Alpine and Linux/Windows arm64 artifacts and OS service registration are not provided.
Linux packages are DEB and RPM only. The Windows MSI does not modify `PATH`; macOS removal uses the
installed uninstall script because PKG has no native uninstall transaction. Packages are unsigned
and macOS packages are not notarized. Matching hosted runners qualify fresh portable extraction,
SDK clients and installer install/launch/upgrade/uninstall/residue; backend TLS, physical LAN/router,
enterprise deployment systems and every supported-distribution version remain separate evidence.
Development Actions artifacts are not official release candidates or stable releases. Checksums and
workflow manifests establish consistency and traceability, not signed publisher identity.

HTTP POST now requires UTF-8 application/json and explicit positive-quality
application/json plus text/event-stream in Accept, although responses remain JSON-only.
A present MCP-Protocol-Version must be 2025-06-18; absence is retained for initialization
and legacy direct callers. Default native binding is loopback. Present browser origins
are denied unless explicitly allowed; this policy does not implement CORS or OAuth.
These intentional differences from the August Rust pin are covered by dedicated tests;
the historical differential clients send the new headers and still compare tool payloads.

WebSocket receive budgets cover an entire message, not individual fragment activity;
expiry can abort the connection without a graceful peer close. Output deadlines likewise
abort a stalled HTTP/WS writer rather than returning a second error response. Expiry and
recovery tests shorten configured WS budgets and use cancellation-ignoring I/O doubles;
they do not establish default-duration endurance or every real slow-consumer/proxy case.
