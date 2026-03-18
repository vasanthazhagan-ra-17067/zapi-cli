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

zapi-cli is a general-purpose REST API invoker for **any** Zoho product. An alternative design would have the CLI derive the API base URL from the account's datacenter (`Dc` field) combined with a product alias (e.g. `--product cliq` → `https://cliq.zoho.com/api/v2`). Many product-centric CLIs follow this pattern.

However, zapi-cli's primary consumer is AI agents, not humans constructing commands interactively. This changes the tradeoff calculus:

- Agents already know (or can look up) the full API endpoint URL from Zoho documentation or an API registry.
- Alias tables create an implicit, opaque mapping that agents cannot inspect or verify — they would have to trust that `--product desk` maps correctly to `https://desk.zoho.com/api/v1`.
- Alias tables must be maintained in the binary; adding a new Zoho product API requires a new zapi-cli release even if the underlying HTTP call is unchanged.
- Some Zoho products use `zoho.com` base URLs while others use `zohoapis.com`; and the URL structure varies per product (some include `/api/v1`, others `/crm/v5`). A single alias-to-URL mapping would need extensive per-product special-casing.

## Decision

`--url` is **required on every `api call` invocation**. The full, absolute endpoint URL is supplied directly by the caller. zapi-cli performs no URL derivation, interpolation, or substitution from the account's `Dc` field or any product alias.

The account's `Dc` field is used **only** to resolve the Zoho Accounts URL for OAuth token operations (`https://accounts.zoho.<dc-domain>`). It is not used for API endpoint URL construction.

```
zapi-cli api call --url "https://cliq.zoho.com/api/v2/channels" --method GET
zapi-cli api call --url "https://desk.zoho.com/api/v1/tickets" --method GET
zapi-cli api call --url "https://crm.zoho.com/crm/v5/Leads" --method POST --body '{"data":[...]}'
```

A future **API Registry** (`api registry`, P2) enables users and agents to store named endpoint entries with full URLs, providing a lookup shorthand without hiding the URL structure from the tool.

## Consequences

### Positive

- **POS-001**: AI agents can call any Zoho product API without requiring a new zapi-cli release — the tool is extensible at the agent level, not the binary level.
- **POS-002**: The full URL appears verbatim in trace entries (`ApiTraceEntry.Url`), making traces self-describing and human-readable without requiring a reverse alias lookup.
- **POS-003**: Eliminates an entire class of maintenance work: no product alias table, no per-product URL template, no versioning logic for endpoint path changes.
- **POS-004**: Consistent with the host allowlist design (ADR-0004) — URL validation operates on the explicit URL, not a derived one, making security reasoning straightforward.

### Negative

- **NEG-001**: Humans typing commands manually must supply the full URL, which is verbose compared to `--product cliq` shorthands. This is an accepted tradeoff given the primary agent-centric use case.
- **NEG-002**: Agents must have access to accurate Zoho API documentation (or the API Registry) to construct correct URLs. An agent with stale URL knowledge may construct incorrect endpoints.
- **NEG-003**: There is no client-side validation that the path portion of `--url` is a valid Zoho API path — only the host is checked. Invalid paths are discovered only at the server response level (4xx).

## Alternatives Considered

##### Product Alias + Dc-Derived Base URL (`--product cliq --path /api/v2/channels`)

- **ALT-001**: **Description**: The CLI maintains an alias-to-base-URL table (e.g., `cliq` → `https://cliq.zoho.<dc-domain>`). The caller provides `--path`; the base URL is derived from the alias and active account's datacenter.
- **ALT-002**: **Rejection Reason**: Datacenter-to-URL mappings are not uniform across Zoho products. Some products always use `zoho.com` regardless of datacenter; others vary. Maintaining this table in the binary is error-prone and requires a new binary release for each new product or URL change.

##### Semi-Structured URL (`--host cliq.zoho.com --path /api/v2/channels`)

- **ALT-003**: **Description**: Split the URL into host and path flags; the tool validates the host against the allowlist and assembles the full URL.
- **ALT-004**: **Rejection Reason**: Splitting a single conceptual value (the URL) into two flags adds complexity without meaningful benefit. Agents still need to know both parts. Full-URL is simpler and matches standard HTTP tooling conventions (curl, httpie).

##### Product Registry Built Into Binary (Hardcoded API Registry)

- **ALT-005**: **Description**: Bundle a registry of known Zoho product endpoints in the binary, allowing `zapi-cli api call --id cliq-list-channels`.
- **ALT-006**: **Rejection Reason**: A hardcoded registry becomes stale as Zoho's APIs evolve. The external API Registry (P2) addresses the same shorthand need while keeping the catalog maintainable outside the binary.

## Implementation Notes

- **IMP-001**: `ApiCommands.cs` must declare `--url` as a required option in the Spectre.Console.Cli settings class. The framework will reject invocations without it and emit usage help automatically.
- **IMP-002**: `ApiRequest.Url` receives the raw string from `--url`. URL parsing (for host extraction and host-allowlist validation) happens inside `ApiClient.CallAsync` via `new Uri(request.Url)`.
- **IMP-003**: `ApiTraceEntry` records both `url` (full assembled URL including query parameters) and `base_url` (scheme + host, extracted from the full URL) to enable product-level filtering in `trace session export --product`.
- **IMP-004**: The P2 API Registry (`api registry add --id <id> --url <full-url>`) stores full URLs per entry — consistent with this decision. The `api call --id <id>` shorthand (P2) resolves the registry entry's `url` and passes it directly to `ApiClient`, preserving the full-URL-only contract at the HTTP layer.

## References

- **REF-001**: [tech_spec/tech_spec.md — Section 4: CLI Structure & Commands — api call](../tech_spec/tech_spec.md#group-api)
- **REF-002**: [tech_spec/tech_spec.md — Section 9: API Client](../tech_spec/tech_spec.md#9-api-client)
- **REF-003**: [ADR-0004: Compile-Time Host Allowlist](adr-0004-host-allowlist.md)
