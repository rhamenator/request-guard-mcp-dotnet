# Operations Runbook

## Endpoints

- `/health`: liveness and component status.
- `/ready`: HTTP 200 only when capacity and configured dependencies are healthy.
- `/metrics`: Prometheus text (path is configurable).
- `/mcp`: authenticated HTTP POST or WebSocket JSON-RPC.

## Common failures

An immediate startup failure means configuration validation, backend connection/schema setup, or
MMDB loading failed. Correct the named setting or dependency; do not disable validation. A 503 from
`/ready` with a live `/health` body identifies the unhealthy dependency. `INTEGRATION_UNAVAILABLE`
means the backend/feature is intentionally absent; `UPSTREAM_ERROR` means a configured backend
failed during a call; `TIMEOUT` means the per-tool deadline expired.

For Redis, verify the `redis://`/`rediss://` endpoint, credentials, DNS, and key prefix. For
PostgreSQL, verify the Npgsql connection string, TLS policy, DDL permission on first start, and pool
limit. For GeoIP, verify the path, file permissions, database edition/type, and checksum.

## Safe maintenance

Before upgrading, record the image/source revision, back up PostgreSQL, validate dependency alerts,
and run the verification plan. Roll out one replica, observe readiness/error/latency metrics, then
continue. Roll back the immutable image if errors rise. Cache data is disposable; decision and
feedback tables are not.

Replace the example application `:latest` reference in the Kubernetes Deployment with the signed,
immutable digest approved for the environment before production use.

Rotate TLS attestation keys by moving the old key to the previous-key setting and deploying the new
key to producers/signers, then remove the previous key after the maximum age plus rollout margin.
Rotate Bearer tokens with an overlap period; cache scopes intentionally change with the token.
