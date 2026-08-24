# Verification Evidence — 2026-08-24

This record applies to the release-candidate commit that contains it and to Rust compatibility
baseline `4170649920190e989175755369215d4f29a7427b`. The executing maintainer performed the checks;
this is not independent verification.

| Gate | Result |
|---|---|
| .NET locked restore, format check, warning-as-error Release build | Pass, zero warnings/errors |
| .NET xUnit suite | Pass, 110/110 |
| NuGet transitive vulnerability query | Pass, no vulnerable packages reported |
| Differential core/tools/transports | Pass, 53 assertions covering every tool and protocol failures |
| Upstream Woothee 0.13.0 corpus | Pass, 238/238 user agents |
| Feature, auth, TLS, and configuration phases | Pass, including JSON/INI/TOML/YAML/RON/JSON5/dotenv |
| Disposable Redis/PostgreSQL/MaxMind phase | Pass, 18 assertions including caller cache isolation |
| Forced PostgreSQL-lock timeout | Pass for Rust and .NET at the configured one-second deadline |
| Rust format and warnings-denied Clippy | Pass |
| Rust tests | Pass, 59/59 |
| RustSec audit | Pass, 431 locked dependencies scanned against 1,225 advisories |
| Container build and runtime smoke | Pass, non-root/read-only health and JSON-RPC |
| Compose integration | Pass, Redis/PostgreSQL readiness and authenticated MCP health |
| Kubernetes schema validation | Pass, kubeconform v0.7.0: 13/13 resources |
| GitHub Actions validation | Pass, actionlint v1.7.7 |
| Container vulnerability scan | Pass, Trivy v0.62.1: zero fixed HIGH/CRITICAL findings |

The MaxMind phase downloads only public upstream test fixtures at commit
`e1120013c4b5cbc830b958b2b7e73fba444d316d` and rejects any SHA-256 mismatch. Redis and PostgreSQL
data are disposable and use distinct namespaces/databases for the two implementations.

## Open release gate

The independent review described in `RELEASE-READINESS-REVIEW.md` remains pending. No stable-release
or certification claim may treat this automated and maintainer-generated evidence as independent.
