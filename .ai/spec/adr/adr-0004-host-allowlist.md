---
title: "ADR-0004: Compile-Time Host Allowlist for Outbound HTTP Requests"
status: "Accepted"
date: "2026-03-18"
authors: "zapi-cli Core Team"
tags: ["architecture", "decision", "security", "networking", "ssrf"]
supersedes: ""
superseded_by: ""
---

# ADR-0004: Compile-Time Host Allowlist for Outbound HTTP Requests

## Status

**Accepted**

## Context

zapi-cli is a general-purpose HTTP API invoker: the caller supplies the full target URL via `--url` on every `api call` invocation. This design makes zapi-cli inherently vulnerable to Server-Side Request Forgery (SSRF) attacks if the URL is not validated.

Because zapi-cli is designed exclusively for Zoho product REST APIs, all legitimate outgoing requests will always be to Zoho-owned domains. Allowing any other destination provides no value and introduces risk.

## Decision

`ApiClient` validates the resolved URL host against a **sealed compile-time allowlist of Zoho domain suffixes** before every outgoing HTTP request. Any URL whose host does not end with one of the allowed suffixes is rejected with error code `HOST_NOT_ALLOWED` (exit 1) before any HTTP connection is attempted.

**Allowlist (sealed constant in `ZapiCli.Core`):**

| Suffix | Covers |
|---|---|
| `zoho.com` | US Zoho product APIs + Accounts |
| `zoho.eu` | EU Zoho product APIs |
| `zoho.in` | IN Zoho product APIs |
| `zoho.com.au` | AU Zoho product APIs |
| `zohoapis.com` | US Zoho API domain |
| `zohoapis.in` | IN Zoho API domain |

**Error output (stderr):**
```json
{ "error": "Target host is not in the allowed Zoho domain list.", "code": "HOST_NOT_ALLOWED", "exitCode": 1 }
```

The allowlist is not configurable at runtime.

## Consequences

### Positive

- **POS-001**: Eliminates SSRF risk for the entire class of attacks that direct zapi-cli toward internal cloud metadata endpoints, localhost services, or arbitrary internet hosts.
- **POS-002**: The allowlist check is in `ApiClient.CallAsync`, the single outgoing HTTP call site.
- **POS-003**: Compile-time constants cannot be altered by runtime configuration injection or AI-agent instruction.

### Negative

- **NEG-001**: When Zoho introduces a new API domain not yet in the allowlist, calls will fail with `HOST_NOT_ALLOWED` until a new binary is distributed.
- **NEG-002**: Users cannot use zapi-cli to call non-Zoho REST APIs.
- **NEG-003**: The implementation must use proper suffix comparison, not `Contains`.

## Implementation Notes

- **IMP-001**: The host suffix check must use `Uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)` with an exact-boundary guard.
- **IMP-002**: The check occurs after URL parsing but before `IAuthProvider.GetTokenAsync`.
- **IMP-003**: The ZohoCorp domain block (ADR-0003) runs immediately after the host allowlist check.

## References

- **REF-001**: [ADR-0003: ZohoCorp Domain Block](adr-0003-zohocorp-domain-block.md)
- **REF-002**: [ADR-0006: Full URL Required for API Calls](adr-0006-full-url-required.md)
- **REF-003**: [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
