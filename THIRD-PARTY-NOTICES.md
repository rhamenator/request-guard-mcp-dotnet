# Third-party notices

## Woothee compatibility logic

`src/RequestGuardMcp.Tools/WootheeUaParser.cs` is derived from the challenge ordering, dataset,
and classification rules in `woothee-rust` 0.13.0.

Copyright 2016 Hideo Hattori (@hhatto)

Licensed under the Apache License, Version 2.0. The license text is available at
<https://www.apache.org/licenses/LICENSE-2.0>.

The compatibility implementation exposes only the name, category, and operating-system fields
used by request-guard-mcp. Differential tests verify those fields against the Rust implementation.
