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

No known behavioral or JSON wire difference remains within the tested 23-tool contract. Any new
difference is a release blocker unless this document and the requirements/evidence are updated with
an explicit disposition.
