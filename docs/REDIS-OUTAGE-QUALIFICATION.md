# Optional Redis cache qualification

This gate targets classification cache failure isolation, not every Redis operation or
full Rust parity. Each optional cache read/write is bounded at 500 ms inside the tool
execution budget. Dispatch start/finish records have separate 500 ms budgets. Pending
backend operations may complete later; queue telemetry and distributed cache writes are
best-effort. Caller cancellation is not converted into cache fallback.

## Reproduce

Requires Python 3 (standard library only), .NET SDK from `global.json`, Docker CLI and
permission to run disposable Linux containers. The runner downloads the digest-pinned
Redis image used by the historical integration suite. It creates only a uniquely named
container with a random loopback port, synthetic credentials, no persistent volume and
no Redis persistence. It removes its container and stops its host process on exit.
Coordinate local Docker use; hosted CI is the normal qualification path.

```sh
dotnet restore RequestGuardMcp.slnx --locked-mode
dotnet build RequestGuardMcp.slnx --configuration Release --no-restore --warnaserror
python3 tests/parity/redis_outage.py \
  --dotnet-dll src/RequestGuardMcp.Host/bin/Release/net10.0/request-guard-mcp-dotnet.dll
```

The runner uses an isolated temporary working directory and clears inherited MCP,
authentication and compatibility configuration variables before supplying synthetic
settings. It does not read the operator's `.env` or backend credentials.

## Required observations

1. Redis answers PONG; the running .NET host returns three correct classifications and
   writes actual Redis cache entries. A cache-only marker proves a subsequent read came
   from Redis rather than the local cache; the original value is restored afterward.
2. Docker confirms Redis is paused. Three distinct cold-cache classifications must all
   return the expected verdict, score, confidence, category, signals and echoed ID.
   External wall-clock median must be below 1.8 seconds and worst below 2.25 seconds,
   with a two-second tool execution timeout. Timed errors do not count as success.
3. After unpause, Redis answers PONG, classifications recover, at least three fresh
   stage-specific cache writes appear and a Redis read is proved again.
4. After Redis is stopped, the same three-call gate passes through local computation.
   The restarted backend must recover fresh writes and proved reads.
5. The pre-cache-bound binary must pass healthy preflight and recovery but fail the
   paused gate. `--expect-paused-failure` accepts only that scoped negative-control
   outcome, not a startup/configuration/HTTP failure.

The GitHub `Redis Outage Qualification` workflow performs both candidate and negative
control runs from exact source references and prints all sample durations, medians,
worst cases and valid-response counts. It does not exercise official MCP `ping`, which
is still tracked with the lifecycle work in issue #24.

## Evidence status

[Hosted run 39741361266](https://github.com/rhamenator/request-guard-mcp-dotnet/actions/runs/39741361266)
passed for candidate `ca1cb71`, with merged pre-fix main
`c028cdbf9408aac66d43db27b0eede3c4ab3fcc6` as negative control. All candidate phases
returned three valid responses; the control returned none during the pause but passed
healthy preflight and recovery.

| Build / phase | Median (s) | Worst (s) | Valid responses |
|---|---:|---:|---:|
| Candidate healthy | 0.003 | 0.024 | 3/3 |
| Candidate paused | 1.503 | 1.533 | 3/3 |
| Candidate unpaused | 0.001 | 0.002 | 3/3 |
| Candidate stopped | 1.503 | 1.503 | 3/3 |
| Candidate restarted | 0.001 | 0.002 | 3/3 |
| Pre-fix paused | 2.503 | 2.511 | 0/3 |
| Pre-fix recovered | 0.002 | 0.003 | 3/3 |

Actual Redis reads and fresh recovered writes passed in both healthy and recovered
phases. An earlier candidate run (39741177876) independently passed with paused median
1.505 s / worst 1.512 s and the earlier bookkeeping-only head as control. These are
scoped maintainer/automation observations, not independent human verification or an
all-Redis latency guarantee. Qualification targets the hosted Ubuntu/.NET environment;
other clients, platforms, TLS and wider backend behavior remain in the parity matrix.
