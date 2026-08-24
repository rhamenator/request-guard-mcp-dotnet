# request-guard-mcp-dotnet

[![CI](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/workflows/ci.yml)
[![License: GPL v3+](https://img.shields.io/badge/License-GPLv3%2B-blue.svg)](LICENSE)

A C#/.NET **Model Context Protocol (MCP) server** for request risk classification, enrichment, and
abuse-signal analysis — a from-scratch .NET port of
[`request-guard-mcp`](https://github.com/rhamenator/request-guard-mcp) (Rust), aiming for the same
tool surface and behavior over the same WebSocket/HTTP JSON-RPC 2.0 transports.

## Status

This repository is under active, phased development. The current state is the project scaffolding
and governance baseline (solution structure, license, contribution/security/support policies, CI,
and the DO-178C-inspired assurance policy) — the MCP protocol, engines, and tools land in the
phases that follow. See [docs/architecture.md](docs/architecture.md) for the target design and
delivery-phase breakdown, and the repository's commit/PR history for current progress.

## Why a .NET port?

The Rust server implements 23 MCP tools (classify/explain/batch-classify, enrichment, threat lookup,
decision replay, drift/calibration reporting, and more) over Redis, PostgreSQL, and MaxMind GeoIP
backends, with Prometheus/OpenTelemetry observability and Docker/Kubernetes deployment. This project
re-implements the same surface on the .NET base class library and ASP.NET Core, using external NuGet
packages only where the BCL has no safe equivalent (Redis, PostgreSQL, and MaxMind clients; the
OpenTelemetry SDK). See [docs/architecture.md](docs/architecture.md) for the dependency budget.

## Solution layout

```
RequestGuardMcp.slnx
src/
  RequestGuardMcp.Core/          # models, engines (rules/scorer/explain/anomaly/policy), util
  RequestGuardMcp.Mcp/           # JSON-RPC types, WS+HTTP transport, tool registry, auth, limits
  RequestGuardMcp.Tools/         # the 22 tool implementations
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

## Documentation

- [docs/architecture.md](docs/architecture.md) — target architecture and delivery phases
- [docs/DO-178C-ASSURANCE-POLICY.md](docs/DO-178C-ASSURANCE-POLICY.md) — the change-control and
  verification baseline this repository follows
- [docs/RELEASE-READINESS-REVIEW.md](docs/RELEASE-READINESS-REVIEW.md) — the independent human
  review gate required before a first stable release

## Community

- [GitHub Discussions](https://github.com/rhamenator/request-guard-mcp-dotnet/discussions) for
  questions, design ideas, and anything not yet a concrete bug or proposal
- [Issue forms](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/new/choose) for bugs,
  feature proposals, documentation problems, and support questions
- [SECURITY.md](SECURITY.md) for private vulnerability reporting

## License

GPL-3.0-or-later — see [LICENSE](LICENSE).
