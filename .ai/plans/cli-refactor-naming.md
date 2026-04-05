# Plan: CLI Naming & Structure Refactor

## Status: Ready for implementation

## Reference Documents
- **Command Reference**: [`tech_spec/docs/command-reference.md`](../../tech_spec/docs/command-reference.md) — canonical post-refactor command surface; all stories in this plan implement toward that spec.
- **Implementation Plan**: [`plan/refactor-cli-naming-1.md`](../../plan/refactor-cli-naming-1.md) — phase-by-phase breakdown with full task list.

---

## TL;DR

Rename the binary from `zapi-cli` → `zapi` and refactor the command surface to align with industry-standard CLI conventions (CLIG, `gh`, `az`, `fly`). No behaviour changes — only naming, restructuring, and deprecated aliases.

**Scope:** 11 stories (26–36), all non-breaking (deprecated aliases maintained).

---

## Deprecation Infrastructure (Story 27 — implement first)

Before any command renames, a shared deprecation mechanism must exist.

**File:** `src/ZapiCli/Commands/GlobalSettings.cs` — **MODIFY** (or create `DeprecationHelper.cs`)

```csharp
// Proposed helper — called at the top of ExecuteAsync for any deprecated alias:
internal static class DeprecationHelper
{
    public static void WarnIfDeprecated(string? alias, string canonical)
    {
        if (alias is null) return;
        Console.Error.WriteLine($"Deprecation: '{alias}' is deprecated; use '{canonical}' instead.");
    }
}
```

Alternative approach (no helper class):
- Register the deprecated command name as a second `AddCommand<T>` binding under the **old name**.
- In `ExecuteAsync`, detect which argv[0]/argv[1] was used by reading `context.Arguments` or by setting a boolean flag on the Settings class that the deprecated registration sets.
- Print the deprecation warning before delegating to the same execution path.

> **Decision required by story-27**: Pick one of these two approaches and document it. All subsequent stories use the same pattern.

---

## Story Dependency Graph

```
26 (binary rename)
  └── 27 (deprecation infrastructure)
        ├── 28 (api call → api request)
        ├── 29 (trace session flatten)
        ├── 30 (scope → account scope)
        ├── 31 (account use shorthand)
        ├── 32 (account re-auth → refresh)
        ├── 33 (api registry → api endpoints)
        ├── 34 (util time-ms/now renames)
        └── 35 (param renames)
              └── 36 (HELP.md + docs full pass)
```

All stories 28–35 can be implemented in parallel after story-27 lands. Story-36 depends on all of 28–35.

---

## Phase 1: Binary rename (Story 26)

**Files:** `src/ZapiCli/ZapiCli.csproj`, `src/ZapiCli/Program.cs`, `publish-all.sh`, `docs/HELP.md`, `README.md`

Key changes:
1. `<AssemblyName>zapi</AssemblyName>` in `ZapiCli.csproj`
2. `config.SetApplicationName("zapi")` in `Program.cs`
3. All `osx-arm64/zapi-cli` → `osx-arm64/zapi` etc. in `publish-all.sh`
4. Installation instructions in HELP.md and README updated

No deprecated alias needed for binary name — a wrapper script (`zapi-cli` → `exec zapi "$@"`) is documented in CHANGELOG only.

---

## Phase 2: Deprecation infrastructure (Story 27)

**Files:** `src/ZapiCli/Commands/` (new helper or pattern)

Decision: implement as a static `DeprecationHelper.Warn(string oldName, string newName)` that writes to `Console.Error`. Each deprecated command registration sets a `IsDeprecatedInvocation = true` property on the Settings class; `ExecuteAsync` reads this and calls `Warn()` before proceeding.

---

## Phase 3: `api call` → `api request` (Story 28)

**Files:** `src/ZapiCli/Program.cs`, `src/ZapiCli/Commands/ApiCommands.cs`

- Rename command registration: `"call"` → `"request"` with `.WithAlias("req")`
- Re-register `"call"` as deprecated alias (sets `IsDeprecatedInvocation` on settings)
- Rename class `ApiCallCommand` → `ApiRequestCommand`

---

## Phase 4: Flatten `trace session` → `trace` (Story 29)

