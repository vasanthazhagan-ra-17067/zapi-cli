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

Research comparing zapi-cli against industry-standard CLIs (GitHub CLI `gh`, Azure CLI `az`, Fly.io `fly`, Stripe CLI `stripe`) and the [CLI Guidelines](https://clig.dev) revealed several ergonomic and structural issues with the current command surface. This plan covers all renames, restructures, and the binary rename needed to align zapi-cli with best-in-class CLI UX.

This is a **breaking change** to the public CLI interface. All renames must be accompanied by deprecated aliases held for one release cycle, documentation updates, and HELP.md regeneration.

---

## 1. Requirements & Constraints

- **REQ-001**: Executable must be renamed from `zapi-cli` to `zapi` across all build outputs (osx-arm64, osx-x64, linux-x64, linux-arm64, win-x64, win-arm64).
- **REQ-002**: `api call` must be renamed to `api request` (`req` accepted as short alias).
- **REQ-003**: `trace session <verb>` three-level nesting must be flattened to `trace <verb>`.
- **REQ-004**: `scope add` and `scope list` top-level commands must move under `account scope add` / `account scope list`.
- **REQ-005**: `account set-default` must gain a `account use` alias (both forms must work).
- **REQ-006**: `account re-auth` must be renamed `account refresh` with `re-auth` kept as deprecated alias.
- **REQ-007**: `api registry` branch must be renamed `api endpoints`.
- **REQ-008**: `util time-ms` must be renamed `util timestamp`; `util time-now` must be renamed `util now`.
- **REQ-009**: All deprecated aliases must emit a deprecation warning to stderr on use: `Deprecation: '<old>' is deprecated; use '<new>' instead.`
- **REQ-010**: HELP.md must be regenerated to reflect all renames as the authoritative reference.
- **REQ-012**: `--zuidstring` must be renamed to `--zuid` on all `account` sub-commands (`show`, `set-default`, `remove`, `re-auth`/`refresh`, `rename`). The `string` suffix is redundant; `--zuidstring` must be kept as a deprecated alias.
- **REQ-013**: `account rename --new-name` must be renamed to `--to` (`zapi account rename --name old --to new`). `--new-name` kept as deprecated alias.
- **REQ-014**: `trace close --wait-ms` must be renamed to `--drain-timeout` (value remains in milliseconds). `--wait-ms` kept as deprecated alias.
- **REQ-015**: Error codes `REGISTRY_ENTRY_ALREADY_EXISTS` and `REGISTRY_ENTRY_NOT_FOUND` must be renamed to `ENDPOINT_ALREADY_EXISTS` and `ENDPOINT_NOT_FOUND` as part of the `api registry` → `api endpoints` rename.
- **CON-001**: No existing command **behaviour** changes — only naming and structure changes.
- **CON-002**: All tests referencing old command names must be updated in the same PR; tests must remain green.
- **CON-003**: `config.SetApplicationName` in `Program.cs` must change from `"zapi-cli"` to `"zapi"`.
- **GUD-001**: Follow [CLIG — Naming](https://clig.dev/#naming): lowercase, no redundant `-cli` suffix, short, memorable.
- **GUD-002**: Follow [CLIG — Subcommands](https://clig.dev/#subcommands): max two levels of nesting; noun-verb ordering.
- **GUD-003**: Deprecated aliases must NOT appear in `--help` output; they should only be documented in HELP.md.
- **PAT-001**: Use Spectre.Console.Cli `WithAlias` / `AddCommand` overloads for alias registration.

---

## 2. Implementation Steps

### Phase 1 — Binary rename

- **GOAL-001**: Rename the output binary from `zapi-cli` / `zapi-cli.exe` to `zapi` / `zapi.exe` in all build configurations and CI scripts.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | Update `<AssemblyName>` in `src/ZapiCli/ZapiCli.csproj` from `zapi-cli` to `zapi` | | |
| TASK-002 | Update `<RootNamespace>` and output file name references in `ZapiCli.csproj` if required | | |
| TASK-003 | Update `config.SetApplicationName("zapi-cli")` → `"zapi"` in `src/ZapiCli/Program.cs` (~line 105) | | |
| TASK-004 | Update `publish-all.sh` to output `build/osx-arm64/zapi`, `build/osx-x64/zapi`, etc. | | |
| TASK-005 | Update `docs/HELP.md` Installation section (`chmod +x ./zapi; mv ./zapi /usr/local/bin/zapi`) | | |
| TASK-007 | Update README.md binary table and all command examples | | |

---

### Phase 2 — Rename `api call` → `api request`

- **GOAL-002**: Replace the `call` sub-command under `api` with `request` (alias `req`); keep `call` as a deprecated alias.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-009 | In `src/ZapiCli/Program.cs`, rename `api.AddCommand<ApiCommands.ApiCallCommand>("call")` to `"request"` and add `.WithAlias("req")` | | |
| TASK-010 | Add deprecated alias: `api.AddCommand<ApiCommands.ApiCallCommand>("call").WithDescription("[Deprecated: use 'request'] ...")` and inject deprecation warning into `ApiCallCommand.ExecuteAsync` via `IsDeprecatedAlias` check | | |
| TASK-011 | Rename the class `ApiCallCommand` → `ApiRequestCommand` in `src/ZapiCli/Commands/ApiCommands.cs` | | |
| TASK-012 | Update all references to `ApiCallCommand` throughout the codebase (DI registrar, tests) | | |
| TASK-013 | Update HELP.md `api call` section header and all examples to use `zapi api request` | | |

---

### Phase 3 — Flatten `trace session <verb>` → `trace <verb>`

- **GOAL-003**: Remove the intermediate `session` branch so trace commands sit at `trace <verb>` (two levels, not three). Keep `trace session <verb>` as deprecated passthrough aliases.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-015 | In `Program.cs`, restructure the `trace` branch: move `start`, `list`, `export`, `close`, `reopen`, `remove` directly under `trace` | | |
| TASK-016 | Re-add `trace.AddBranch("session", ...)` containing the same commands with deprecation warnings injected | | |
| TASK-017 | The deprecation warning must print to stderr: `Deprecation: 'trace session <cmd>' is deprecated; use 'trace <cmd>' instead.` | | |
| TASK-018 | `trace config set` and `trace config show` remain under `trace config` (no change) | | |
| TASK-019 | Update HELP.md: replace all `trace session *` headings and examples with `trace *` | | |
| TASK-021 | Update all tests in `tests/` that invoke `trace session *` to use `trace *` | | |

---

### Phase 4 — Move `scope` branch under `account scope`

- **GOAL-004**: Consolidate OAuth scope management under the `account` branch for cohesion. Top-level `scope` commands become deprecated passthroughs.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-022 | In `Program.cs`, add `account.AddBranch("scope", scope => { scope.AddCommand<ScopeAddCommand>("add"); scope.AddCommand<ScopeListCommand>("list"); })` | | |
| TASK-023 | Keep `config.AddBranch("scope", ...)` at the top level with deprecation warnings: `Deprecation: 'scope <cmd>' is deprecated; use 'account scope <cmd>' instead.` | | |
| TASK-024 | Update HELP.md: add new `account scope add` / `account scope list` sections; mark old `scope` sections deprecated | | |
| TASK-026 | Update all tests that use `scope add` / `scope list` to use `account scope add` / `account scope list` | | |

---

### Phase 5 — Add `account use` alias for `account set-default`

- **GOAL-005**: Add `account use <name>` as a positional shorthand for setting the default account, matching `kubectl config use-context` ergonomics.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-027 | In `Program.cs`, register `account.AddCommand<AccountSetDefaultCommand>("use").WithDescription("Set the default account (shorthand for set-default).")` | | |
| TASK-028 | `AccountSetDefaultCommand` already accepts `--name`; add support for a positional `<NAME>` argument so `zapi account use myaccount` works without `--name` flag | | |
| TASK-029 | Update HELP.md: add `account use` section with positional example | | |

---

### Phase 6 — Rename `account re-auth` → `account refresh`

- **GOAL-006**: Rename `re-auth` to `refresh` to match `gh auth refresh` convention. Keep `re-auth` as deprecated alias.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-031 | In `Program.cs`, rename `account.AddCommand<AccountReAuthCommand>("re-auth")` to `"refresh"` | | |
| TASK-032 | Re-add `account.AddCommand<AccountReAuthCommand>("re-auth")` with deprecation warning injection | | |
| TASK-033 | Rename class `AccountReAuthCommand` → `AccountRefreshCommand` in `AccountCommands.cs` | | |
| TASK-034 | Update HELP.md `account re-auth` section to `account refresh` | | |

---

### Phase 7 — Rename `api registry` → `api endpoints`

- **GOAL-007**: Replace the overloaded term `registry` with the more descriptive `endpoints`. Keep `api registry` as a deprecated passthrough branch.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-036 | In `Program.cs`, rename `api.AddBranch("registry", ...)` to `"endpoints"` | | |
| TASK-037 | Re-add `api.AddBranch("registry", ...)` with deprecation warnings on each sub-command | | |
| TASK-038 | Rename `ApiRegistryCommands.cs` → `ApiEndpointCommands.cs`; update class names (`ApiRegistryListCommand` → `ApiEndpointListCommand`, etc.) | | |
| TASK-039 | Update DI registrar and all references in the project | | |
| TASK-040 | Update HELP.md `api registry *` sections to `api endpoints *` | | |
| TASK-041 | Rename error codes in `ErrorCodes.cs` (or equivalent): `REGISTRY_ENTRY_ALREADY_EXISTS` → `ENDPOINT_ALREADY_EXISTS`; `REGISTRY_ENTRY_NOT_FOUND` → `ENDPOINT_NOT_FOUND`; update all throw sites and HELP.md error tables | | |
| TASK-042 | Update tests referencing registry command class names | | |

---

### Phase 8 — Rename `util time-ms` / `util time-now`

- **GOAL-008**: Remove hyphens from util sub-command names; rename to single-word, self-descriptive names.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-043 | In `Program.cs`, rename `util.AddCommand<UtilTimeMsCommand>("time-ms")` to `"timestamp"`; add deprecated alias `"time-ms"` | | |
| TASK-044 | In `Program.cs`, rename `util.AddCommand<UtilTimeNowCommand>("time-now")` to `"now"`; add deprecated alias `"time-now"` | | |
| TASK-045 | Rename classes: `UtilTimeMsCommand` → `UtilTimestampCommand`; `UtilTimeNowCommand` → `UtilNowCommand` in `UtilCommands.cs` | | |
| TASK-046 | Update HELP.md util section | | |

---

### Phase 9 — Param name renames

- **GOAL-009**: Rename ergonomically poor parameter names across the existing command surface. Each rename follows a deprecated-alias pattern identical to command renames (old param emits a deprecation warning to stderr).

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-055 | In `AccountShowSettings`, `AccountSetDefaultSettings`, `AccountRemoveSettings`, `AccountRefreshSettings`, `AccountRenameSettings`: rename the `ZuidString` C# property to `Zuid`; update the `[CommandOption("--zuidstring")]` attribute to `[CommandOption("--zuid\|--zuidstring")]` (Spectre multi-option syntax gives `--zuidstring` as a transparent alias) | | |
| TASK-056 | In `AccountCommands.cs`, update all internal `settings.ZuidString` references to `settings.Zuid` | | |
| TASK-057 | Update HELP.md: replace all `--zuidstring` option references with `--zuid`; add footnote `--zuidstring is accepted as a deprecated alias` | | |
| TASK-058 | In `AccountRenameSettings`: rename `--new-name` to `--to`; add `--new-name` as a deprecated multi-option alias via `[CommandOption("--to\|--new-name")]` | | |
| TASK-059 | Update HELP.md `account rename` OPTIONS block: replace `--new-name <NEW_NAME>` with `--to <NEW_NAME>` | | |
| TASK-060 | In `TraceCloseSettings`: rename `--wait-ms` to `--drain-timeout`; add `--wait-ms` as a deprecated alias via `[CommandOption("--drain-timeout\|--wait-ms")]` | | |
| TASK-061 | Update HELP.md `trace session close` OPTIONS block: replace `--wait-ms <MS>` with `--drain-timeout <MS>  (milliseconds; default: 5000)` | | |
| TASK-062 | Update all integration tests that pass `--zuidstring`, `--new-name`, or `--wait-ms` to use the new flag names | | |

---

### Phase 10 — Documentation & validation pass

- **GOAL-010**: Ensure all documentation and tests are consistent before marking this plan complete.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-048 | Full text search across the zapi-cli repo for `zapi-cli` (excluding git history); fix all remaining occurrences | | |
| TASK-049 | Full text search for `api call`, `trace session`, `scope add`, `scope list`, `re-auth`, `api registry`, `time-ms`, `time-now`, `zuidstring`, `new-name`, `wait-ms`; fix remaining occurrences | | |
| TASK-050 | Run full test suite: `dotnet test src/zapi-cli.sln` — all tests must pass | | |
| TASK-051 | Build all platform targets via `publish-all.sh`; verify binary names on each platform | | |
| TASK-052 | Manually invoke each deprecated alias and verify deprecation warning appears on stderr | | |
| TASK-053 | Manually invoke each new command and verify output contract is unchanged | | |
| TASK-054 | Update CHANGELOG / release notes with breaking change notice and migration table | | |

---

## 3. Alternatives

- **ALT-001**: Keep `zapi-cli` as the binary name and only fix commands — rejected because the binary name is the most-typed string and ergonomics compound over many sessions.
- **ALT-002**: Rename `account set-default` → `account default` instead of adding `account use` — `account use` is more action-oriented and matches the `kubectl use-context` mental model; `account default` could be confused with a read command.
- **ALT-003**: Flatten `trace` commands all the way to top-level (`zapi start-trace`, `zapi export-trace`) — rejected because grouping under `trace` keeps the namespace clean and consistent with other tools' sub-command style.
- **ALT-004**: Rename `api registry` → `api saved` or `api bookmarks` — `endpoints` was chosen as it's the most technically accurate and immediately understood term.
- **ALT-005**: Move `scope` under `auth` rather than `account` — rejected because there is no `auth` top-level branch; scopes are properties of accounts, so `account scope` is the direct parent.
- **ALT-006**: Keep `--zuidstring` as-is — rejected; the `string` suffix is implementation-leaked jargon. `--zuid` is the identity value, not its type.
- **ALT-007**: Rename `account rename --new-name` → `--rename-to` — `--to` is sufficient and shorter; `mv source dest` convention supports single-word destinations.
- **ALT-008**: Use `--timeout` (seconds) instead of `--drain-timeout` (ms) for `trace close` — rejected; changing the unit is a behaviour change (CON-001 forbids this). `--drain-timeout` retains ms semantics with a more descriptive name.

---

## 4. Dependencies

- **DEP-001**: Spectre.Console.Cli — already in use; verify `WithAlias` is available in the current version for registering command aliases.
- **DEP-002**: `publish-all.sh` — build script; must be updated before platform binary names change.

---

## 5. Files

- **FILE-001**: `src/ZapiCli/Program.cs` — all command registrations; primary target for phases 1–8.
- **FILE-002**: `src/ZapiCli/Commands/ApiCommands.cs` — `ApiCallCommand` → `ApiRequestCommand`.
- **FILE-003**: `src/ZapiCli/Commands/ApiRegistryCommands.cs` → `ApiEndpointCommands.cs` — full class rename.
- **FILE-004**: `src/ZapiCli/Commands/AccountCommands.cs` — `AccountReAuthCommand` → `AccountRefreshCommand`; positional arg for `AccountSetDefaultCommand`; `--zuidstring` → `--zuid` on all account settings classes; `--new-name` → `--to` on `AccountRenameSettings`.
- **FILE-005**: `src/ZapiCli/Commands/ScopeCommands.cs` — no logic changes; only re-registered under new branch.
- **FILE-006**: `src/ZapiCli/Commands/TraceCommands.cs` — no logic changes; commands re-registered at flattened level; `--wait-ms` → `--drain-timeout` on `TraceCloseSettings`.
- **FILE-007**: `src/ZapiCli/Commands/UtilCommands.cs` — `UtilTimeMsCommand` → `UtilTimestampCommand`; `UtilTimeNowCommand` → `UtilNowCommand`.
- **FILE-008**: `src/ZapiCli/ZapiCli.csproj` — `<AssemblyName>zapi</AssemblyName>`.
- **FILE-009**: `publish-all.sh` — binary output path names.
- **FILE-010**: `docs/HELP.md` — authoritative user-facing reference; all sections updated.

---

## 6. Testing

- **TEST-001**: For each deprecated alias (11 total: `call`, `trace session *` ×6, `scope add/list`, `re-auth`, `api registry`, `time-ms`, `time-now`, `--zuidstring`, `--new-name`, `--wait-ms`), write an integration test asserting that `stderr` contains the deprecation warning string and `stdout` / exit code matches the canonical form.
- **TEST-002**: For `account use <NAME>` (positional), write a test asserting it sets the default account identically to `account set-default --name <NAME>`.
- **TEST-003**: For `account scope add` and `account scope list`, verify behaviour is identical to the old `scope add` / `scope list`.
- **TEST-004**: For `trace start`, `trace list`, `trace export`, `trace close`, `trace reopen`, `trace remove`, verify outputs are identical to the old `trace session *` equivalents.
- **TEST-005**: Snapshot test — capture the `--help` output for the root command after all changes and assert it no longer contains `zapi-cli`, `re-auth`, `time-ms`, `time-now`, `call` (under `api`), `registry`, or `zuidstring`.

---

## 7. Risks & Assumptions

- **RISK-001**: Any published scripts or CI configurations using `zapi-cli` will break after the binary rename. Mitigate with a release note and a one-release deprecation wrapper script (`zapi-cli` → exec `zapi`).
- **RISK-002**: Spectre.Console.Cli may not support the `WithAlias` pattern for branch-level (nested) commands — verify before implementing Phase 3's `trace session` passthrough.
- **RISK-003**: Moving `scope` under `account` requires `account`'s branch config to accept sub-branches alongside leaf commands; verify Spectre.Console.Cli allows mixed branches and leaf commands at the same level.
- **ASSUMPTION-001**: No external tooling or CI configurations currently depend on the `zapi-cli` binary name except `docs/HELP.md` and `README.md`, which are both in-scope for this plan.
- **ASSUMPTION-002**: `account use <NAME>` will accept the name as a positional argument; the existing `AccountSetDefaultCommand` will need a small settings-class change to expose `[CommandArgument(0, "[NAME]")]`.

---

## 8. Related Specifications / Further Reading

- [CLI Guidelines — Naming](https://clig.dev/#naming)
- [CLI Guidelines — Subcommands](https://clig.dev/#subcommands)
- [CLI Guidelines — Future-proofing](https://clig.dev/#future-proofing)
- [GitHub CLI (`gh`) command reference](https://cli.github.com/manual/)
- [Azure CLI (`az`) reference index](https://learn.microsoft.com/en-us/cli/azure/reference-index)
- [`docs/HELP.md`](../docs/HELP.md) — current authoritative command reference
