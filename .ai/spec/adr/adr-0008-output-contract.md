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

On HTTP 4xx/5xx from `api call`, the Zoho response body is still written to stdout as-is. The failure is additionally signalled on stderr.

**stderr:** All pre-call failures and internal errors are written as a JSON error envelope:

```json
{ "error": "<human-readable message>", "code": "<ERROR_CODE>", "exitCode": <0|1|2> }
```

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

- **POS-001**: Agents can consume stdout directly as the API result without stripping an outer wrapper.
- **POS-002**: Strict channel separation enables shell pipelines like `zapi-cli api call ... | jq .channels`.
- **POS-003**: The structured stderr error envelope allows agents to programmatically detect specific failure modes.
- **POS-004**: Raw passthrough on `api call` means the agent receives the exact Zoho response including any product-specific error metadata.
- **POS-005**: `IOutputWriter` abstraction allows unit tests to assert on exact stdout/stderr content.

### Negative

- **NEG-001**: The absence of a standard envelope means the agent must know which commands return objects vs. arrays vs. raw strings.
- **NEG-002**: Writing the Zoho response body to stdout on 4xx/5xx creates an asymmetry.
- **NEG-003**: Capturing both stdout and stderr separately requires process management that some simple agent implementations may not handle.

## Implementation Notes

- **IMP-001**: `IOutputWriter` must expose `WriteDataAsync(string json)` for stdout and `WriteErrorAsync(string json)` for stderr.
- **IMP-002**: `ApiClient.CallAsync` must write the raw Zoho response body using `WriteDataAsync` regardless of HTTP status code.
- **IMP-003**: No command may write token values, client secrets, or `Authorization` header contents to either stream.
- **IMP-004**: The `--json` global flag (default `true`) is reserved for future non-JSON output modes.

## References

- **REF-001**: [ADR-0003: ZohoCorp Domain Block](adr-0003-zohocorp-domain-block.md)
- **REF-002**: [ADR-0004: Host Allowlist](adr-0004-host-allowlist.md)