**Files:** `src/ZapiCli/Program.cs`

- Move all 6 session commands up directly under `trace`
- Re-add `trace.AddBranch("session", ...)` containing the same commands with `IsDeprecatedInvocation = true`
- The `trace config` branch is unchanged

Spectre.Console.Cli caveat to verify: confirm that a branch can have both leaf commands and a sub-branch at the same level (i.e., `trace start` and `trace config set` can coexist under `trace`). This is the same pattern as the existing `config set *` + `config show` under `config`.

---

## Phase 5: `scope` → `account scope` (Story 30)

**Files:** `src/ZapiCli/Program.cs`

- Add `account.AddBranch("scope", ...)` with `ScopeAddCommand` and `ScopeListCommand`
- Keep top-level `scope` branch with `IsDeprecatedInvocation = true` on each command

Spectre.Console.Cli caveat: verify that `account` branch can have both leaf commands (login, list, show, etc.) and a sub-branch (scope). Same as `config set *` pattern — should work.

---

## Phase 6: `account use` shorthand (Story 31)

**Files:** `src/ZapiCli/Program.cs`, `src/ZapiCli/Commands/AccountCommands.cs`

- Add `AccountUseCommand` (or reuse `AccountSetDefaultCommand` with a positional arg overload)
- Settings class: `[CommandArgument(0, "[NAME]")]` for positional + keep `--name` as flag
- Register under `account.AddCommand<AccountUseCommand>("use")`
- `account set-default` remains unchanged (no deprecation — both forms coexist)

---

## Phase 7: `account re-auth` → `account refresh` (Story 32)

**Files:** `src/ZapiCli/Program.cs`, `src/ZapiCli/Commands/AccountCommands.cs`

- Rename class `AccountReAuthCommand` → `AccountRefreshCommand`
- Register as `"refresh"` in Program.cs
- Re-register `"re-auth"` as deprecated alias

---

## Phase 8: `api registry` → `api endpoints` (Story 33)

**Files:** `src/ZapiCli/Program.cs`, `src/ZapiCli/Commands/ApiRegistryCommands.cs` → `ApiEndpointCommands.cs`

- Rename file and all classes (`ApiRegistryListCommand` → `ApiEndpointListCommand`, etc.)
- Rename branch registration `"registry"` → `"endpoints"`
- Re-add `"registry"` branch as deprecated alias
- Rename error codes in `ErrorCodes.cs`: `REGISTRY_ENTRY_*` → `ENDPOINT_*`

---

## Phase 9: `util` renames (Story 34)

**Files:** `src/ZapiCli/Program.cs`, `src/ZapiCli/Commands/UtilCommands.cs`

- `"time-ms"` → `"timestamp"`, deprecated alias `"time-ms"` retained
- `"time-now"` → `"now"`, deprecated alias `"time-now"` retained
- Rename classes `UtilTimeMsCommand` → `UtilTimestampCommand`, `UtilTimeNowCommand` → `UtilNowCommand`

---

## Phase 10: Param renames (Story 35)

**Files:** `src/ZapiCli/Commands/AccountCommands.cs`, `src/ZapiCli/Commands/TraceCommands.cs`

### `--zuidstring` → `--zuid`

Affected Settings classes: `AccountShowSettings`, `AccountSetDefaultSettings`, `AccountRemoveSettings`, `AccountRefreshSettings`, `AccountRenameSettings`

Spectre multi-option syntax: `[CommandOption("--zuid|--zuidstring")]`  
The backing property is renamed from `ZuidString` → `Zuid`.

### `--new-name` → `--to`

Affected: `AccountRenameSettings`  
`[CommandOption("--to|--new-name")]` — backing property `NewName` renamed to `To`.

### `--wait-ms` → `--drain-timeout`

Affected: `TraceCloseSettings`  
`[CommandOption("--drain-timeout|--wait-ms")]` — backing property `WaitMs` renamed to `DrainTimeout`.

---

## Phase 11: HELP.md + full docs pass (Story 36)

**Files:** `docs/HELP.md`, `README.md`

Regenerate HELP.md to use all new command names, new param names, and updated error codes. Cross-check against `tech_spec/docs/command-reference.md`. Run full test suite before marking this story done.
