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
| TASK-001 | Remove `account.AddCommand<AccountCommands.AccountReAuthDeprecatedCommand>("re-auth")` block (~line 148) | | |
| TASK-002 | Remove `api.AddCommand<ApiCommands.ApiCallCommand>("call")` block (~line 167) | | |
| TASK-003 | Remove the entire `api.AddBranch("registry", registry => { ... })` block (5 registry shim registrations) | | |
| TASK-004 | Remove `util.AddCommand<UtilCommands.UtilTimeMsDeprecatedCommand>("time-ms")` entry | | |
| TASK-005 | Remove `util.AddCommand<UtilCommands.UtilTimeNowDeprecatedCommand>("time-now")` entry | | |
| TASK-006 | Remove the entire top-level `config.AddBranch("scope", ...)` block (both `ScopeAddDeprecatedCommand` and `ScopeListDeprecatedCommand`) | | |
| TASK-007 | Remove the `trace.AddBranch("session", session => { ... })` block (6 deprecated session shim registrations) | | |

---

### Phase 2 — Delete deprecated shim classes from command source files

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-008 | In `AccountCommands.cs`: delete `AccountReAuthDeprecatedCommand` class | | |
| TASK-009 | In `ApiCommands.cs`: delete `ApiCallCommand` deprecated wrapper class; remove the `api call` summary comment above it | | |
| TASK-010 | In `ApiRegistryCommands.cs`: delete all 5 `ApiRegistry*Command` shim classes (`ApiRegistryListCommand`, `ApiRegistryAddCommand`, `ApiRegistryUpdateCommand`, `ApiRegistryShowCommand`, `ApiRegistryRemoveCommand`) and the `// ─── deprecated shims` section comment | | |
| TASK-011 | In `UtilCommands.cs`: delete `UtilTimeMsDeprecatedCommand` and `UtilTimeNowDeprecatedCommand` classes | | |
| TASK-012 | In `ScopeCommands.cs`: delete `ScopeAddDeprecatedCommand` and `ScopeListDeprecatedCommand` classes and the `// ─── deprecated shims` section comment | | |
| TASK-013 | In `TraceCommands.cs`: delete all 6 `DeprecatedSession*Command` classes (`DeprecatedSessionStartCommand`, `DeprecatedSessionListCommand`, `DeprecatedSessionExportCommand`, `DeprecatedSessionCloseCommand`, `DeprecatedSessionReopenCommand`, `DeprecatedSessionRemoveCommand`) and the `// ─── Deprecated 'trace session *' shims` section comment | | |

---

### Phase 3 — Delete `DeprecationHelper`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-014 | Verify no remaining `DeprecationHelper.Warn(` calls exist anywhere in `src/` (should be zero after Phase 2) | | |
| TASK-015 | Delete `src/ZapiCli/Commands/DeprecationHelper.cs` | | |
| TASK-016 | Delete `tests/ZapiCli.Tests/DeprecationHelperTests.cs` | | |

---

### Phase 4 — Update test comments

