---
goal: Refactor executable name and command structure for zapi-cli
version: "1.0"
date_created: 2026-04-01
owner: zapi-cli
status: 'Planned'
tags: [refactor, cli, naming, ux, breaking-change]
---

# Introduction

![Status: Planned](https://img.shields.io/badge/status-planned-blue)

Research comparing zapi-cli against industry-standard CLIs (`gh`, `az`, `fly`, `stripe`) and the [CLI Guidelines](https://clig.dev) revealed several ergonomic and structural issues with the current command surface. This plan covers all renames, restructures, and the binary rename needed to align zapi-cli with best-in-class CLI UX.

This is a **breaking change** to the public CLI interface. All renames must be accompanied by deprecated aliases held for one release cycle, documentation updates, and HELP.md regeneration.

---

## 1. Requirements & Constraints

- **REQ-001**: Executable must be renamed from `zapi-cli` to `zapi` across all build outputs.
- **REQ-002**: `api call` must be renamed to `api request` (`req` accepted as short alias).
- **REQ-003**: `trace session <verb>` three-level nesting must be flattened to `trace <verb>`.
- **REQ-004**: `scope add` and `scope list` top-level commands must move under `account scope add` / `account scope list`.
- **REQ-005**: `account set-default` must gain a `account use` alias (both forms must work).
- **REQ-006**: `account re-auth` must be renamed `account refresh` with `re-auth` kept as deprecated alias.
- **REQ-007**: `api registry` branch must be renamed `api endpoints`.
- **REQ-008**: `util time-ms` must be renamed `util timestamp`; `util time-now` must be renamed `util now`.
- **REQ-009**: All deprecated aliases must emit a deprecation warning to stderr on use: `Deprecation: '<old>' is deprecated; use '<new>' instead.`
- **REQ-010**: HELP.md must be regenerated to reflect all renames as the authoritative reference.
- **REQ-012**: `--zuidstring` must be renamed to `--zuid` on all `account` sub-commands. `--zuidstring` kept as deprecated alias.
- **REQ-013**: `account rename --new-name` must be renamed to `--to`. `--new-name` kept as deprecated alias.
- **REQ-014**: `trace close --wait-ms` must be renamed to `--drain-timeout` (value remains in milliseconds). `--wait-ms` kept as deprecated alias.
- **REQ-015**: Error codes `REGISTRY_ENTRY_ALREADY_EXISTS` and `REGISTRY_ENTRY_NOT_FOUND` must be renamed to `ENDPOINT_ALREADY_EXISTS` and `ENDPOINT_NOT_FOUND`.
- **CON-001**: No existing command **behaviour** changes — only naming and structure changes.
- **CON-002**: All tests referencing old command names must be updated in the same PR; tests must remain green.
- **CON-003**: `config.SetApplicationName` in `Program.cs` must change from `"zapi-cli"` to `"zapi"`.
- **GUD-001**: Follow [CLIG — Naming](https://clig.dev/#naming): lowercase, no redundant `-cli` suffix.
- **GUD-002**: Follow [CLIG — Subcommands](https://clig.dev/#subcommands): max two levels of nesting; noun-verb ordering.
- **GUD-003**: Deprecated aliases must NOT appear in `--help` output.
- **PAT-001**: Use Spectre.Console.Cli `WithAlias` / `AddCommand` overloads for alias registration.

---

## 2. Implementation Steps

### Phase 1 — Binary rename

- **GOAL-001**: Rename the output binary from `zapi-cli` / `zapi-cli.exe` to `zapi` / `zapi.exe`.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | Update `<AssemblyName>` in `src/ZapiCli/ZapiCli.csproj` from `zapi-cli` to `zapi` | | |
| TASK-002 | Update `<RootNamespace>` and output file name references in `ZapiCli.csproj` if required | | |
| TASK-003 | Update `config.SetApplicationName("zapi-cli")` → `"zapi"` in `Program.cs` | | |
| TASK-004 | Update `publish-all.sh` to output `build/osx-arm64/zapi`, `build/osx-x64/zapi`, etc. | | |
| TASK-005 | Update `docs/HELP.md` Installation section | | |
| TASK-007 | Update README.md binary table and all command examples | | |

---

### Phase 2 — Rename `api call` → `api request`

- **GOAL-002**: Replace the `call` sub-command under `api` with `request` (alias `req`); keep `call` as a deprecated alias.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-009 | In `Program.cs`, rename `api.AddCommand<ApiCommands.ApiCallCommand>("call")` to `"request"` and add `.WithAlias("req")` | | |
| TASK-010 | Add deprecated alias: `api.AddCommand<ApiCommands.ApiCallCommand>("call")` with deprecation warning injection | | |
| TASK-011 | Rename class `ApiCallCommand` → `ApiRequestCommand` in `ApiCommands.cs` | | |
| TASK-012 | Update all references to `ApiCallCommand` throughout the codebase | | |
| TASK-013 | Update HELP.md `api call` section to `api request` | | |

---

### Phase 3 — Flatten `trace session <verb>` → `trace <verb>`

- **GOAL-003**: Remove the intermediate `session` branch so trace commands sit at `trace <verb>`. Keep `trace session <verb>` as deprecated passthrough aliases.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-015 | In `Program.cs`, restructure the `trace` branch: move `start`, `list`, `export`, `close`, `reopen`, `remove` directly under `trace` | | |
| TASK-016 | Re-add `trace.AddBranch("session", ...)` with deprecation warnings | | |
| TASK-017 | Deprecation warning on stderr: `Deprecation: 'trace session <cmd>' is deprecated; use 'trace <cmd>' instead.` | | |
| TASK-018 | `trace config set` and `trace config show` remain under `trace config` (no change) | | |
| TASK-019 | Update HELP.md: replace all `trace session *` headings with `trace *` | | |
| TASK-021 | Update all tests in `tests/` that invoke `trace session *` | | |

---

### Phase 4 — Move `scope` branch under `account scope`

- **GOAL-004**: Consolidate OAuth scope management under the `account` branch. Top-level `scope` commands become deprecated passthroughs.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-022 | In `Program.cs`, add `account.AddBranch("scope", scope => { scope.AddCommand<ScopeAddCommand>("add"); scope.AddCommand<ScopeListCommand>("list"); })` | | |
| TASK-023 | Keep `config.AddBranch("scope", ...)` at the top level with deprecation warnings: `Deprecation: 'scope <cmd>' is deprecated; use 'account scope <cmd>' instead.` | | |
| TASK-024 | Update HELP.md: add `account scope add` / `account scope list` sections; mark old `scope` sections deprecated | | |
| TASK-026 | Update all tests that use `scope add` / `scope list` | | |

---

### Phase 5 — Add `account use` alias for `account set-default`

- **GOAL-005**: Add `account use <name>` as a positional shorthand.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-027 | In `Program.cs`, register `account.AddCommand<AccountSetDefaultCommand>("use")` | | |
| TASK-028 | `AccountSetDefaultCommand` — add support for positional `<NAME>` argument | | |
| TASK-029 | Update HELP.md: add `account use` section with positional example | | |

---

### Phase 6 — Rename `account re-auth` → `account refresh`

- **GOAL-006**: Rename `re-auth` to `refresh` to match `gh auth refresh` convention.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-031 | In `Program.cs`, rename `account.AddCommand<AccountReAuthCommand>("re-auth")` to `"refresh"` | | |
| TASK-032 | Re-add `account.AddCommand<AccountReAuthCommand>("re-auth")` with deprecation warning | | |
| TASK-033 | Rename class `AccountReAuthCommand` → `AccountRefreshCommand` | | |
| TASK-034 | Update HELP.md `account re-auth` section to `account refresh` | | |

---

### Phase 7 — Rename `api registry` → `api endpoints`

- **GOAL-007**: Replace `registry` with `endpoints`. Keep `api registry` as deprecated passthrough.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-036 | In `Program.cs`, rename `api.AddBranch("registry", ...)` to `"endpoints"` | | |
| TASK-037 | Re-add `api.AddBranch("registry", ...)` with deprecation warnings | | |
| TASK-038 | Rename `ApiRegistryCommands.cs` → `ApiEndpointCommands.cs`; rename all classes accordingly | | |
| TASK-039 | Update DI registrar and all references | | |
| TASK-040 | Update HELP.md `api registry *` sections to `api endpoints *` | | |
| TASK-041 | Rename error codes: `REGISTRY_ENTRY_ALREADY_EXISTS` → `ENDPOINT_ALREADY_EXISTS`; `REGISTRY_ENTRY_NOT_FOUND` → `ENDPOINT_NOT_FOUND` | | |
| TASK-042 | Update tests referencing registry command class names | | |

---

### Phase 8 — Rename `util time-ms` / `util time-now`

- **GOAL-008**: Remove hyphens from util sub-command names.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-043 | In `Program.cs`, rename `"time-ms"` to `"timestamp"`; add deprecated alias `"time-ms"` | | |
| TASK-044 | In `Program.cs`, rename `"time-now"` to `"now"`; add deprecated alias `"time-now"` | | |
| TASK-045 | Rename classes: `UtilTimeMsCommand` → `UtilTimestampCommand`; `UtilTimeNowCommand` → `UtilNowCommand` | | |
| TASK-046 | Update HELP.md util section | | |

---

### Phase 9 — Param name renames

- **GOAL-009**: Rename ergonomically poor parameter names.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-055 | In `AccountShowSettings`, `AccountSetDefaultSettings`, `AccountRemoveSettings`, `AccountRefreshSettings`, `AccountRenameSettings`: rename `ZuidString` property to `Zuid`; update attribute to `[CommandOption("--zuid\|--zuidstring")]` | | |
| TASK-056 | Update all internal `settings.ZuidString` references to `settings.Zuid` | | |
| TASK-057 | Update HELP.md: replace `--zuidstring` with `--zuid`; add footnote | | |
| TASK-058 | In `AccountRenameSettings`: rename `--new-name` to `--to`; add deprecated alias via `[CommandOption("--to\|--new-name")]` | | |
| TASK-059 | Update HELP.md `account rename` OPTIONS block | | |
| TASK-060 | In `TraceCloseSettings`: rename `--wait-ms` to `--drain-timeout`; add alias via `[CommandOption("--drain-timeout\|--wait-ms")]` | | |
| TASK-061 | Update HELP.md `trace close` OPTIONS block | | |
| TASK-062 | Update all integration tests that pass `--zuidstring`, `--new-name`, or `--wait-ms` | | |

---

### Phase 10 — Documentation & validation pass

- **GOAL-010**: Ensure all documentation and tests are consistent.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-048 | Full text search across the zapi-cli repo for `zapi-cli` (excluding git history); fix all remaining occurrences | | |
| TASK-049 | Full text search for `api call`, `trace session`, `scope add`, `scope list`, `re-auth`, `api registry`, `time-ms`, `time-now`, `zuidstring`, `new-name`, `wait-ms`; fix remaining occurrences | | |
| TASK-050 | Run full test suite: `dotnet test src/zapi-cli.sln` — all tests must pass | | |
| TASK-051 | Build all platform targets via `publish-all.sh`; verify binary names | | |
| TASK-052 | Manually invoke each deprecated alias and verify deprecation warning appears on stderr | | |
| TASK-053 | Manually invoke each new command and verify output contract is unchanged | | |
| TASK-054 | Update CHANGELOG / release notes with breaking change notice and migration table | | |

---

## 3. Alternatives

- **ALT-001**: Keep `zapi-cli` as the binary name — rejected; ergonomics compound over many sessions.
- **ALT-002**: Rename `account set-default` → `account default` — `account use` is more action-oriented.
- **ALT-003**: Flatten `trace` commands all the way to top-level — rejected; grouping under `trace` keeps namespace clean.
- **ALT-004**: Rename `api registry` → `api saved` or `api bookmarks` — `endpoints` is most technically accurate.
- **ALT-005**: Move `scope` under `auth` rather than `account` — rejected; scopes are properties of accounts.
- **ALT-006**: Keep `--zuidstring` as-is — rejected; the `string` suffix is implementation-leaked jargon.
- **ALT-007**: Rename `account rename --new-name` → `--rename-to` — `--to` is sufficient and shorter.
- **ALT-008**: Use `--timeout` (seconds) for `trace close` — rejected; changing the unit is a behaviour change.

---

## 4. Dependencies

- **DEP-001**: Spectre.Console.Cli — verify `WithAlias` is available for registering command aliases.
- **DEP-002**: `publish-all.sh` — build script; must be updated before platform binary names change.

---

## 5. Files

- **FILE-001**: `src/ZapiCli/Program.cs` — all command registrations; primary target for phases 1–8.
- **FILE-002**: `src/ZapiCli/Commands/ApiCommands.cs` — `ApiCallCommand` → `ApiRequestCommand`.
- **FILE-003**: `src/ZapiCli/Commands/ApiRegistryCommands.cs` → `ApiEndpointCommands.cs`.
- **FILE-004**: `src/ZapiCli/Commands/AccountCommands.cs` — `AccountReAuthCommand` → `AccountRefreshCommand`; positional arg; `--zuidstring` → `--zuid`; `--new-name` → `--to`.
- **FILE-005**: `src/ZapiCli/Commands/ScopeCommands.cs` — re-registered under new branch.
- **FILE-006**: `src/ZapiCli/Commands/TraceCommands.cs` — re-registered at flattened level; `--wait-ms` → `--drain-timeout`.
- **FILE-007**: `src/ZapiCli/Commands/UtilCommands.cs` — `UtilTimeMsCommand` → `UtilTimestampCommand`; `UtilTimeNowCommand` → `UtilNowCommand`.
- **FILE-008**: `src/ZapiCli/ZapiCli.csproj` — `<AssemblyName>zapi</AssemblyName>`.
- **FILE-009**: `publish-all.sh` — binary output path names.
- **FILE-010**: `docs/HELP.md` — authoritative user-facing reference.

---

## 6. Testing

- **TEST-001**: For each deprecated alias, assert `stderr` contains deprecation warning string.
- **TEST-002**: For `account use <NAME>` (positional), assert it sets the default account identically to `account set-default --name <NAME>`.
- **TEST-003**: For `account scope add` and `account scope list`, verify behaviour is identical to old `scope add` / `scope list`.
- **TEST-004**: For `trace *` commands, verify outputs are identical to old `trace session *` equivalents.
- **TEST-005**: Snapshot test — `--help` output must not contain `zapi-cli`, `re-auth`, `time-ms`, `time-now`, `call`, `registry`, or `zuidstring`.

---

## 7. Risks & Assumptions

- **RISK-001**: Published scripts using `zapi-cli` will break after the binary rename. Mitigate with a release note and one-release deprecation wrapper script.
- **RISK-002**: Spectre.Console.Cli may not support `WithAlias` for branch-level nested commands — verify before implementing Phase 3's `trace session` passthrough.
- **RISK-003**: Moving `scope` under `account` requires `account`'s branch config to accept sub-branches alongside leaf commands.

---

## 8. Related Specifications / Further Reading

- [CLI Guidelines — Naming](https://clig.dev/#naming)
- [CLI Guidelines — Subcommands](https://clig.dev/#subcommands)
- [command-reference.md](../docs/command-reference.md) — Post-refactor canonical command surface
- [`docs/HELP.md`](../../docs/HELP.md) — current authoritative command reference
