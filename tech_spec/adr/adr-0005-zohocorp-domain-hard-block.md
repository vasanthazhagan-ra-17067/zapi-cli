---
title: "ADR-0005: ZohoCorp Domain Hard Block at All Entry Points"
status: "Proposed"
date: "2026-03-17"
authors: "zapi-cli Engineering"
tags: ["architecture", "decision", "security", "policy"]
supersedes: ""
superseded_by: ""
---

## Status

**Proposed**

## Context

Zoho employees use `@zohocorp.com` (and datacenter-specific variants such as `@zohocorp.eu`, `@zohocorp.in`, `@zohocorp.com.au`) accounts that are tied to Zoho's **corporate identity infrastructure**. These accounts have elevated internal access privileges and are subject to Zoho's corporate IT and data governance policies.

zapi-cli is a CLI tool **designed for external developers**. Its primary consumer is AI agents running via GitHub Copilot CLI Skills. Allowing the tool to:
- Store a ZohoCorp employee's PAT or OAuth token in the OS keychain
- Make API calls on behalf of a ZohoCorp account
- Include ZohoCorp credentials in agent-driven automation pipelines

...introduces unacceptable privacy and security risks:
- AI agents may exfiltrate internal Zoho data through automated API calls.
- ZohoCorp credentials stored on developer machines extend the attack surface beyond Zoho's controlled corporate environment.
- ZohoCorp accounts may have access to internal APIs not intended for external consumers.

The block must be **unconditional and unbypassable** — no flag, environment variable, or config option may override it. It must apply at all entry points where a ZohoCorp account could be used.

## Decision

**Hard-block all operations involving a ZohoCorp-domain account at the earliest possible entry point.** The detection rule and the blocked-domain label list are **compile-time constants** — not configurable at runtime.

**Detection rule:**
```csharp
email.Split('@')[1].Split('.')[0]
     .Equals("zohocorp", StringComparison.OrdinalIgnoreCase)
```

This covers all datacenter TLDs (`zohocorp.com`, `zohocorp.eu`, `zohocorp.in`, `zohocorp.com.au`) and any future TLDs automatically — by matching the second-level domain label `"zohocorp"` rather than enumerating specific domains.

**Blocked entry points:**

| Entry Point | Check Timing |
|-------------|-------------|
| `account add` | After email is fetched from Zoho user-info API, before keychain write |
| `api call` (active account) | Before any HTTP request is dispatched |
| `api call --account <name>` | Same check on the named account override |
| `scope add/remove/list` | Account resolved first; rejected before any mutation |

**Error output (stderr):**
```json
{
  "error": "ZohoCorp accounts are not permitted. Use a personal or external Zoho account.",
  "code": "ACCOUNT_DOMAIN_BLOCKED",
  "exitCode": 1
}
```

At startup, if `accounts.json` already contains a ZohoCorp-domain account (e.g., previously added before this check was implemented), a warning is written to stderr. Any command resolving to that account is rejected.

## Consequences

### Positive

- **POS-001**: Eliminates the risk of AI agents making API calls against Zoho's corporate infrastructure through a developer's ZohoCorp credentials.
- **POS-002**: Compile-time constant detection rule cannot be disabled via flags, env vars, or config — bypass is not possible without modifying and recompiling the source.
- **POS-003**: The label-based detection (`zohocorp` as the SLD) covers all current and future datacenter TLDs without requiring the domain list to evolve.
- **POS-004**: The check at `account add` prevents ZohoCorp credentials from ever reaching the OS keychain, preserving a clean state.

### Negative

- **NEG-001**: Zoho employees who want to use the tool for legitimate personal/external Zoho accounts with a non-ZohoCorp email cannot add their corporate account even for read-only use cases.
- **NEG-002**: The startup warning for legacy `accounts.json` entries requires careful messaging — users may be confused why a previously working account is now blocked.
- **NEG-003**: The email fetch at `account add` adds a network round-trip before the block can be applied — if the Zoho user-info API is unavailable, `account add` must fail safely rather than silently skipping the check.

## Alternatives Considered

##### Opt-out via flag or config

- **ALT-001**: **Description**: Allow `--allow-zohocorp` flag or a config key to override the block for "trusted" users.
- **ALT-002**: **Rejection Reason**: The risk is systemic — there is no "safe" subset of ZohoCorp account operations for an AI-agent-consumed tool. An opt-out creates a false sense of security and can be accidentally enabled. The block must be unconditional.

##### Domain enumeration (explicit list of blocked TLDs)

- **ALT-003**: **Description**: Maintain a list of known ZohoCorp domains (`["zohocorp.com", "zohocorp.eu", "zohocorp.in", "zohocorp.com.au"]`) and check membership.
- **ALT-004**: **Rejection Reason**: Any new datacenter TLD added by Zoho would silently bypass the block until the list is updated and a new binary is released. The SLD-label approach is inherently forward-compatible.

##### Block only API calls, allow account add

- **ALT-005**: **Description**: Permit `account add` for ZohoCorp accounts; block only subsequent `api call` invocations.
- **ALT-006**: **Rejection Reason**: Allows ZohoCorp credentials to reach the OS keychain, which is itself a security risk. The block must apply at the earliest safe intercept — before any credential storage occurs.

##### No restriction, documentation-only policy

- **ALT-007**: **Description**: Document that ZohoCorp accounts should not be used; rely on user judgment.
- **ALT-008**: **Rejection Reason**: AI agents do not read documentation. The policy must be enforced in code, not communicated in prose.

## Implementation Notes

- **IMP-001**: The detection function lives in `ZapiCli.Core` as a `static bool IsZohoCorpEmail(string email)` method — accessible to both `PatAuthProvider.StoreTokenAsync` and `ApiClient.CallAsync`.
- **IMP-002**: Email is fetched from `https://accounts.zoho.com/oauth/user/info` (with the appropriate datacenter variant based on `--domain`) before any write operations in `account add`. If the fetch fails, `account add` must fail with `AUTH_FAILURE`, not skip the domain check.
- **IMP-003**: The startup warning mechanism scans `accounts.json` on load and emits a stderr warning (not an error) for any ZohoCorp-domain account found. This allows unrelated commands (e.g., `account list`, `util uuid`) to still execute.
- **IMP-004**: Unit tests must cover: `@zohocorp.com`, `@zohocorp.eu`, `@zohocorp.in`, `@zohocorp.com.au`, a plausible future `@zohocorp.jp`, and confirm that `@zoho.com`, `@gmail.com`, and `@example.zohocorp.com` are not blocked.

## References

- **REF-001**: [tech_spec.md — Section 7: ZohoCorp Account Restriction](../tech_spec.md#7-zohocorp-account-restriction)
- **REF-002**: [tech_spec.md — Section 11: Error Codes — ACCOUNT_DOMAIN_BLOCKED](../tech_spec.md#11-output-contract)
- **REF-003**: [ADR-0002: Pluggable Authentication Interface with PAT-First Approach](./adr-0002-pluggable-auth-interface-pat-first.md)
- **REF-004**: [ADR-0006: Host Allowlist as Compile-Time Security Control](./adr-0006-host-allowlist-compile-time-control.md)
