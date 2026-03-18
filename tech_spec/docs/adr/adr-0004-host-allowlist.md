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

zapi-cli is a general-purpose HTTP API invoker: the caller supplies the full target URL via `--url` on every `api call` invocation. Unlike a product-specific CLI, zapi-cli does not derive URLs from internal configuration — it sends whatever URL it is given.

This design makes zapi-cli inherently vulnerable to Server-Side Request Forgery (SSRF) attacks if the URL is not validated:

- An AI agent whose prompt is compromised could be instructed to call `--url "http://169.254.169.254/latest/meta-data/"` (cloud metadata endpoint), `--url "http://localhost:6443/"` (local Kubernetes API), or any arbitrary internal network resource.
- A malicious or misconfigured instruction could direct the tool to exfiltrate data from internal services that are accessible from the host running zapi-cli.

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
| `zohoapis.com` | US Zoho API domain (used by some internal endpoints) |
| `zohoapis.in` | IN Zoho API domain |

**Error output (stderr):**
```json
{ "error": "Target host is not in the allowed Zoho domain list.", "code": "HOST_NOT_ALLOWED", "exitCode": 1 }
```

The allowlist is not configurable at runtime. No flag, environment variable, or configurable extension point bypasses it.

## Consequences

### Positive

- **POS-001**: Eliminates SSRF risk for the entire class of attacks that direct zapi-cli toward internal cloud metadata endpoints, localhost services, or arbitrary internet hosts — protecting both the user's machine and network.
- **POS-002**: The allowlist check is in `ApiClient.CallAsync`, the single outgoing HTTP call site — there is no other code path that can dispatch an external request, so the protection is universal.
- **POS-003**: Compile-time constants cannot be altered by runtime configuration injection, environment variable manipulation, or AI-agent instruction — the attack surface is structurally closed.
- **POS-004**: The suffix check correctly handles all Zoho datacenter TLDs in the allowlist without requiring per-product URL mapping, matching the "full URL required" design philosophy (ADR-0006).

### Negative

- **NEG-001**: When Zoho introduces a new API domain (e.g., `zoho.jp.com` or `zohoapis.eu`) not yet in the allowlist, calls to those endpoints will fail with `HOST_NOT_ALLOWED` until a new zapi-cli binary is distributed.
- **NEG-002**: Users cannot use zapi-cli to call non-Zoho REST APIs (e.g., for testing or inspection purposes), even if they explicitly intend to — this is a deliberate restriction, but may surprise users expecting a general HTTP client.
- **NEG-003**: The host check uses suffix matching; an adversarial domain like `evilzoho.com` would not match, but `zoho.com.evil.io` also would not match (it does not *end* with `zoho.com`). The implementation must use a proper suffix comparison, not `Contains`.

## Alternatives Considered

##### Runtime-Configurable Allowlist

- **ALT-001**: **Description**: Store the domain suffix list in `accounts.json` or a separate config file; allow users/admins to add entries.
- **ALT-002**: **Rejection Reason**: A configurable allowlist can be modified by an AI agent instructed to add a malicious domain before making a request. The security property requires the list to be immutable at runtime.

##### Blocklist of Known-Bad Hosts (e.g., RFC 1918, metadata endpoints)

- **ALT-003**: **Description**: Block known-dangerous destinations (169.254.x.x, 10.x.x.x, 192.168.x.x, localhost, etc.) while allowing all other hosts.
- **ALT-004**: **Rejection Reason**: Blocklists are inherently incomplete. New internal endpoint ranges, cloud provider metadata routes, and local network services cannot all be enumerated. An allowlist of known-good Zoho domains is a far smaller and more manageable trust surface.

##### No Validation (Trust Caller)

- **ALT-005**: **Description**: Accept any URL supplied via `--url` without host validation, matching the behavior of curl or wget.
- **ALT-006**: **Rejection Reason**: zapi-cli's primary caller is AI agents, not humans inspecting each invocation. An agent that trusts tool output completely cannot distinguish a legitimate Zoho response from data exfiltrated from an internal endpoint. The risk is unacceptable for a tool positioned in agentic workflows.

## Implementation Notes

- **IMP-001**: The host suffix check must use `Uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)` with an exact-boundary guard (e.g., also check that the character before the suffix is `.` or the host equals the suffix exactly) to prevent subdomain spoofing like `evilzoho.com`.
- **IMP-002**: The check occurs after URL parsing but before `IAuthProvider.GetTokenAsync` — no credentials are read from the keychain for disallowed hosts.
- **IMP-003**: The ZohoCorp domain block (ADR-0003) runs immediately after the host allowlist check; the ordering ensures both security layers are applied on every outgoing request.
- **IMP-004**: Unit tests must cover: a valid `zoho.com` URL, a valid `zohoapis.com` URL, `http://localhost/test` (blocked), `http://169.254.169.254/` (blocked), and a near-miss like `https://notzoho.com/api` (blocked).

## References

- **REF-001**: [tech_spec/tech_spec.md — Section 9: API Client](../tech_spec/tech_spec.md#9-api-client)
- **REF-002**: [ADR-0003: ZohoCorp Domain Block](adr-0003-zohocorp-domain-block.md)
- **REF-003**: [ADR-0006: Full URL Required for API Calls](adr-0006-full-url-required.md)
- **REF-004**: [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
