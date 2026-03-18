---
title: "ADR-0008: stdout/stderr Output Contract — Raw Passthrough with Structured Error Envelope"
status: "Accepted"
date: "2026-03-18"
authors: "zapi-cli Core Team"
tags: ["architecture", "decision", "output", "cli-design", "ai-agent"]
supersedes: ""
superseded_by: ""
---

# ADR-0008: stdout/stderr Output Contract — Raw Passthrough with Structured Error Envelope

## Status

**Accepted**

## Context

zapi-cli's primary consumer is AI agents operating via GitHub Copilot CLI Skills. The output format of every command directly determines how easily an agent can parse and act on the response.

Two core design questions arise:

1. **Should stdout responses be wrapped in a standard envelope** (e.g., `{"status": "ok", "data": {...}}`)? Or should they pass through raw data?
2. **Where do errors go?** If both success responses and error messages go to stdout, an agent must inspect every response to detect failure. If errors go to stderr, stdout can be treated as clean API data.

The agent use case favours a **strict channel separation**: stdout is always structured data (or raw API passthrough), stderr is always the error channel. This allows agents to:
- Read stdout unconditionally as the command result.
- Check exit code + stderr only on failure.
- Avoid parsing an outer wrapper on every response.

## Decision

**stdout:** Every command writes its result as plain JSON directly to stdout with **no wrapper envelope**:

- `api call`: Raw Zoho API response body, byte-for-byte as received, regardless of HTTP status code.
- `account list/show/set-default`: Plain JSON array or object.
- `scope list`: Plain JSON array.
- `util time-ms` / `util uuid`: Plain JSON object.
- `trace` commands: Plain JSON result of the operation.

On HTTP 4xx/5xx from `api call`, the Zoho response body is still written to stdout as-is (it often contains structured error detail useful to the agent). The failure is additionally signalled on stderr.

**stderr:** All pre-call failures and internal errors are written as a JSON error envelope:

```json
{ "error": "<human-readable message>", "code": "<ERROR_CODE>", "exitCode": <0|1|2> }
```

This covers: host not allowed, ZohoCorp block, auth failures, missing args, file I/O errors, keychain errors, and all errors that occur before an HTTP request is dispatched.

For HTTP errors from `api call`:
```
stdout: {"code":"CHANNEL_NOT_FOUND","message":"Channel not found"}
stderr: {"error":"API returned 404 Not Found","code":"API_ERROR","httpStatus":404,"exitCode":1}
```

**Exit codes:**

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | General / recoverable error |
| `2` | Auth failure / needs-reauth |

**Token safety:** `account show` displays `"token": "***"`. No command ever includes a raw token value in any output stream.

All output goes through a central `IOutputWriter` interface to allow test capture without console side effects.

## Consequences

### Positive

- **POS-001**: Agents can consume stdout directly as the API result without stripping an outer wrapper; the data structure mirrors what Zoho's own API returns.
- **POS-002**: Strict channel separation (stdout = data, stderr = errors) enables shell pipelines like `zapi-cli api call ... | jq .channels` to work correctly without filtering noise.
- **POS-003**: The structured stderr error envelope (with machine-readable `code` and `exitCode` fields) allows agents to programmatically detect specific failure modes (e.g., `AUTH_FAILURE` → trigger re-auth) rather than parsing human-readable error strings.
- **POS-004**: Raw passthrough on `api call` means the agent receives the exact Zoho response including any product-specific error metadata in the response body, even on HTTP 4xx — this is often more actionable than a generic error message.
- **POS-005**: `IOutputWriter` abstraction allows unit tests to assert on exact stdout/stderr content without redirecting real console streams.

### Negative

- **NEG-001**: The absence of a standard envelope means the agent must know which commands return objects vs. arrays vs. raw strings — there is no uniform top-level discriminator.
- **NEG-002**: Writing the Zoho response body to stdout on 4xx/5xx creates an asymmetry: stdout contains data on success but also on failure. Agents must check exit code or stderr to determine success, not stdout alone.
- **NEG-003**: Capturing both stdout and stderr separately requires process management that some simple agent implementations may not handle; agents that only capture stdout will miss error details.

## Alternatives Considered

##### Uniform Success Envelope (`{"status":"ok","data":{...}}`)

- **ALT-001**: **Description**: Every command wraps its output in a consistent `{"status":"ok","data":...}` envelope. Errors use `{"status":"error","code":"...","message":"..."}`. Both go to stdout.
- **ALT-002**: **Rejection Reason**: Wrapping `api call` output in an envelope breaks the raw-passthrough principle — the agent would need to unwrap every API response. The envelope also prevents direct piping of API responses to `jq` without an intermediate `| jq .data`. For AI agent consumption, the extra wrapper adds parsing work for no gain when the exit code already signals success vs. failure.

##### All Output to stdout, Structured Discriminator

- **ALT-003**: **Description**: Both data and errors go to stdout as `{"type":"data","payload":{...}}` or `{"type":"error","code":"..."}`.
- **ALT-004**: **Rejection Reason**: Breaks shell composability entirely. Standard Unix tools (`jq`, `grep`, `tail`) processing stdout would receive a mix of data and error records. The stderr convention is the Unix-standard approach for this separation.

##### Human-Readable Error Messages on stderr (No JSON)

- **ALT-005**: **Description**: Errors are written as plain text to stderr (e.g., `Error: Account not found (ACCOUNT_NOT_FOUND)`), not as JSON.
- **ALT-006**: **Rejection Reason**: AI agents parsing stderr for error codes must use fragile regex against human-readable strings. Structured JSON on stderr allows agents to reliably extract `code` and `exitCode` with a simple JSON parse.

## Implementation Notes

- **IMP-001**: `IOutputWriter` must expose `WriteDataAsync(string json)` for stdout and `WriteErrorAsync(string json)` for stderr; implementations write to `Console.Out` and `Console.Error` respectively.
- **IMP-002**: `ApiClient.CallAsync` must write the raw Zoho response body using `WriteDataAsync` regardless of HTTP status code. The calling command is responsible for interpreting the status and writing the `API_ERROR` envelope to stderr.
- **IMP-003**: No command may write token values, client secrets, or `Authorization` header contents to either stream at any log level or verbosity setting.
- **IMP-004**: The `--json` global flag (default `true`) is reserved for future non-JSON output modes. In v1 all output is always JSON; the flag is accepted but has no effect.

## References

- **REF-001**: [tech_spec/tech_spec.md — Section 11: Output Contract](../tech_spec/tech_spec.md#11-output-contract)
- **REF-002**: [tech_spec/tech_spec.md — Section 12: Error Handling](../tech_spec/tech_spec.md#12-error-handling)
- **REF-003**: [ADR-0003: ZohoCorp Domain Block](adr-0003-zohocorp-domain-block.md)
- **REF-004**: [ADR-0004: Host Allowlist](adr-0004-host-allowlist.md)
