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

1. **CLI presentation layer**: Spectre.Console.Cli wiring, command classes, flag parsing, output formatting. Has a hard dependency on the CLI framework and console I/O.
2. **Domain logic**: Account management, authentication flows, API invocation, trace sessions. Must be testable in isolation without a real console, real OS keychain, or real HTTP server.
3. **OS keychain access**: Platform-specific P/Invoke calls to macOS Security.framework, Windows Credential Manager, and Linux libsecret. Has hard dependencies on native libraries and is inherently platform-specific.

Mixing these concerns in a single project would:
- Make unit testing difficult (all tests would require mocking CLI frameworks or OS APIs).
- Increase coupling between platform-specific P/Invoke code and domain logic, complicating future platform additions.
- Prevent the domain layer from being packaged or reused independently in future tooling.

## Decision

Split the solution into three .NET projects with the following dependency graph:

```
ZapiCli (entry point + Spectre wiring)
    → ZapiCli.Core (domain logic)
    → ZapiCli.Keychain (OS keychain abstraction)

ZapiCli.Core (domain logic)
    → ZapiCli.Keychain (IKeychainProvider interface + implementations)
```

**`ZapiCli`** (`src/ZapiCli/`):
- `Program.cs` — DI container bootstrap, Spectre.Console.Cli app builder, entry point.
- `Commands/` — one file per command group; all classes implement Spectre's `AsyncCommand<TSettings>`.
- `DependencyInjectionRegistrar.cs` — registers all services; selects platform keychain provider.
- No business logic; delegates all work to services from `ZapiCli.Core`.

**`ZapiCli.Core`** (`src/ZapiCli.Core/`):
- `Auth/` — `IAuthProvider`, `OAuthProvider`.
- `Accounts/` — `AccountStore`, `AccountEntry`, `AccountsRoot`.
- `Api/` — `ApiClient`, `ApiRegistry`.
- `Trace/` — `TraceSession`, `TraceWriter`, `TraceEntry`, `TraceExporter`.
- `Pex/` — `PexBuffer` (future).
- Zero references to Spectre.Console or any console/output framework.
- All side-effectful dependencies (`IKeychainProvider`, `IAccountStore`, `HttpClient`) injected via DI interfaces.

**`ZapiCli.Keychain`** (`src/ZapiCli.Keychain/`):
- `IKeychainProvider` interface (no platform dependencies).
- Platform implementations: `MacOsKeychainProvider`, `WindowsKeychainProvider`, `LinuxKeychainProvider`, `EncryptedFileKeychainProvider`.
- Zero references to `ZapiCli.Core` or `ZapiCli` — this project is a pure infrastructure leaf.

**Tests** (`tests/ZapiCli.Tests/`):
- References `ZapiCli.Core` and `ZapiCli.Keychain` only; does not reference `ZapiCli` to avoid CLI framework test setup.
- Uses interface mocks for keychain, HTTP, and file system; no production OS dependencies in unit tests.

## Consequences

### Positive

- **POS-001**: `ZapiCli.Core` has no CLI or OS keychain dependencies; its entire domain logic can be unit-tested with pure in-memory mocks and no platform-specific setup.
- **POS-002**: The `ZapiCli.Keychain` leaf project encapsulates all P/Invoke complexity in one place; changes to native APIs (e.g., macOS deprecating old keychain APIs) are isolated to a single project.
- **POS-003**: The three-way separation enforces the architecture at compile time — `ZapiCli.Core` cannot accidentally call Spectre.Console APIs, and `ZapiCli.Keychain` cannot access domain models.
- **POS-004**: Future tooling (e.g., a VS Code extension, a GitHub Actions action) can reference `ZapiCli.Core` as a library without pulling in the CLI framework or P/Invoke code.

### Negative

- **NEG-001**: Three projects add solution overhead — more `.csproj` files, project references, and cross-project type sharing to manage compared to a single-project approach.
- **NEG-002**: DI registration (`DependencyInjectionRegistrar.cs`) must be kept in sync with new services added to `ZapiCli.Core`; there is no compile-time check that all required services are registered.
- **NEG-003**: Cross-cutting concerns (e.g., logging configuration) span all three layers and must be threaded through via `Microsoft.Extensions.Logging` abstractions to avoid circular dependencies.

## Alternatives Considered

##### Single-Project Monolith

- **ALT-001**: **Description**: All code (CLI, domain, keychain) in a single `ZapiCli` project. Simplest setup; one `.csproj` to maintain.
- **ALT-002**: **Rejection Reason**: A single project makes it structurally difficult to enforce layer boundaries. Over time, CLI framework calls inevitably leak into domain logic, and P/Invoke code spreads into business rules. Unit testing becomes harder as every test requires the CLI framework and platform keychain to be present.

##### Two Projects (CLI + Core with Keychain Merged into Core)

- **ALT-003**: **Description**: `ZapiCli` (CLI) + `ZapiCli.Core` (domain + keychain). Simpler dependency graph; fewer project files.
- **ALT-004**: **Rejection Reason**: Merging keychain code into Core means every test of Core domain logic must deal with P/Invoke and platform availability. The keychain implementations have distinct platform dependencies that make them poor test neighbors for pure domain logic.

##### Four Projects (CLI / Domain / Keychain / Abstractions)

- **ALT-005**: **Description**: Extract all interfaces (`IAuthProvider`, `IAccountStore`, `IKeychainProvider`) into a fourth `ZapiCli.Abstractions` project to break circular-dependency risks.
- **ALT-006**: **Rejection Reason**: For the current scope, there are no circular dependencies between the three layers. Introducing a fourth project adds complexity without solving an actual problem. Revisit if inter-layer dependencies become circular in future phases.

## Implementation Notes

- **IMP-001**: The project reference graph must be enforced in CI by running `dotnet build` with no `<ProjectReference>` from `ZapiCli.Core` to `ZapiCli`, and none from `ZapiCli.Keychain` to either.
- **IMP-002**: `ZapiCli.Tests` references only `ZapiCli.Core` and `ZapiCli.Keychain` directly. CLI command integration tests (end-to-end) may be added as a separate test project referencing `ZapiCli` in a future phase.
- **IMP-003**: All services in `ZapiCli.Core` and `ZapiCli.Keychain` must be registered via `DependencyInjectionRegistrar.cs` in `ZapiCli`. No service should use `new ServiceClass(...)` directly outside of test setup.
- **IMP-004**: `IOutputWriter` (for capturing stdout/stderr in tests) lives in `ZapiCli.Core` and is injected into command-level services, allowing unit tests to assert on output without any console dependency.

## References

- **REF-001**: [tech_spec/tech_spec.md — Section 3: Project Structure](../tech_spec/tech_spec.md#3-project-structure)
- **REF-002**: [ADR-0005: OS Keychain Abstraction via P/Invoke](adr-0005-os-keychain-abstraction.md)
- **REF-003**: [ADR-0001: Technology Stack](adr-0001-technology-stack.md)
