---
title: "ADR-0001: Technology Stack — .NET 10, C# 13, Spectre.Console.Cli"
status: "Accepted"
date: "2026-03-18"
authors: "zapi-cli Core Team"
tags: ["architecture", "decision", "technology", "runtime"]
supersedes: ""
superseded_by: ""
---

# ADR-0001: Technology Stack — .NET 10, C# 13, Spectre.Console.Cli

## Status

**Accepted**

## Context

zapi-cli is a standalone, multi-platform CLI binary that must run on macOS (x64 and arm64), Windows (x64), and Linux (x64). It needs to:

- Produce a single self-contained executable per platform with no runtime install requirement.
- Interact with OS-level APIs (keychain, credential manager) for secure credential storage.
- Parse complex CLI flags, subcommand groups, and produce readable help output.
- Be developed and maintained by a team with existing .NET expertise.
- Be testable end-to-end with a standard unit-test framework.

The tool is positioned as a developer utility and AI-agent integration point, so correctness, type safety, and ease of cross-platform distribution are higher priorities than startup latency or minimal binary size.

## Decision

Adopt **.NET 10** as the runtime and **C# 13** as the source language. Use **Spectre.Console.Cli** as the CLI command/flag framework. Use **System.Text.Json** (stdlib) for all JSON serialization. Use **Microsoft.Extensions.DependencyInjection** and **Microsoft.Extensions.Logging** for DI and structured logging.

Publish as a self-contained single-file binary per platform:

```
dotnet publish -r <rid> /p:PublishSingleFile=true --self-contained true
```

Supported RIDs: `win-x64`, `osx-x64`, `osx-arm64`, `linux-x64`.

## Consequences

### Positive

- **POS-001**: Single-file, self-contained binary eliminates runtime installation requirements for end users and AI agents — download and execute without SDK setup.
- **POS-002**: .NET 10's cross-compilation with RID-specific publishing produces native-performance binaries for all three target platforms from a single codebase.
- **POS-003**: Spectre.Console.Cli provides first-class subcommand groups, strongly-typed settings classes, and generated `--help` output, reducing boilerplate and preventing common CLI design mistakes.
- **POS-004**: C# 13 nullable reference types (enabled project-wide) catch null-dereference bugs at compile time, improving reliability in unattended AI-agent usage.
- **POS-005**: System.Text.Json (stdlib) avoids a third-party JSON dependency and performs well for the tool's I/O-bound workload.

### Negative

- **NEG-001**: Self-contained binaries are significantly larger than framework-dependent builds (~60–80 MB per platform), which may slow initial distribution.
- **NEG-002**: Native AOT publishing (which would reduce binary size and startup time) is excluded from v1 scope because P/Invoke-based keychain implementations require runtime reflection.
- **NEG-003**: .NET 10 is a current LTS candidate at time of writing; any security patches require users to update the distributed binary, as there is no shared runtime.
- **NEG-004**: Spectre.Console.Cli's opinionated command model may require workarounds for edge-case flag interactions not anticipated by its design (e.g., deeply nested async commands).

## Alternatives Considered

##### Go (with Cobra)

- **ALT-001**: **Description**: Go produces very small, statically linked, zero-dependency binaries with excellent cross-compilation support. Cobra is the de facto CLI framework in the Go ecosystem.
- **ALT-002**: **Rejection Reason**: The team has no Go expertise; switching runtimes would require significant ramp-up time. P/Invoke-based keychain integration is idiomatic in .NET but requires more effort in Go's CGO layer.

##### Node.js / TypeScript (with Commander.js)

- **ALT-003**: **Description**: Node.js with TypeScript provides type safety and a large ecosystem. Commander.js or yargs handle CLI parsing. Can be packaged as a self-contained binary via `pkg` or `bun`.
- **ALT-004**: **Rejection Reason**: Node.js single-file packaging tools remain less mature and harder to distribute than `dotnet publish --self-contained`. V8 startup overhead is more noticeable for frequent short-lived CLI invocations.

##### Python (with Typer/Click)

- **ALT-005**: **Description**: Python has a rich ecosystem for CLI development. Typer provides type-annotated argument parsing.
- **ALT-006**: **Rejection Reason**: Python's packaging story for true zero-dependency single-file binaries (PyInstaller, Nuitka) is fragile across OS versions. The team has no production Python background.

##### Rust (with Clap)

- **ALT-007**: **Description**: Rust would produce tiny, fast binaries with no runtime. Clap is a full-featured CLI framework with derive macros.
- **ALT-008**: **Rejection Reason**: Rust has a steep learning curve and slow compile times. The team lacks experience; the time-to-delivery cost outweighs the runtime performance benefits for this class of tool.

## Implementation Notes

- **IMP-001**: All projects must set `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` in `.csproj` files.
- **IMP-002**: `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` must be configured globally so all JSON I/O (to files and stdout) uses `snake_case` property names consistently.
- **IMP-003**: The CI pipeline must build and publish binaries for all four RIDs as release artifacts. RID-specific tests (particularly keychain integration tests) run in platform-appropriate CI runners.
- **IMP-004**: `dotnet test` is used for unit tests; no separate test framework tooling is required.

## References

- **REF-001**: [tech_spec/tech_spec.md — Section 2: Runtime & Language](../../tech_spec/tech_spec.md#2-runtime--language)
- **REF-002**: [tech_spec/tech_spec.md — Section 13: Dependencies](../../tech_spec/tech_spec.md#13-dependencies)
- **REF-003**: [Spectre.Console.Cli documentation](https://spectreconsole.net/cli/getting-started)
- **REF-004**: [.NET 10 RID catalog](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog)
