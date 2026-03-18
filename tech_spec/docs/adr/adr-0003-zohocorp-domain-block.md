---
title: "ADR-0003: Hard-Block ZohoCorp Account Domain"
status: "Accepted"
date: "2026-03-18"
authors: "zapi-cli Core Team"
tags: ["architecture", "decision", "security", "authentication", "compliance"]
supersedes: ""
superseded_by: ""
---

# ADR-0003: Hard-Block ZohoCorp Account Domain

## Status

**Accepted**

## Context

ZohoCorp accounts (`*@zohocorp.com`) are Zoho internal employee accounts tied to Zoho's corporate identity infrastructure. zapi-cli is designed for external developers and AI agents accessing Zoho's public REST APIs.

Allowing zapi-cli to authenticate with, store credentials for, or issue API calls on behalf of ZohoCorp accounts poses an unacceptable risk:

- **Data privacy:** Internal Zoho infrastructure may be accessible via ZohoCorp tokens; credential exposure could allow access to non-public internal systems.
- **AI-agent threat surface:** zapi-cli's primary consumer is AI agents. A misconfigured or compromised agent using a ZohoCorp token could exfiltrate internal data or cause unintended mutations in Zoho's production systems.
- **Liability / compliance:** If the tool were distributed publicly and an employee's ZohoCorp credentials were misused through zapi-cli, Zoho would bear responsibility for the design decision that permitted it.

The block must be unconditional — no flag, environment variable, or configuration can bypass it — because any override path would be a potential attack surface.

## Decision

All zapi-cli operations that resolve to a ZohoCorp-domain account are **hard-blocked** at the earliest possible entry point.

**Detection rule** (sealed compile-time constant in `ZapiCli.Core`):

```csharp
email.Split('@')[1].Split('.')[0]
      .Equals("zohocorp", StringComparison.OrdinalIgnoreCase)
```

This matches `zohocorp.com`, `zohocorp.eu`, `zohocorp.in`, `zohocorp.com.au`, and all future datacenter TLDs automatically, without requiring a maintained allowlist of TLD variants.

**Blocked entry points:**

| Entry point | When |
|---|---|
| `account add` | Before any keychain write or config mutation |
| `api call` (active or `--account` override) | Before any HTTP request is dispatched |
| Any `scope` command | Before any mutation to `accounts.json` |

**Email acquisition:** At `account add`, the supplied token is used to call `GET https://accounts.zoho.<dc-domain>/oauth/user/info`. Both `email` and `ZUIDSTRING` are extracted. If no email is returned, `account add` aborts with `EMAIL_REQUIRED` — the ZohoCorp check is never skipped.

**Error output (stderr):**

```json
{ "error": "ZohoCorp accounts are not permitted. Use a personal or external Zoho account.", "code": "ACCOUNT_DOMAIN_BLOCKED", "exitCode": 1 }
```

Exit code: `1`.

## Consequences

### Positive

- **POS-001**: Eliminates any risk of zapi-cli being used — intentionally or accidentally by an AI agent — to authenticate with Zoho's internal corporate accounts, protecting internal data and systems.
- **POS-002**: The domain-label check (not a suffix match) automatically covers all current and future ZohoCorp datacenter TLDs without requiring a maintained list of domains.
- **POS-003**: Fail-closed design: a missing email (e.g., token lacking `AaaServer.profile.READ` scope) prevents account creation rather than silently bypassing the check.
- **POS-004**: The block at `api call` provides defence-in-depth: even if a ZohoCorp account were somehow added to `accounts.json` out-of-band, API calls will still be rejected.

### Negative

- **NEG-001**: Legitimate Zoho employees who wish to use zapi-cli for personal API script automation (using a personal non-ZohoCorp account) are unaffected, but any ZohoCorp-account usage is unconditionally blocked even for benign purposes.
- **NEG-002**: The `AaaServer.profile.READ` scope requirement must be communicated clearly in `account add` documentation; omitting it causes `EMAIL_REQUIRED` errors that may confuse users.
- **NEG-003**: A startup warning is emitted if `accounts.json` already contains a ZohoCorp account (added out-of-band), but there is no automatic cleanup — manual removal is required.

## Alternatives Considered

##### Opt-out Flag (`--allow-zohocorp`)

- **ALT-001**: **Description**: Admin or developer could pass `--allow-zohocorp` to bypass the domain block for specific invocations.
- **ALT-002**: **Rejection Reason**: Any override path creates an attack surface. An AI agent instructed (via a compromised prompt) to pass this flag would circumvent the protection entirely. The security property can only be guaranteed if the block is unconditional.

##### Allowlist Instead of Blocklist

- **ALT-003**: **Description**: Only permit accounts with emails from a configurable set of known external domains, defaulting to blocking everything except well-known public domains.
- **ALT-004**: **Rejection Reason**: An allowlist is harder to maintain and would incorrectly block legitimate external Zoho customer accounts on custom domains. The narrower ZohoCorp-domain blocklist is more precise and less restrictive.

##### Best-Effort Warning Only (No Hard Block)

- **ALT-005**: **Description**: Emit a warning to stderr when a ZohoCorp account is detected but proceed with the operation.
- **ALT-006**: **Rejection Reason**: Warnings are ignorable — by both humans and AI agents. Given the privacy and security implications, a warning is insufficient. Only a hard exit prevents the risk.

## Implementation Notes

- **IMP-001**: The detection logic and error code `ACCOUNT_DOMAIN_BLOCKED` must be co-located in `ZapiCli.Core` as a sealed static method, not duplicated per command — this prevents any command-level implementation from inadvertently skipping the check.
- **IMP-002**: `ApiClient.CallAsync` must run the ZohoCorp check against `AccountEntry.Email` before calling `IAuthProvider.GetTokenAsync`, ensuring no token read from keychain occurs for blocked accounts.
- **IMP-003**: Unit tests must cover: `@zohocorp.com`, `@zohocorp.eu`, `@ZOHOCORP.COM` (case-insensitive), `@zohocorp.in`, and a negative case (`@zoho.com`) to prevent regression.
- **IMP-004**: `account add --dc cn --token <zohocorp-cn-token>` (where user-info returns `@zohocorp.cn`) must also be blocked — the domain-label check covers this without special TLD handling.

## References

- **REF-001**: [tech_spec/tech_spec.md — Section 7: ZohoCorp Account Restriction](../tech_spec/tech_spec.md#7-zohocorp-account-restriction)
- **REF-002**: [ADR-0002: Authentication via OAuth Self-Client](adr-0002-oauth-self-client-authentication.md)
- **REF-003**: [ADR-0004: Host Allowlist for Outbound Requests](adr-0004-host-allowlist.md)
