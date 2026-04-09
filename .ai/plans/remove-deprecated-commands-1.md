---
goal: Remove all deprecated command aliases and shim classes from the codebase
version: "1.0"
date_created: 2026-04-08
owner: zapi-cli
status: 'Planned'
tags: [cleanup, cli, deprecated, breaking-change]
---

# Introduction

![Status: Planned](https://img.shields.io/badge/status-planned-blue)

The `refactor-cli-naming-1` plan introduced new canonical commands alongside deprecated wrapper aliases for a one-release migration window. That window has passed. This plan removes every deprecated shim command, class, and helper from the codebase so that running `zapi account -h` (or any other `--help`) shows only clean, current commands with zero `[Deprecated]` noise.

---

## Deprecated surface being removed

| Old command | New command | Where defined |
|---|---|---|
| `account re-auth` | `account refresh` | `AccountCommands.cs` — `AccountReAuthDeprecatedCommand` |
| `api call` | `api request` / `api req` | `ApiCommands.cs` — `ApiCallCommand` (deprecated wrapper) |
| `api registry list/add/update/show/remove` | `api endpoints *` | `ApiRegistryCommands.cs` — `ApiRegistry*Command` shims |
| `util time-ms` | `util timestamp` | `UtilCommands.cs` — `UtilTimeMsDeprecatedCommand` |
| `util time-now` | `util now` | `UtilCommands.cs` — `UtilTimeNowDeprecatedCommand` |
| `scope add` (top-level) | `account scope add` | `ScopeCommands.cs` — `ScopeAddDeprecatedCommand` |
| `scope list` (top-level) | `account scope list` | `ScopeCommands.cs` — `ScopeListDeprecatedCommand` |
| `trace session start/list/export/close/reopen/remove` | `trace *` | `TraceCommands.cs` — `DeprecatedSession*Command` (6 classes) |

---

## 1. Requirements & Constraints

- **REQ-001**: All deprecated command registrations must be removed from `Program.cs`.
- **REQ-002**: All deprecated command shim classes must be deleted from their source files.
- **REQ-003**: `DeprecationHelper.cs` must be deleted once it has no remaining callers.
- **REQ-004**: `DeprecationHelperTests.cs` must be deleted alongside `DeprecationHelper.cs`.
- **REQ-005**: All new (canonical) commands must remain fully functional and tested.
- **REQ-006**: `dotnet test` must pass with zero failures after all changes.
- **CON-001**: Tests that exercise the underlying service logic (not the deprecated command classes themselves) must be preserved unchanged.
- **CON-002**: Test section-header comments that reference old command names should be updated to reflect current command names.

---

## 2. Implementation Steps

### Phase 1 — Remove deprecated registrations from `Program.cs`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | Remove `account.AddCommand<AccountCommands.AccountReAuthDeprecatedCommand>("re-auth")` block | | |
| TASK-002 | Remove `api.AddCommand<ApiCommands.ApiCallCommand>("call")` block | | |
| TASK-003 | Remove the entire `api.AddBranch("registry", registry => { ... })` block (5 registry shim registrations) | | |
| TASK-004 | Remove `util.AddCommand<UtilCommands.UtilTimeMsDeprecatedCommand>("time-ms")` entry | | |
| TASK-005 | Remove `util.AddCommand<UtilCommands.UtilTimeNowDeprecatedCommand>("time-now")` entry | | |
| TASK-006 | Remove the entire top-level `config.AddBranch("scope", ...)` block (`ScopeAddDeprecatedCommand` and `ScopeListDeprecatedCommand`) | | |
| TASK-007 | Remove the `trace.AddBranch("session", session => { ... })` block (6 deprecated session shim registrations) | | |

---

### Phase 2 — Delete deprecated shim classes from command source files

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-008 | In `AccountCommands.cs`: delete `AccountReAuthDeprecatedCommand` class | | |
| TASK-009 | In `ApiCommands.cs`: delete `ApiCallCommand` deprecated wrapper class | | |
| TASK-010 | In `ApiRegistryCommands.cs`: delete all 5 `ApiRegistry*Command` shim classes and the `// ─── deprecated shims` section comment | | |
| TASK-011 | In `UtilCommands.cs`: delete `UtilTimeMsDeprecatedCommand` and `UtilTimeNowDeprecatedCommand` classes | | |
| TASK-012 | In `ScopeCommands.cs`: delete `ScopeAddDeprecatedCommand` and `ScopeListDeprecatedCommand` classes | | |
| TASK-013 | In `TraceCommands.cs`: delete all 6 `DeprecatedSession*Command` classes and the `// ─── Deprecated 'trace session *' shims` section comment | | |

---

### Phase 3 — Delete `DeprecationHelper`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-014 | Verify no remaining `DeprecationHelper.Warn(` calls exist anywhere in `src/` | | |
| TASK-015 | Delete `src/ZapiCli/Commands/DeprecationHelper.cs` | | |
| TASK-016 | Delete `tests/ZapiCli.Tests/DeprecationHelperTests.cs` | | |

---

### Phase 4 — Update test comments

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-017 | In `UtilCommandTests.cs`: rename section comment `// ── util time-ms` → `// ── util timestamp` | | |
| TASK-018 | In `UtilCommandTests.cs`: rename section comment `// ── util time-now` → `// ── util now` | | |
| TASK-019 | In `AccountCommandTests.cs`: rename section comment `// ── account re-auth` → `// ── account refresh` | | |
| TASK-020 | In `ScopeCommandTests.cs`: rename section comments `// ── scope add` → `// ── account scope add` and `// ── scope list` → `// ── account scope list` | | |
| TASK-021 | In `ApiRegistryTests.cs`: update the file-level summary comment from `the <c>api registry</c> command group` to `the <c>api endpoints</c> command group` | | |

---

### Phase 5 — Documentation updates

Remove all references to deprecated commands, flags, and the "Deprecated Commands" section from documentation files.

| Task | File | Change | Completed | Date |
|------|------|--------|-----------|------|
| TASK-067 | `docs/HELP.md` — Table of Contents | Remove `*(was: ...)*` suffixes from all 11 renamed command entries | ✅ | 2026-04-08 |
| TASK-068 | `docs/HELP.md` — account OPTIONS blocks | Remove `*(deprecated alias: --zuidstring)*` notes | ✅ | 2026-04-08 |
| TASK-069 | `docs/HELP.md` — `account refresh` section | Remove `*(was: account re-auth — deprecated alias still accepted)*` paragraph | ✅ | 2026-04-08 |
| TASK-070 | `docs/HELP.md` — `api request` section | Remove deprecation-notice blockquote about `api call` | ✅ | 2026-04-08 |
| TASK-071 | `docs/HELP.md` — `api endpoints *` sections | Remove all `*(was: api registry * — deprecated)*` paragraphs | ✅ | 2026-04-08 |
| TASK-072 | `docs/HELP.md` — `account scope add/list` sections | Remove `*(was: scope add/list — deprecated)*` paragraphs | ✅ | 2026-04-08 |
| TASK-073 | `docs/HELP.md` — `util timestamp` / `util now` sections | Remove `*(was: util time-ms/time-now — deprecated)*` paragraphs | ✅ | 2026-04-08 |
| TASK-074 | `docs/HELP.md` — `trace *` sections | Remove all `*(was: trace session * — deprecated)*` paragraphs from 6 trace sub-commands | ✅ | 2026-04-08 |
| TASK-075 | `docs/HELP.md` — `trace close` OPTIONS | Remove `*(deprecated alias: --wait-ms)*` | ✅ | 2026-04-08 |
| TASK-076 | `docs/HELP.md` — `## Deprecated Commands` section | Delete the entire section | ✅ | 2026-04-08 |
| TASK-077 | `tech_spec/docs/command-reference.md` — Deprecation Contract | Replace with single-line note | ✅ | 2026-04-08 |
| TASK-080 | `tech_spec/docs/command-reference.md` — `## Error Code Changes` and `## Full Rename Map` | Delete both sections | ✅ | 2026-04-08 |

---

### Phase 5 — Validation

#### 5a — Build & unit tests

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-022 | Run `dotnet build src/zapi-cli.sln` — must succeed with 0 errors | ✅ | 2026-04-08 |
| TASK-023 | Run `dotnet test src/zapi-cli.sln --logger "console;verbosity=normal"` — all tests must pass | ✅ | 2026-04-08 |

#### 5b — Help output clean-up checks (no `[Deprecated]` entries anywhere)

| Task | Command | Expected |
|------|---------|----------|
| TASK-024 | `./build/osx-arm64/zapi -h` | No `scope` branch listed at the top level |
| TASK-025 | `./build/osx-arm64/zapi account -h` | Commands: `login`, `list`, `show`, `set-default`, `use`, `remove`, `refresh`, `rename`, `scope` — no `re-auth`, no `[Deprecated]` |
| TASK-026 | `./build/osx-arm64/zapi api -h` | Sub-commands: `request`, `req`, `endpoints` — no `call`, no `registry`, no `[Deprecated]` |
| TASK-027 | `./build/osx-arm64/zapi util -h` | Sub-commands: `timestamp`, `uuid`, `now` — no `time-ms`, no `time-now` |
| TASK-028 | `./build/osx-arm64/zapi trace -h` | Sub-commands: `start`, `list`, `export`, `close`, `reopen`, `remove`, `config` — no `session` |

#### 5c — Confirm old commands are gone

| Task | Command | Expected outcome |
|------|---------|-----------------|
| TASK-059 | `zapi account re-auth --name x` | Exit non-0 with `Unknown command` — **no** deprecation warning |
| TASK-060 | `zapi api call --help` | Exit non-0 — command not found |
| TASK-061 | `zapi api registry list` | Exit non-0 — branch not found |
| TASK-062 | `zapi util time-ms` | Exit non-0 — command not found |
| TASK-063 | `zapi util time-now` | Exit non-0 — command not found |
| TASK-064 | `zapi scope add --account x --scope y` | Exit non-0 — top-level `scope` branch gone |
| TASK-065 | `zapi trace session start --name x` | Exit non-0 — `session` sub-branch gone |

#### 5e — Final binary rebuild

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-066 | Rebuild all platform binaries via `./publish-all.sh` | ✅ | 2026-04-08 |

---

## 3. Files Changed Summary

| File | Change type |
|---|---|
| `src/ZapiCli/Program.cs` | Edit — remove 7 deprecated registrations / branches |
| `src/ZapiCli/Commands/AccountCommands.cs` | Edit — delete `AccountReAuthDeprecatedCommand` |
| `src/ZapiCli/Commands/ApiCommands.cs` | Edit — delete `ApiCallCommand` wrapper |
| `src/ZapiCli/Commands/ApiRegistryCommands.cs` | Edit — delete 5 `ApiRegistry*Command` shims |
| `src/ZapiCli/Commands/UtilCommands.cs` | Edit — delete 2 deprecated time commands |
| `src/ZapiCli/Commands/ScopeCommands.cs` | Edit — delete 2 deprecated scope shims |
| `src/ZapiCli/Commands/TraceCommands.cs` | Edit — delete 6 deprecated session shims |
| `src/ZapiCli/Commands/DeprecationHelper.cs` | **Delete** |
| `tests/ZapiCli.Tests/DeprecationHelperTests.cs` | **Delete** |
| `tests/ZapiCli.Tests/UtilCommandTests.cs` | Edit — update 2 section comments |
| `tests/ZapiCli.Tests/AccountCommandTests.cs` | Edit — update 1 section comment |
| `tests/ZapiCli.Tests/ScopeCommandTests.cs` | Edit — update 2 section comments |
| `tests/ZapiCli.Tests/ApiRegistryTests.cs` | Edit — update file summary comment |

## Deprecated surface being removed

| Old command | New command | Shim class |
|---|---|---|
| `account re-auth` | `account refresh` | `AccountReAuthDeprecatedCommand` in `AccountCommands.cs` |
| `api call` | `api request` / `api req` | `ApiCallCommand` in `ApiCommands.cs` |
| `api registry list` | `api endpoints list` | `ApiRegistryListCommand` in `ApiRegistryCommands.cs` |
| `api registry add` | `api endpoints add` | `ApiRegistryAddCommand` in `ApiRegistryCommands.cs` |
| `api registry update` | `api endpoints update` | `ApiRegistryUpdateCommand` in `ApiRegistryCommands.cs` |
| `api registry show` | `api endpoints show` | `ApiRegistryShowCommand` in `ApiRegistryCommands.cs` |
| `api registry remove` | `api endpoints remove` | `ApiRegistryRemoveCommand` in `ApiRegistryCommands.cs` |
| `util time-ms` | `util timestamp` | `UtilTimeMsDeprecatedCommand` in `UtilCommands.cs` |
| `util time-now` | `util now` | `UtilTimeNowDeprecatedCommand` in `UtilCommands.cs` |
| `scope add` (top-level) | `account scope add` | `ScopeAddDeprecatedCommand` in `ScopeCommands.cs` |
| `scope list` (top-level) | `account scope list` | `ScopeListDeprecatedCommand` in `ScopeCommands.cs` |
| `trace session start` | `trace start` | `DeprecatedSessionStartCommand` in `TraceCommands.cs` |
| `trace session list` | `trace list` | `DeprecatedSessionListCommand` in `TraceCommands.cs` |
| `trace session export` | `trace export` | `DeprecatedSessionExportCommand` in `TraceCommands.cs` |
| `trace session close` | `trace close` | `DeprecatedSessionCloseCommand` in `TraceCommands.cs` |
| `trace session reopen` | `trace reopen` | `DeprecatedSessionReopenCommand` in `TraceCommands.cs` |
| `trace session remove` | `trace remove` | `DeprecatedSessionRemoveCommand` in `TraceCommands.cs` |

---

## Key safety rule

Always remove `Program.cs` registrations (Story 37) **before** deleting the shim class bodies (Story 38). Deleting a class that is still referenced in `Program.cs` will break the build.
