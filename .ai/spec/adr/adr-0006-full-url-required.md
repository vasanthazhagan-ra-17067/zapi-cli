---
title: "ADR-0006: Full URL Required for API Calls — No Product Alias Derivation"
status: "Accepted"
date: "2026-03-18"
authors: "zapi-cli Core Team"
tags: ["architecture", "decision", "api", "cli-design", "ai-agent"]
supersedes: ""
superseded_by: ""
---

# ADR-0006: Full URL Required for API Calls — No Product Alias Derivation

## Status

**Accepted**

## Context

zapi-cli is a general-purpose REST API invoker for **any** Zoho product. An alternative design would have the CLI derive the API base URL from the account's datacenter (`Dc` field) combined with a product alias. However, zapi-cli's primary consumer is AI agents, not humans constructing commands interactively.

- Agents already know (or can look up) the full API endpoint URL from Zoho documentation or an API registry.
- Alias tables create an implicit, opaque mapping that agents cannot inspect or verify.
- Alias tables must be maintained in the binary; adding a new Zoho product API requires a new zapi-cli release even if the underlying HTTP call is unchanged.

## Decision

`--url` is **required on every `api call` invocation**. The full, absolute endpoint URL is supplied directly by the caller. zapi-cli performs no URL derivation, interpolation, or substitution from the account's `Dc` field or any product alias.

The account's `Dc` field is used **only** to resolve the Zoho Accounts URL for OAuth token operations (`https://accounts.zoho.<dc-domain>`). It is not used for API endpoint URL construction.

```
zapi-cli api call --url "https://cliq.zoho.com/api/v2/channels" --method GET
zapi-cli api call --url "https://desk.zoho.com/api/v1/tickets" --method GET
zapi-cli api call --url "https://crm.zoho.com/crm/v5/Leads" --method POST --body '{"data":[...]}'
```

A future **API Registry** (`api registry`, P2) enables users and agents to store named endpoint entries with full URLs.

## Consequences

### Positive

- **POS-001**: AI agents can call any Zoho product API without requiring a new zapi-cli release.
- **POS-002**: The full URL appears verbatim in trace entries, making traces self-describing.
- **POS-003**: Eliminates an entire class of maintenance work: no product alias table, no per-product URL template.
- **POS-004**: Consistent with the host allowlist design (ADR-0004).

### Negative

- **NEG-001**: Humans typing commands manually must supply the full URL, which is verbose.
- **NEG-002**: Agents must have access to accurate Zoho API documentation to construct correct URLs.
- **NEG-003**: There is no client-side validation that the path portion of `--url` is a valid Zoho API path.

## Implementation Notes

- **IMP-001**: `ApiCommands.cs` must declare `--url` as a required option in the Spectre.Console.Cli settings class.
- **IMP-002**: URL parsing (for host extraction and host-allowlist validation) happens inside `ApiClient.CallAsync` via `new Uri(request.Url)`.
- **IMP-003**: `ApiTraceEntry` records both `url` (full URL including query parameters) and `base_url` (scheme + host).

## References

- **REF-001**: [ADR-0004: Compile-Time Host Allowlist](adr-0004-host-allowlist.md)
