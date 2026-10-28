# PostgreSQL TLS and recovery qualification

Production PostgreSQL connections crossing a host boundary should use Npgsql's
`SSL Mode=VerifyFull` with a trusted CA. `VerifyFull` verifies both the certificate
chain and the requested host name. `SSL Mode=Require` encrypts without authenticating
the server and is not equivalent. The local Docker Compose example is a loopback/development
configuration, not a production TLS example.

Example using a private CA supplied as a read-only secret:

```text
MCP__POSTGRES__URL=Host=db.internal.example;Port=5432;Database=request_guard;Username=request_guard;Password=<secret>;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres-ca.crt
```

Keep the password and CA material outside source control. Restrict the CA file to the
service account, grant the database account only the documented schema/data permissions,
and use a stable `CACHE_SCOPE_HMAC_KEY` of at least 32 non-placeholder bytes whenever
authentication and PostgreSQL are enabled. Startup requires DDL permission to create or
repair the two tables and indexes; operators may pre-create them and revoke DDL afterward.

## Reproduce

Requires Python 3, OpenSSL, the SDK selected by `global.json`, Docker CLI, and permission
to run disposable Linux containers. The runner uses the digest-pinned PostgreSQL image
from the historical parity suite. It creates one uniquely named volume for one-day synthetic
server certificates and a uniquely named container with random credentials, a loopback-only
published port, one CPU, 512 MiB memory and a 256-process limit. A resource-capped helper
copies only the synthetic server certificate/key into that volume with PostgreSQL ownership.
The runner removes both container and volume on exit.

```bash
dotnet restore RequestGuardMcp.slnx --locked-mode
dotnet build RequestGuardMcp.slnx --configuration Release --no-restore --warnaserror
python3 tests/parity/postgres_tls_recovery.py \
  --dotnet-dll src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll
```

The runner clears inherited MCP/authentication configuration, starts the server from an
isolated temporary directory, and never reads operator credentials. It checks logs for
the synthetic password, Bearer token and caller-scope key. Generated CA keys, certificates,
and logs disappear with the temporary directory; the copied synthetic server key disappears
with the scoped Docker volume.

## Required observations

1. `VerifyFull` with a CA that did not sign the database certificate prevents startup.
2. `VerifyFull` with the signing CA but an IP address absent from the certificate prevents
   startup, proving host-name verification is active.
3. `VerifyFull` with the signing CA and `localhost` starts successfully. Classification,
   persistence and deterministic replay pass, and `pg_stat_ssl` observes an encrypted
   application session.
4. While Docker has actually paused PostgreSQL, replay returns a JSON-RPC timeout error
   within six seconds. After unpause, the same persisted decision replays successfully.
5. With PostgreSQL stopped, replay and classification persistence return upstream errors
   within six seconds; they do not synthesize success. After the same container restarts,
   the original persisted decision replays successfully without restarting the MCP server.

The six-second external ceiling includes the configured two-second tool deadline plus
driver cancellation and HTTP scheduling. The measured local paused call is expected near
two seconds; the ceiling is a fail-safe, not a latency objective.

## Scope and limitations

This gate qualifies Npgsql `VerifyFull` with an explicit PEM root, private-CA rejection,
host-name rejection, a running process through pause/stop/restart, and truthful errors for
PostgreSQL-dependent operations. It does not qualify every public CA store, mutual TLS,
certificate rotation, cloud-provider proxy, failover cluster, packet-loss pattern, or
database migration from every historical schema. Readiness uses the same backend but is
not a substitute for these tool-level checks. Redis TLS is not implemented or claimed;
keep Redis on a trusted isolated link or an independently secured loopback tunnel.

Hosted workflow success is source-build evidence for Ubuntu/.NET, not independent human
review or installer/platform TLS evidence. Record its exact source SHA and run URL before
using it in a release-candidate decision.
