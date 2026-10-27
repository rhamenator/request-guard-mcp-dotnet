# Known Limitations

These are explicit scope boundaries, not untracked defects.

- The assurance process is DO-178C-inspired. This repository is not certified, has not undergone
  certification-authority review, and is not represented as suitable for airborne use.
- Independent human verification remains pending and is the only release-readiness gate that a
  sole maintainer cannot complete. Follow `RELEASE-READINESS-REVIEW.md` before a stable release.
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
upgrade can temporarily reduce cache hit rate. Byte-identical cache keys shared between
Rust and .NET (including Unicode/number serialization edge cases) are not yet qualified;
use separate Redis key prefixes for mixed-runtime deployments.

Connection controls added for Rust main #160/#162 are tested separately from the August
differential baseline. Kestrel does not provide same-port cleartext HTTP/1 + HTTP/2
prior-knowledge auto-detection in this host. HTTP/2 tests explicitly select that protocol;
TLS/ALPN and reverse-proxy hosting need independent client qualification. The 120-second
production age limit is exercised with a shorter injected clock budget in tests; this
is not an exact 120-second artifact endurance record.
