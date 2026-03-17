---
title: "ADR-0007: Always-On Trace Session Design"
status: "Proposed"
date: "2026-03-17"
authors: "zapi-cli Engineering"
tags: ["architecture", "decision", "tracing", "observability", "agent-workflow"]
supersedes: ""
superseded_by: ""
---

## Status

**Proposed**

## Context

zapi-cli is primarily consumed by AI agents executing multi-step API exploration tasks. A typical agent session involves:

1. Starting an analysis run (e.g., "map the Zoho Desk ticket lifecycle").
2. Firing multiple `api call` commands across different endpoints.
3. Optionally draining Pex events between calls.
4. Exporting a complete record of all calls and responses for developer review.

The trace system must answer the question: **what did the agent call, in what order, with what responses?**

Key design tensions:

- **Opt-in tracing (per-call `--trace` flag)**: Agents could forget to pass `--trace` on any individual call, producing an incomplete trace. Debugging an incomplete trace is harder than debugging the absence of a trace.
- **Implicit sessions (auto-create on first call)**: Eliminates developer control over session naming and boundaries; multiple unrelated runs would be merged into one session.
- **Explicit opt-in sessions (`trace session start` required)**: Requires conscious session management, but gives developers full control over naming and session boundaries.
- **No tracing**: Loses observability entirely.

The primary consumer (AI agents) has a clear workflow: start a named session, execute calls, export. The trace system should match this workflow without adding per-call overhead or creating foot-guns (forgotten `--trace` flags).

Additionally:
- Trace entries must **never include `Authorization` header values** — tokens must not appear in any on-disk artifact.
- Export must be non-destructive (safe to call multiple times).
- Sessions must be identified by developer-readable names, not UUIDs, for easy reference in downstream analysis.

## Decision

**Adopt an always-on, session-scoped trace model:**
- Tracing is **automatically active** when a named session is open — no per-call flag required.
- When no session is active, trace entries are **silently dropped** — no implicit session is created.
- Sessions are named explicitly via `trace session start --name <name>`, giving developers full control over session boundaries and labels.
- Export is **non-destructive** — `trace session export` writes to stdout and does not modify the `.jsonl` file.

**Storage layout:**
```
<configDir>/zapi-cli/traces/
  sessions.json               ← index: name, startTime, entryCount, status
  <session-name>/
    trace.jsonl               ← append-only, one JSON object per line
```

**`ApiTraceEntry` JSON shape (excerpt):**
```json
{
  "seq": 1,
  "type": "api",
  "session": "Desk-Tickets-2026-03-17",
  "timestamp": "2026-03-17T10:23:45.123Z",
  "duration_ms": 342,
  "account": "work",
  "method": "GET",
  "base_url": "https://desk.zoho.com/api/v1",
  "url": "https://desk.zoho.com/api/v1/tickets",
  "request_headers": { "Content-Type": "application/json" },
  "request_body": null,
  "response_status": 200,
  "response_headers": {},
  "response_body": "{\"data\":[...]}",
  "error": null
}
```

`Authorization` header is **always excluded** from `request_headers`.

**Agent workflow integration:**
```
zapi-cli trace session start --name "Desk-Tickets-2026-03-17"
# ... agent fires api calls — entries auto-appended ...
zapi-cli trace session export --name "Desk-Tickets-2026-03-17"   # → stdout
zapi-cli trace session close  --name "Desk-Tickets-2026-03-17"
```

## Consequences

### Positive

- **POS-001**: Eliminates per-call `--trace` flags — no call is accidentally omitted from the session trace. The trace is guaranteed complete for the session's lifetime.
- **POS-002**: Named sessions give developers and agents meaningful, human-readable identifiers for cross-referencing traces with feature work (e.g., `"Desk-Tickets-2026-03-17"`).
- **POS-003**: Append-only JSONL storage is crash-safe — a partial session caused by agent interruption still contains all entries written up to the interruption point.
- **POS-004**: Non-destructive export means the trace can be exported multiple times (e.g., mid-session for partial review) without risk of data loss.
- **POS-005**: `seq` monotonically increasing across entries enables reliable ordering and cross-referencing between `api` and `pex` entry types.

