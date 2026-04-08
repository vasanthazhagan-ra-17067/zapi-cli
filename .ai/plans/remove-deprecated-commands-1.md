# Plan: Remove Deprecated Commands

## Status: Ready for implementation

## Reference Documents
- **Implementation Plan**: [`plan/remove-deprecated-commands-1.md`](../../plan/remove-deprecated-commands-1.md) — full phase-by-phase task list, including documentation tasks already completed.
- **Command Reference**: [`tech_spec/docs/command-reference.md`](../../tech_spec/docs/command-reference.md) — canonical current command surface.

---

## TL;DR

Remove every deprecated command alias, shim class, and the `DeprecationHelper` infrastructure introduced in stories 27–34. The migration window has passed. Running `zapi --help` (or any sub-command help) must show zero `[Deprecated]` entries.

**Scope:** 5 stories (37–41). Documentation already cleaned up in story 36 + subsequent doc pass (TASK-067 to TASK-082 in the plan, all ✅).

---

## Story Dependency Graph

```
36 (HELP.md regen — last completed story)
  └── 37 (remove deprecated registrations from Program.cs)
        └── 38 (delete deprecated shim classes from command files)
              └── 39 (delete DeprecationHelper.cs + DeprecationHelperTests.cs)
                    └── 40 (update test section comments)
                          └── 41 (build, test, and publish validation)
```

---

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
