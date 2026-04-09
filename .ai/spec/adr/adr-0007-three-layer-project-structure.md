---
title: "ADR-0007: Three-Layer Project Structure (CLI / Core / Keychain)"
status: "Accepted"
date: "2026-03-18"
authors: "zapi-cli Core Team"
tags: ["architecture", "decision", "project-structure", "separation-of-concerns"]
supersedes: ""
superseded_by: ""
---

# ADR-0007: Three-Layer Project Structure (CLI / Core / Keychain)

## Status

**Accepted**

## Context

zapi-cli is a multi-platform CLI tool with three distinct concerns that have different dependencies and testability requirements:

1. **CLI presentation layer**: Spectre.Console.Cli wiring, command classes, flag parsing. Hard dependency on the CLI framework and console I/O.
2. **Domain logic**: Account management, authentication flows, API invocation, trace sessions. Must be testable in isolation without a real console, real OS keychain, or real HTTP server.
3. **OS keychain access**: Platform-specific P/Invoke calls. Has hard dependencies on native libraries.

## Decision

Split the solution into three .NET projects with the following dependency graph:

```
ZapiCli (entry point + Spectre wiring)
    → ZapiCli.Core (domain logic)
    → ZapiCli.Keychain (OS keychain abstraction)

ZapiCli.Core (domain logic)
    → ZapiCli.Keychain (IKeychainProvider interface + implementations)
```

**`ZapiCli`** (`src/ZapiCli/`): `Program.cs`, `Commands/`, `DependencyInjectionRegistrar.cs`. No business logic.

**`ZapiCli.Core`** (`src/ZapiCli.Core/`): `Auth/`, `Accounts/`, `Api/`, `Trace/`, `Pex/`. Zero references to Spectre.Console or any console/output framework.

**`ZapiCli.Keychain`** (`src/ZapiCli.Keychain/`): `IKeychainProvider` interface + platform implementations. Zero references to `ZapiCli.Core` or `ZapiCli`.

**Tests** (`tests/ZapiCli.Tests/`): References `ZapiCli.Core` and `ZapiCli.Keychain` only.

## Consequences

### Positive

- **POS-001**: `ZapiCli.Core` has no CLI or OS keychain dependencies; its entire domain logic can be unit-tested with pure in-memory mocks.
- **POS-002**: The `ZapiCli.Keychain` leaf project encapsulates all P/Invoke complexity in one place.
- **POS-003**: The three-way separation enforces the architecture at compile time.
- **POS-004**: Future tooling can reference `ZapiCli.Core` as a library without pulling in the CLI framework or P/Invoke code.

### Negative

- **NEG-001**: Three projects add solution overhead.
- **NEG-002**: DI registration must be kept in sync with new services; no compile-time check that all required services are registered.

## Implementation Notes

- **IMP-001**: The project reference graph must be enforced in CI.
- **IMP-002**: `ZapiCli.Tests` references only `ZapiCli.Core` and `ZapiCli.Keychain` directly.
- **IMP-003**: All services must be registered via `DependencyInjectionRegistrar.cs` in `ZapiCli`.
- **IMP-004**: `IOutputWriter` lives in `ZapiCli.Core` and is injected into command-level services.

## References

- **REF-001**: [ADR-0005: OS Keychain Abstraction via P/Invoke](adr-0005-os-keychain-abstraction.md)
- **REF-002**: [ADR-0001: Technology Stack](adr-0001-technology-stack.md)
