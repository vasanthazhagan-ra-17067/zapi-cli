---
title: "ADR-0006: Host Allowlist as Compile-Time Security Control"
status: "Proposed"
date: "2026-03-17"
authors: "zapi-cli Engineering"
tags: ["architecture", "decision", "security", "ssrf", "api-client"]
supersedes: ""
superseded_by: ""
---

## Status

**Proposed**

## Context

zapi-cli accepts `--base-url` as a caller-supplied URL fragment that is used to assemble outgoing HTTPS requests. The primary consumer is AI agents. Without a restriction layer, a compromised or misdirected agent could supply an arbitrary URL, causing zapi-cli to:

- Make outbound requests to non-Zoho services with the user's Zoho `Authorization` token injected in the request headers.
- Act as an SSRF (Server-Side Request Forgery) vector, reaching internal network hosts if the CLI runs in a network environment with internal DNS.
- Exfiltrate user credentials to attacker-controlled infrastructure.

The `--base-url` flag is designed exclusively for Zoho product API endpoints. zapi-cli has no legitimate use case for sending Zoho authentication tokens to any host outside the Zoho domain family.

The control must be:
1. **Pre-request** — checked before any HTTP connection is established or any token is injected.
2. **Compile-time constant** — not configurable or overridable at runtime via flags, env vars, or config.
3. **Suffix-based** — covering all subdomains and paths under each allowed root domain.

## Decision

**Enforce a compile-time host allowlist in `ApiClient` before every outgoing HTTP request.** Any URL whose host does not end with an allowed suffix is rejected with `HOST_NOT_ALLOWED` (exit 1) before any network activity occurs.

**Allowlist:**

| Suffix | Coverage |
|--------|---------|
| `.zoho.com` | US Zoho product APIs and Accounts |
| `.zoho.eu` | EU Zoho product APIs |
| `.zoho.in` | IN Zoho product APIs |
| `.zoho.com.au` | AU Zoho product APIs |
| `.zohoapis.com` | US Zoho internal API domain |
| `.zohoapis.in` | IN Zoho internal API domain |

The check uses suffix matching on the resolved `Uri.Host`:
```csharp
private static readonly string[] AllowedHostSuffixes =
[
    ".zoho.com", ".zoho.eu", ".zoho.in", ".zoho.com.au",
    ".zohoapis.com", ".zohoapis.in"
];

private static bool IsHostAllowed(Uri uri) =>
    AllowedHostSuffixes.Any(suffix =>
        uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
```

The allowlist is a `static readonly` sealed array in `ZapiCli.Core` — not user-configurable.

**Error output (stderr):**
```json
{
  "error": "The target host is not in the list of allowed Zoho API hosts.",
  "code": "HOST_NOT_ALLOWED",
  "exitCode": 1
}
```

## Consequences

### Positive

- **POS-001**: Eliminates the SSRF risk surface for agent-driven invocations — a compromised agent cannot redirect the CLI's outbound requests outside the Zoho domain family.
- **POS-002**: Prevents accidental injection of the user's Zoho `Authorization` token into non-Zoho endpoints, which would be a credential leak.
- **POS-003**: The compile-time constant approach means the allowlist cannot be weakened without a source code change and rebuild — no runtime misconfiguration is possible.
- **POS-004**: Suffix matching covers all current and future subdomains under each allowed root automatically (e.g., new products added under `desk.zoho.in` are covered without updating the allowlist).

### Negative

- **NEG-001**: New Zoho datacenter domains (e.g., a future `zoho.ca` or `zoho.jp`) require a source code update and new binary release before they can be targeted — there is no runtime extension mechanism.
- **NEG-002**: The allowlist is a static compile-time constant, making it impossible for enterprise users with Zoho on-premise or private cloud deployments (if such deployments exist) to add custom hostnames.
- **NEG-003**: Suffix matching does not validate that `--base-url` values are structurally valid URLs — a malformed URL raises an exception rather than producing a clean `HOST_NOT_ALLOWED` error if not handled separately.

## Alternatives Considered

##### No host restriction (accept any URL)

- **ALT-001**: **Description**: Trust the caller to supply only valid Zoho URLs; document that non-Zoho URLs are not supported.
- **ALT-002**: **Rejection Reason**: Documentation-only controls are not effective for AI agents. The risk of credential exfiltration via a misdirected `--base-url` is concrete and the mitigation is low-cost.

##### Runtime-configurable allowlist

- **ALT-003**: **Description**: Allow the user to add custom host suffixes via a config file or `--allow-host` flag.
- **ALT-004**: **Rejection Reason**: Providing a user-controlled escape hatch undermines the security property entirely. An agent or a CI pipeline could trivially add an attacker-controlled host to the allowlist. The control is only meaningful as a compile-time constant.

##### DNS-resolution-based check

- **ALT-005**: **Description**: Resolve the hostname and check that the resulting IP does not fall in a private range (RFC 1918 / SSRF prevention via IP block).
- **ALT-006**: **Rejection Reason**: IP-based SSRF prevention is incomplete — public Zoho IPs could still be used as a relay, and DNS TOCTOU (time-of-check-time-of-use) issues make IP-based checks unreliable. A domain-suffix allowlist is simpler, more deterministic, and covers the actual threat model (non-Zoho external endpoints).

##### Schema/HTTPS enforcement only

- **ALT-007**: **Description**: Enforce HTTPS and reject plaintext HTTP; do not restrict the host.
- **ALT-008**: **Rejection Reason**: HTTPS enforcement is necessary but not sufficient. An HTTPS endpoint on an attacker-controlled domain still receives the token. Host restriction is the correct additional layer.

## Implementation Notes

- **IMP-001**: The allowlist check runs in `ApiClient.CallAsync` immediately after URL assembly, before `HttpClient.SendAsync` is called and before the `Authorization` header is injected into the request.
- **IMP-002**: URL assembly uses `new Uri(new Uri(request.BaseUrl.TrimEnd('/')), request.Path.TrimStart('/'))` to avoid double-slash or missing-slash edge cases; the resulting `Uri.Host` is passed to the allowlist check.
- **IMP-003**: `--base-url` values that are not valid absolute URIs should produce `INVALID_ARGS` (not `HOST_NOT_ALLOWED`) — validate with `Uri.TryCreate(..., UriKind.Absolute, ...)` before the allowlist check.
- **IMP-004**: Unit tests must cover: allowed suffixes (pass), wrong TLD (`.zoho.ca` — fail), non-Zoho domain (`example.com` — fail), subdomain-of-allowed (`api.desk.zoho.com` — pass), and DNS rebinding attempt (`zoho.com.attacker.com` — fail, suffix check prevents this).
- **IMP-005**: Add `zohoapis.eu` and `zohoapis.com.au` to the allowlist in the first release cycle if confirmed as valid Zoho API domains — verify before shipping.

## References

- **REF-001**: [tech_spec.md — Section 9: API Client — Host Allowlist](../tech_spec.md#9-api-client)
- **REF-002**: [tech_spec.md — Section 11: Error Codes — HOST_NOT_ALLOWED](../tech_spec.md#11-output-contract)
- **REF-003**: [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
- **REF-004**: [ADR-0003: Caller-Supplied Base URL Per API Invocation](./adr-0003-caller-supplied-base-url.md)
- **REF-005**: [ADR-0005: ZohoCorp Domain Hard Block at All Entry Points](./adr-0005-zohocorp-domain-hard-block.md)
