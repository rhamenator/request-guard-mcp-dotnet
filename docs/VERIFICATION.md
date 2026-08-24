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

Coverage may be collected with:

```bash
dotnet test --configuration Release --collect:"XPlat Code Coverage"
```

A percentage is not presently an assurance claim. Branch and structural-coverage gaps must be
reviewed against the requirements matrix before a stable release; MC/DC is not claimed.

## Backend integration verification

Use isolated, disposable Redis and PostgreSQL instances with non-production credentials:

1. Set `AUTH_TOKENS`, `CACHE_SCOPE_HMAC_KEY`, `REDIS_PASSWORD`, and `POSTGRES_PASSWORD`, then start
   `docker compose -f docker/docker-compose.yml up --build`.
2. Confirm `/ready` is HTTP 200 and `/health` reports Redis and PostgreSQL healthy.
3. Classify a request with a fixed `request_id`; submit feedback; replay it; request drift and
   calibration reports; and confirm no connection string or token appears in responses/logs.
4. Register test-only threat and canary records directly in the disposable Redis instance, invoke
   the corresponding tools, and verify queue telemetry ages out.
5. Restart one replica and confirm the shared cache remains caller-scoped.

MaxMind verification requires licensed/test MMDB fixtures. Test City, ASN, anonymous proxy, Tor,
private-IP, missing-record, corrupt-file, and wrong-database cases. Do not commit commercial data.

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

Automated tests and maintainer self-review are not independent verification. Independent human
review remains mandatory at the gate in `RELEASE-READINESS-REVIEW.md`. External backend tests and
licensed MaxMind fixtures are environment-dependent and therefore are documented procedures rather
than default CI jobs.
