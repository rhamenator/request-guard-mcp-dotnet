# request-guard-mcp-dotnet

[![CI](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/workflows/ci.yml)
[![License: GPL v3+](https://img.shields.io/badge/License-GPLv3%2B-blue.svg)](LICENSE)

A C#/.NET **Model Context Protocol (MCP) server** for request risk classification, enrichment, and
abuse-signal analysis — a from-scratch .NET port of
[`request-guard-mcp`](https://github.com/rhamenator/request-guard-mcp) (Rust), aiming for the same
tool surface and behavior over the same WebSocket/HTTP JSON-RPC 2.0 transports.

## Status

The .NET port implements all 23 sibling Rust tool contracts, HTTP/WebSocket JSON-RPC transports,
Bearer authentication, bounded concurrency/timeouts, two-level caching, PostgreSQL persistence and
reporting, Redis threat/canary/queue services, MaxMind enrichment, HMAC-attested JA3/JA4 rules,
Prometheus metrics, optional OTLP traces, and container/Kubernetes deployment assets.

The assurance baseline is DO-178C-inspired and appropriate to a sole-maintainer project; it is not
a claim of certification or airborne suitability. Independent review remains required before the
first stable release.

### Try it

```bash
cp .env.example .env
# replace every uncommented replace_me value in .env, then:
dotnet run --project src/RequestGuardMcp.Host
curl http://localhost:8085/health
curl -H "Authorization: Bearer your-strong-token" -X POST http://localhost:8085/mcp \
  -d '{"jsonrpc":"2.0","id":1,"method":"classify","params":{"user_agent":"GPTBot/1.0","path":"/"}}'
```

## Why a .NET port?

The sibling Rust server defines the compatibility contract. This project re-implements that surface
on ASP.NET Core, using external packages only for Redis, PostgreSQL, MaxMind, OpenTelemetry, and the
test runner. See [docs/architecture.md](docs/architecture.md) for the dependency budget.

## Solution layout

```text
RequestGuardMcp.slnx
src/
  RequestGuardMcp.Core/          # config, AppState, errors, response models, health check, engines
  RequestGuardMcp.Mcp/           # JSON-RPC types, WS+HTTP transport, tool registry, auth, limits
  RequestGuardMcp.Tools/         # all 23 MCP tool implementations
  RequestGuardMcp.Integrations/  # Redis, Postgres, MaxMind adapters behind interfaces
  RequestGuardMcp.Host/          # ASP.NET Core Program.cs, DI wiring — the executable
tests/
  RequestGuardMcp.Tests/         # unit + contract tests (xUnit)
```

## Development

```bash
dotnet restore
dotnet build --configuration Release
dotnet test
```

Or, equivalently:

```bash
make ci
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full workflow.

## Tools

| Classification and explanation | Enrichment and security | Persistence and operations |
|---|---|---|
| `classify`, `batch_classify`, `explain` | `enrich_ip`, `enrich_asn`, `enrich_ua` | `feedback`, `replay_decision` |
| `score_breakdown`, `validate_payload` | `threat_lookup`, `canary_eval` | `drift_report`, `calibration_report` |
| `model_info`, `feature_flags` | `abuse_pattern_match`, `redact_preview` | `health`, `queue_status`, `warmup`, `config_snapshot`, `self_test` |

Backend-dependent tools remain registered for wire compatibility. They report
`INTEGRATION_UNAVAILABLE` when their feature/backend is absent and `UPSTREAM_ERROR` when a
configured dependency fails; they do not return synthetic success data.

## Documentation

- [docs/architecture.md](docs/architecture.md) — target architecture and delivery phases
- [docs/DO-178C-ASSURANCE-POLICY.md](docs/DO-178C-ASSURANCE-POLICY.md) — the change-control and
  verification baseline this repository follows
- [docs/RELEASE-READINESS-REVIEW.md](docs/RELEASE-READINESS-REVIEW.md) — the independent human
  review gate required before a first stable release
- [docs/REQUIREMENTS-TRACEABILITY.md](docs/REQUIREMENTS-TRACEABILITY.md) — requirements mapped to
  implementation and verification evidence
- [docs/VERIFICATION.md](docs/VERIFICATION.md) — automated, integration, and deployment checks
- [docs/security-model.md](docs/security-model.md) and [docs/runbook.md](docs/runbook.md) — trust
  boundaries and operations

## Deployment

```bash
docker build -f docker/Dockerfile -t request-guard-mcp-dotnet:local .
# Set AUTH_TOKENS, CACHE_SCOPE_HMAC_KEY, REDIS_PASSWORD, and POSTGRES_PASSWORD first.
docker compose -f docker/docker-compose.yml up --build
```

Kubernetes examples are under `deploy/k8s`. Copy `secret.example.yaml` to `secret.yaml`, replace
every placeholder, and keep that generated file out of version control.

## Community

- [GitHub Discussions](https://github.com/rhamenator/request-guard-mcp-dotnet/discussions) for
  questions, design ideas, and anything not yet a concrete bug or proposal
- [Issue forms](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/new/choose) for bugs,
  feature proposals, documentation problems, and support questions
- [SECURITY.md](SECURITY.md) for private vulnerability reporting

## License

GPL-3.0-or-later — see [LICENSE](LICENSE).
