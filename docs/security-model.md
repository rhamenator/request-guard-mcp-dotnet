# Security Model

## Trust boundaries

Clients are untrusted until Bearer authentication succeeds. JSON request fields, including TLS
metadata, remain untrusted input. Redis, PostgreSQL, MaxMind files, the OTLP collector, reverse
proxy, and orchestrator are privileged dependencies whose credentials and network reachability must
be constrained independently.

HTTP/WebSocket Bearer parsing rejects repeated headers and malformed token syntax,
accepts case-insensitive schemes and compares every allowed token with `FixedTimeEquals`.
Malformed configured tokens fail startup. Native/default binding is loopback; remote
listeners must be explicitly configured. Any present browser Origin must match a validated
bare HTTP(S) origin in `auth.allowed_origins`, including when Bearer auth is disabled.
The empty default rejects every present Origin. Missing Origin is allowed for machine
clients; it is not authentication. Allowed origins still require the configured token.
Origin validation does not add CORS or OAuth support. Proxies must preserve Origin.

Cache scopes use a server-keyed HMAC, so raw credentials
and reusable bare hashes do not enter cache keys. TLS JA3/JA4 metadata affects classification only
after a fresh HMAC binds it to IP, method, path, source, and timestamp. Attestation tokens are
consumed before caching or persistence.

## Data handling

PostgreSQL stores classification responses, operator-supplied feedback, and a minimized replay
snapshot. The snapshot retains only current rule inputs: user agent, method, a path with query and
fragment removed, and the presence (not values) of `accept`/`accept-language` headers. It does not
retain IP addresses, bodies, referrers, timestamps, arbitrary extra fields, request IDs, TLS
fingerprints, or attestation data. Server-keyed caller scopes isolate decision lookup, feedback,
drift, and calibration; public request IDs are not database keys. User agents, paths, responses,
feedback notes, and reporter names can still be sensitive, so establish retention and database
access controls appropriate to the deployment. Logs use structured events and must not include
Bearer tokens, connection strings, attestation keys, full canary tokens, or raw request bodies.

`config_snapshot` reports only configuration presence/counts and redacts the OTLP endpoint by
default. Secret values are never returned. `redact_preview` is a preview helper, not a substitute
for storage/logging policy.

## Operational controls

- Terminate TLS before the service and restrict direct network access.
- Use distinct high-entropy values for auth, cache-scope HMAC, TLS attestation, Redis, and database
  credentials; rotate them through the platform secret store.
- Restrict Redis/PostgreSQL to the application network and enable their authentication/TLS for
  cross-host deployments.
- Mount MMDB files read-only and verify provenance/checksums during updates.
- Run the image as non-root with a read-only filesystem and dropped capabilities.
- Treat `/metrics` as operational data and restrict it if tool names or traffic volumes are
  sensitive.

See `SECURITY.md` for vulnerability reporting.
