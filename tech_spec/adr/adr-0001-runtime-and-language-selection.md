---
title: "ADR-0001: Runtime and Language Selection"
status: "Proposed"
date: "2026-03-17"
authors: "zapi-cli Engineering"
tags: ["architecture", "decision", "runtime", "language"]
supersedes: ""
superseded_by: ""
---

## Status

**Proposed**

## Context

zapi-cli is a standalone multi-platform CLI binary designed to interact with any Zoho product REST API. Its **primary consumer is AI agents** running via GitHub Copilot CLI Skills, which imposes specific constraints:

- The binary must be **self-contained and portable** — agents cannot be expected to manage runtime dependencies (no `dotnet` SDK required on the target machine).
- It must run on all major developer platforms: macOS (Apple Silicon and Intel), Windows x64, and Linux x64.
- The project team has existing expertise in the .NET ecosystem.
- The CLI must expose a minimal surface area with zero implicit prompts when run in agent mode (`--no-input`).
- Platform keychain access (macOS Security.framework, Windows Credential Manager, Linux libsecret) must be reachable without a native addon build step — ruling out Node.js without native addons.
- Strong typing and nullable reference types are required to enforce correctness at compile time, given the security-sensitive nature of credential handling.

The selection must also support a mature CLI framework to avoid hand-rolling argument parsing, subcommand routing, and output formatting.

## Decision

**Use .NET 10 / C# 13** as the runtime and language, published as a **self-contained single-file binary** per platform RID (`win-x64`, `osx-x64`, `osx-arm64`, `linux-x64`).

Key factors driving this decision:
- `dotnet publish -r <rid> /p:PublishSingleFile=true --self-contained true` produces a portable single-binary artifact with zero runtime dependencies.
- C# 13 nullable reference types enforce safe handling of credentials and nullable config fields at compile time.
- P/Invoke to platform keychain APIs (Security.framework, CredWrite, libsecret) is first-class without any native addon build toolchain.
- `Spectre.Console.Cli` — the chosen CLI framework — is a .NET-native library with excellent subcommand/flag modeling.
- .NET 10 is an LTS release with performance improvements relevant to startup time (important for AI agent invocations where latency accumulates across many CLI calls).

## Consequences

### Positive

- **POS-001**: Zero runtime dependency on agent machines — the self-contained single-file binary is drop-in installable.
- **POS-002**: Compile-time nullable enforcement prevents null-reference bugs in credential and config handling code paths.
- **POS-003**: P/Invoke to OS keychain APIs is natively supported with no extra tooling, keeping the binary entirely self-contained.
- **POS-004**: `System.Text.Json` (stdlib) handles all serialization — no third-party JSON dependency to version-pin.
- **POS-005**: Cross-platform support for all target RIDs from a single codebase with conditional compilation only at the keychain provider layer.

### Negative

- **NEG-001**: Binary artifact size is larger than Go or Rust equivalents (~50–80 MB self-contained vs ~5–15 MB), which matters if the binary is distributed or downloaded by agents.
- **NEG-002**: Cold-start latency on first invocation may be noticeable on Linux (JIT warm-up); mitigation via ReadyToRun is possible in a future publish flag.
- **NEG-003**: .NET 10 LTS lifecycle ties the project to Microsoft's release cadence — upgrading to .NET 12 (next LTS) will require periodic csproj retargeting.
- **NEG-004**: Contributors unfamiliar with C# face a steeper onboarding curve compared to a scripting language.

## Alternatives Considered

##### Go

- **ALT-001**: **Description**: Compile to a small self-contained binary natively; strong standard library for HTTP and JSON; excellent cross-compilation.
- **ALT-002**: **Rejection Reason**: No first-class P/Invoke equivalent — OS keychain access would require CGo, reintroducing a C toolchain build dependency. Team lacks Go expertise.

##### Node.js (TypeScript)

- **ALT-003**: **Description**: Familiar to the broader web ecosystem; cross-platform; excellent CLI libraries (commander, yargs).
- **ALT-004**: **Rejection Reason**: Cannot produce a self-contained binary without bundlers (pkg, nexe) that produce large, brittle artifacts. Native keychain libraries require node-gyp which breaks the zero-dependency install story.

##### Rust

- **ALT-005**: **Description**: Smallest binary size; native OS keychain crates exist; excellent performance.
- **ALT-006**: **Rejection Reason**: No team experience; significant ramp-up time. Async Rust adds complexity for a CLI that is largely sequential. Not ruled out for a future rewrite.

##### Python

- **ALT-007**: **Description**: Rapid prototyping; rich library ecosystem.
- **ALT-008**: **Rejection Reason**: Cannot produce a portable self-contained binary. Agent environments cannot guarantee a specific Python interpreter version.

## Implementation Notes

- **IMP-001**: Set `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` in all `.csproj` files.
- **IMP-002**: Publish command: `dotnet publish -r <rid> -c Release /p:PublishSingleFile=true --self-contained true`. CI must produce one artifact per supported RID.
- **IMP-003**: Add `<TrimmerRootDescriptor>` if tree-trimming is enabled in a future optimization pass — P/Invoke reflection paths require explicit rooting.
- **IMP-004**: Success metric: cold-start time under 300 ms on all supported RIDs measured in CI.

## References

- **REF-001**: [tech_spec.md — Section 2: Runtime & Language](../tech_spec.md#2-runtime--language)
- **REF-002**: [tech_spec.md — Section 13: Dependencies](../tech_spec.md#13-dependencies)
- **REF-003**: [.NET 10 Release Notes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10)
- **REF-004**: [ADR-0004: OS Keychain for Secret Storage](./adr-0004-os-keychain-for-secret-storage.md)
