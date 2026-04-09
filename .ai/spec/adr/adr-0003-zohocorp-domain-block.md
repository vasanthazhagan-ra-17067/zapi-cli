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

- **POS-001**: Eliminates any risk of zapi-cli being used — intentionally or accidentally by an AI agent — to authenticate with Zoho's internal corporate accounts.
- **POS-002**: The domain-label check automatically covers all current and future ZohoCorp datacenter TLDs without requiring a maintained list.
- **POS-003**: Fail-closed design: a missing email prevents account creation rather than silently bypassing the check.
- **POS-004**: The block at `api call` provides defence-in-depth.

### Negative

- **NEG-001**: Legitimate Zoho employees using zapi-cli with personal accounts are unaffected, but any ZohoCorp-account usage is unconditionally blocked.
- **NEG-002**: The `AaaServer.profile.READ` scope requirement must be communicated clearly in documentation.
- **NEG-003**: A startup warning is emitted if `accounts.json` already contains a ZohoCorp account, but there is no automatic cleanup.

## Alternatives Considered

##### Opt-out Flag (`--allow-zohocorp`)

- **ALT-001**: **Rejection Reason**: Any override path creates an attack surface. An AI agent instructed via a compromised prompt to pass this flag would circumvent the protection entirely.

##### Best-Effort Warning Only (No Hard Block)

- **ALT-002**: **Rejection Reason**: Warnings are ignorable — by both humans and AI agents. Given the privacy and security implications, a warning is insufficient.

## Implementation Notes

- **IMP-001**: The detection logic and error code `ACCOUNT_DOMAIN_BLOCKED` must be co-located in `ZapiCli.Core` as a sealed static method, not duplicated per command.
- **IMP-002**: `ApiClient.CallAsync` must run the ZohoCorp check against `AccountEntry.Email` before calling `IAuthProvider.GetTokenAsync`.
- **IMP-003**: Unit tests must cover: `@zohocorp.com`, `@zohocorp.eu`, `@ZOHOCORP.COM` (case-insensitive), `@zohocorp.in`, and a negative case (`@zoho.com`).

## References

- **REF-001**: [ADR-0004: Compile-Time Host Allowlist](adr-0004-host-allowlist.md)
- **REF-002**: [ADR-0002: Authentication via OAuth Self-Client](adr-0002-oauth-self-client-authentication.md)