Deprecated-command test sections that used old headings but already test the new command classes must have their section headers updated to match current command names.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-017 | In `UtilCommandTests.cs`: rename section comment `// ── util time-ms` → `// ── util timestamp` (tests already invoke `UtilTimestampCommand`) | | |
| TASK-018 | In `UtilCommandTests.cs`: rename section comment `// ── util time-now` → `// ── util now` (tests already invoke `UtilNowCommand`) | | |
| TASK-019 | In `AccountCommandTests.cs`: rename section comment `// ── account re-auth` → `// ── account refresh` (tests exercise the service's `ReAuthAsync` method, not the deprecated command class) | | |
| TASK-020 | In `ScopeCommandTests.cs`: rename section comments `// ── scope add` → `// ── account scope add` and `// ── scope list` → `// ── account scope list` | | |
| TASK-021 | In `ApiRegistryTests.cs`: update the file-level summary comment from `the <c>api registry</c> command group` to `the <c>api endpoints</c> command group` | | |

---

### Phase 6 — Documentation updates

Remove all references to deprecated commands, flags, and the "Deprecated Commands" section from every documentation file in the repository. These changes ensure that docs only describe the current canonical surface.

| Task | File | Change |
|------|------|--------|
| TASK-067 | `docs/HELP.md` — Table of Contents | Remove `*(was: ...)*` suffixes from all 11 renamed command entries; remove `api call is deprecated` note from `api request` entry | ✅ | 2026-04-08 |
| TASK-068 | `docs/HELP.md` — `account show` / `set-default` / `remove` / `refresh` / `rename` OPTIONS blocks | Remove `*(deprecated alias: --zuidstring)*` note from all `--zuid` lines; remove `*(deprecated alias: --new-name)*` from `--to` | ✅ | 2026-04-08 |
| TASK-069 | `docs/HELP.md` — `account refresh` section | Remove `*(was: account re-auth — deprecated alias still accepted)*` paragraph | ✅ | 2026-04-08 |
| TASK-070 | `docs/HELP.md` — `api request` section | Remove the deprecation-notice blockquote about `api call`; update alias line to `Alias: api req (short form)` | ✅ | 2026-04-08 |
| TASK-071 | `docs/HELP.md` — `api endpoints *` section headers | Remove `*(was: api registry * — deprecated alias still accepted)*` paragraphs from all 5 endpoints sub-commands | ✅ | 2026-04-08 |
| TASK-072 | `docs/HELP.md` — `account scope add/list` section headers | Remove `*(was: scope add/list — deprecated alias still accepted)*` paragraphs | ✅ | 2026-04-08 |
| TASK-073 | `docs/HELP.md` — `util timestamp` / `util now` section headers | Remove `*(was: util time-ms/time-now — deprecated alias still accepted)*` paragraphs | ✅ | 2026-04-08 |
| TASK-074 | `docs/HELP.md` — `trace start/list/export/close/reopen/remove` section headers | Remove `*(was: trace session * — deprecated alias still accepted)*` paragraphs from all 6 trace sub-commands | ✅ | 2026-04-08 |
| TASK-075 | `docs/HELP.md` — `trace close` OPTIONS | Remove `*(deprecated alias: --wait-ms)*` from `--drain-timeout` line | ✅ | 2026-04-08 |
| TASK-076 | `docs/HELP.md` — `## Deprecated Commands` section | Delete the entire section (command names table, flag names table, error codes table, alias table) | ✅ | 2026-04-08 |
| TASK-077 | `tech_spec/docs/command-reference.md` — Deprecation Contract section | Replace with a single-line note: "All previously deprecated aliases have been removed." | ✅ | 2026-04-08 |
| TASK-078 | `tech_spec/docs/command-reference.md` — all `*(was: ...)*` section headings | Strip `*(was: ...)` from all 15 section headings | ✅ | 2026-04-08 |
| TASK-079 | `tech_spec/docs/command-reference.md` — all `Deprecated alias:` / `Deprecated alias branch:` notes | Remove from `account refresh`, `account rename --to`, `account scope add/list`, `api request`, `api endpoints list`, `trace close --drain-timeout` | ✅ | 2026-04-08 |
| TASK-080 | `tech_spec/docs/command-reference.md` — `## Error Code Changes` and `## Full Rename Map` sections | Delete both sections entirely | ✅ | 2026-04-08 |
| TASK-081 | `tech_spec/prd.md` — scattered `api call`, `re-auth`, `scope add/list`, `trace session *`, `time-ms`, `registry`, `zuidstring`, `zapi-cli` references | Replace each with the current canonical command/flag/binary name | ✅ | 2026-04-08 |
| TASK-082 | `docs/api-analysis/api-analysis-plan.md` — steps 1, 2, 9 | Replace `scope add`, `re-auth`, and generic "Start/Close trace session" with `zapi account scope add`, `zapi account refresh`, `zapi trace start/close` | ✅ | 2026-04-08 |

---

### Phase 5 — Validation

#### 5a — Build & unit tests

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-022 | Run `dotnet build src/zapi-cli.sln` — must succeed with 0 errors, 0 warnings about missing types | ✅ | 2026-04-08 |
| TASK-023 | Run `dotnet test src/zapi-cli.sln --logger "console;verbosity=normal"` — all tests must pass, 0 failures | ✅ | 2026-04-08 |

#### 5b — Help output clean-up checks (no `[Deprecated]` entries anywhere)

| Task | Command | Expected |
|------|---------|----------|
| TASK-024 | `./build/osx-arm64/zapi -h` | No `scope` branch listed at the top level |
| TASK-025 | `./build/osx-arm64/zapi account -h` | Commands: `login`, `list`, `show`, `set-default`, `use`, `remove`, `refresh`, `rename`, `scope` — no `re-auth`, no `[Deprecated]` |
| TASK-026 | `./build/osx-arm64/zapi api -h` | Sub-commands: `request`, `req`, `endpoints` — no `call`, no `registry`, no `[Deprecated]` |
| TASK-027 | `./build/osx-arm64/zapi util -h` | Sub-commands: `timestamp`, `uuid`, `now` — no `time-ms`, no `time-now`, no `[Deprecated]` |
| TASK-028 | `./build/osx-arm64/zapi trace -h` | Sub-commands: `start`, `list`, `export`, `close`, `reopen`, `remove`, `config` — no `session`, no `[Deprecated]` |

#### 5c — Functional smoke tests (every canonical command exercised)

Each test must exit 0 (or the expected error code) and produce valid JSON on stdout with no deprecation warning on stderr.

**account branch**

| Task | Command | Expected outcome |
|------|---------|-----------------|
| TASK-030 | `zapi account list` | JSON array (may be empty); exit 0 |
| TASK-031 | `zapi account login --help` | Login usage text; exit 0 |
| TASK-032 | `zapi account show --name <existing>` | JSON object with `name`, `dc`, `email` fields; exit 0 |
| TASK-033 | `zapi account set-default --name <existing>` | JSON `status: ok`; exit 0 |
| TASK-034 | `zapi account use <existing>` | JSON `status: ok`; exit 0 (positional shorthand) |
| TASK-035 | `zapi account refresh --name <existing>` | Token refreshed; exit 0 — **no** deprecation warning |
| TASK-036 | `zapi account rename --name <a> --to <b>` | JSON `status: ok`; exit 0 |
| TASK-037 | `zapi account scope list --account <existing>` | JSON array of scopes; exit 0 |
| TASK-038 | `zapi account scope add --account <existing> --scope ZohoDesk.Tickets.READ` | JSON `status: ok`; exit 0 |

**api branch**

| Task | Command | Expected outcome |
|------|---------|-----------------|
| TASK-039 | `zapi api request --help` | Request usage text; exit 0 |
| TASK-040 | `zapi api req --help` | Same usage text as `api request`; exit 0 |
| TASK-041 | `zapi api endpoints list` | JSON array; exit 0 |
| TASK-042 | `zapi api endpoints add --id smoke-test --method GET --url "https://desk.zoho.com/api/v1/tickets"` | JSON `status: ok`; exit 0 |
| TASK-043 | `zapi api endpoints show --id smoke-test` | JSON object matching added entry; exit 0 |
| TASK-044 | `zapi api endpoints update --id smoke-test --url "https://desk.zoho.com/api/v1/contacts"` | JSON `status: ok`; exit 0 |
| TASK-045 | `zapi api endpoints remove --id smoke-test` | JSON `status: ok`; exit 0 |

**util branch**

| Task | Command | Expected outcome |
|------|---------|-----------------|
| TASK-046 | `zapi util timestamp` | JSON `{ "ts": <positive integer> }`; exit 0 |
| TASK-047 | `zapi util uuid` | JSON containing a valid UUID v4 string; exit 0 |
| TASK-048 | `zapi util now` | JSON containing an IST date-time string; exit 0 |

**trace branch**

| Task | Command | Expected outcome |
|------|---------|-----------------|
| TASK-049 | `zapi trace start --name smoke-session` | JSON `status: ok`; exit 0 |
| TASK-050 | `zapi trace list` | JSON array containing `smoke-session`; exit 0 |
| TASK-051 | `zapi trace close --name smoke-session` | JSON `status: ok`; exit 0 |
| TASK-052 | `zapi trace reopen --name smoke-session` | JSON `status: ok`; exit 0 |
| TASK-053 | `zapi trace export --name smoke-session` | JSON export output; exit 0 |
| TASK-054 | `zapi trace remove --name smoke-session` | JSON `status: ok`; exit 0 |
| TASK-055 | `zapi trace config show` | JSON config object; exit 0 |
| TASK-056 | `zapi trace config set --export-path /tmp/trace-smoke` | JSON `status: ok`; exit 0 |

**config branch**

| Task | Command | Expected outcome |
|------|---------|-----------------|
| TASK-057 | `zapi config show` | JSON object with all current config keys; exit 0 |
| TASK-058 | `zapi config set env-file --help` | Usage text; exit 0 |

#### 5d — Confirm old commands are gone (must error, not deprecation-warn)

| Task | Command | Expected outcome |
|------|---------|-----------------|
| TASK-059 | `zapi account re-auth --name x` | Exit non-0 with `Unknown command` or similar error — **no** deprecation warning |
| TASK-060 | `zapi api call --help` | Exit non-0 — command not found |
| TASK-061 | `zapi api registry list` | Exit non-0 — branch not found |
| TASK-062 | `zapi util time-ms` | Exit non-0 — command not found |
| TASK-063 | `zapi util time-now` | Exit non-0 — command not found |
| TASK-064 | `zapi scope add --account x --scope y` | Exit non-0 — top-level `scope` branch gone |
| TASK-065 | `zapi trace session start --name x` | Exit non-0 — `session` sub-branch gone |

#### 5e — Final binary rebuild

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-066 | Rebuild all platform binaries via `./publish-all.sh` and confirm no build errors | ✅ | 2026-04-08 |

---

## 3. Files changed summary

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