### Negative

- **NEG-001**: Developers who run `api call` without starting a session first will have no trace — the silent drop behaviour may be surprising. A warning to stderr when no session is active is advisable.
- **NEG-002**: Long-running sessions with many API calls accumulate large `.jsonl` files. No automatic rotation or size limit is defined in v1.
- **NEG-003**: Session names are not sanitized for filesystem safety — a session name containing `/`, `..`, or other path-traversal characters could be a security issue if not validated.
- **NEG-004**: The `--product <name>` filter on `trace session export` depends on API Registry, which is deferred to v2 — this export option cannot be used until the registry is implemented.

## Alternatives Considered

##### Per-call `--trace <session-name>` flag

- **ALT-001**: **Description**: Require an explicit `--trace "session-name"` flag on each `api call` to write the entry into a session.
- **ALT-002**: **Rejection Reason**: Agents can and do forget flags. A single forgotten `--trace` produces a gap in the trace that is invisible unless the developer notices the missing `seq` number. Always-on is safer for the agent use case.

##### Automatic implicit session creation

- **ALT-003**: **Description**: Create a new session automatically on the first `api call` with an auto-generated name (timestamp + UUID).
- **ALT-004**: **Rejection Reason**: Auto-generated names are not human-readable. Multiple unrelated analysis runs in the same session pollute the trace. Developers lose the ability to demarcate session boundaries for clean exports.

##### In-memory trace only (no disk persistence)

- **ALT-005**: **Description**: Keep trace entries in memory for the duration of the process; export via a summary command at the end.
- **ALT-006**: **Rejection Reason**: AI agents run `api call` as separate process invocations — there is no shared in-memory state across invocations. Disk persistence is the only viable approach for multi-invocation sessions.

##### Single global trace log (no named sessions)

- **ALT-007**: **Description**: Append all API calls to a single rotating log file; provide filtering on export.
- **ALT-008**: **Rejection Reason**: A global log mixes entries from unrelated analysis runs with no natural boundaries. Named sessions map cleanly to the agent workflow unit of work and allow precise, scoped exports.

## Implementation Notes

- **IMP-001**: `TraceSession` loads `sessions.json` on every write to detect the currently active session. If multiple sessions are `"active"`, `TraceWriter` selects the most recently started one and emits a warning to stderr.
- **IMP-002**: Session names must be validated against the regex `^[a-zA-Z0-9_\-]{1,64}$` before creating the session directory — reject path-traversal characters to prevent filesystem injection.
- **IMP-003**: `TraceWriter.AppendAsync` uses `FileMode.Append` + `FileShare.Read` — multiple concurrent `api call` invocations writing to the same session will interleave JSONL lines. For v1 (sequential agent calls), this is acceptable; concurrent writes are a v2 concern.
- **IMP-004**: `trace session export --truncate-body <bytes>` truncates `request_body` and `response_body` fields at export time only — the full content is preserved in the `.jsonl` file for subsequent exports.
- **IMP-005**: Success metric: `trace session export` on a 1000-entry session completes in under 500 ms without reading the entire file into memory (streaming JSON array output).

## References

- **REF-001**: [tech_spec.md — Section 10: Trace Sessions](../tech_spec.md#10-trace-sessions)
- **REF-002**: [tech_spec.md — Section 5: Data Models — ApiTraceEntry, PexTraceEntry](../tech_spec.md#5-data-models)
- **REF-003**: [tech_spec.md — Section 4: Group `trace`](../tech_spec.md#group-trace)
- **REF-004**: [ADR-0003: Caller-Supplied Base URL Per API Invocation](./adr-0003-caller-supplied-base-url.md)
