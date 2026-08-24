# Contributing

Thank you for improving Request Guard MCP (.NET). Small, focused changes with clear validation are the easiest to review and maintain.

## Choose the Right Channel

- Use the structured [issue forms](https://github.com/rhamenator/request-guard-mcp-dotnet/issues/new/choose) for confirmed bugs, feature proposals, documentation problems, and support questions.
- Read [SUPPORT.md](SUPPORT.md) before requesting help.
- Report suspected vulnerabilities privately as described in [SECURITY.md](SECURITY.md). Do not open a public issue for them.
- Follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) in all project spaces.
- Use [GitHub Discussions](https://github.com/rhamenator/request-guard-mcp-dotnet/discussions) for open-ended questions, design ideas, and anything that isn't yet a concrete bug or proposal.

Search existing issues and pull requests before opening a new one. For larger or compatibility-sensitive changes, open an issue first so the design can be discussed before substantial implementation work.

## Development

Use the pinned .NET SDK (`global.json`) and keep changes scoped. Add tests for behavioral changes and update the README, docs, schemas, deployment examples, or configuration references when they are affected. Dependency versions are pinned centrally in `Directory.Packages.props`; restore uses `packages.lock.json` per project (`--locked-mode` in CI), so add new dependencies with `dotnet add package <name> --package-directory` or by editing `Directory.Packages.props` directly, then re-run `dotnet restore --force-evaluate` to refresh the lock files.

Run the full local validation suite before submitting a pull request:

```shell
make ci
```

The equivalent individual checks are:

```shell
dotnet format --verify-no-changes
dotnet build --configuration Release --warnaserror
dotnet test --configuration Release
dotnet list package --vulnerable --include-transitive
```

If a check cannot be run locally, explain why in the pull request and report what you ran instead.

## Pull Requests

- Create a focused branch from `main` and keep commits concise and intentional.
- Complete the pull request template, link related issues, and list exact validation commands and results.
- Call out changes to MCP behavior, tool schemas, APIs, authentication, configuration, persistence, metrics, performance, or deployment.
- Never commit secrets, credentials, private request content, production data, or sensitive infrastructure details.
- Address review feedback and keep the branch current until required checks pass.

By contributing, you agree that your contribution is licensed under the repository's license.
