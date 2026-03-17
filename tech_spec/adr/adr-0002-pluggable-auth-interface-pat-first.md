---
title: "ADR-0002: Pluggable Authentication Interface with PAT-First Approach"
status: "Proposed"
date: "2026-03-17"
authors: "zapi-cli Engineering"
tags: ["architecture", "decision", "authentication", "security"]
supersedes: ""
superseded_by: ""
---

## Status

**Proposed**

## Context

zapi-cli must authenticate with Zoho product APIs on behalf of configured accounts. Zoho supports multiple authentication mechanisms:

- **Personal Access Tokens (PAT)** — simple, long-lived bearer tokens that can be created in the Zoho developer console. No browser or redirect flow required.
- **OAuth 2.0 PKCE** — short-lived access tokens with scoped permissions, refreshed via a refresh token. Requires a registered OAuth client application with `client_id` and `client_secret`.

The v1 primary consumer is AI agents running non-interactively. This constrains the authentication approach:
- Agents cannot participate in browser-redirect OAuth flows.
- Agents require deterministic, non-interactive authentication.
- The auth mechanism must be extensible to support OAuth in v2 without restructuring the codebase.
- OAuth `client_id` / `client_secret` for a Zoho application are not user-supplied values — they are registered credentials owned by the tool publisher and must be embedded at compile time.
- Scope changes to OAuth accounts require re-authorization, creating a lifecycle signal (`needs_reauth`) that must be tracked per account.

## Decision

**Define a pluggable `IAuthProvider` interface** and ship **`PatAuthProvider` as the sole v1 implementation**. Design `OAuthProvider` as a v2 extension point with compile-time embedded credentials.

The interface is:
```csharp
public interface IAuthProvider
{
    Task<string> GetTokenAsync(string accountName, CancellationToken ct = default);
    Task StoreTokenAsync(string accountName, string token, CancellationToken ct = default);
    Task ClearTokenAsync(string accountName, CancellationToken ct = default);
}
```

PAT tokens are injected into every outgoing request as:
```
Authorization: Zoho-oauthtoken <token>
```

OAuth `client_id` / `client_secret` are **compile-time constants** in `OAuthProvider.cs` — never runtime config, never user input:
```csharp
private const string ClientId = "PLACEHOLDER_CLIENT_ID";
private const string ClientSecret = "PLACEHOLDER_CLIENT_SECRET";
```

Scope changes set `needs_reauth = true` on the account. Any `api call` against an account with `needs_reauth = true` is hard-blocked with exit code 2 (`NEEDS_REAUTH`) until `account re-auth` is run.

## Consequences

### Positive

- **POS-001**: The `IAuthProvider` interface cleanly decouples auth mechanics from the API client and command layer — adding OAuth in v2 requires no changes to `ApiClient` or any command.
- **POS-002**: PAT flow is fully non-interactive, making it ideal for AI agent consumers in v1.
- **POS-003**: Embedding OAuth credentials at compile time prevents accidental exposure via config files, env vars, or CLI flags.
- **POS-004**: `needs_reauth` tracking surfaces scope drift to the agent immediately at the next API call, preventing silent auth failures with stale scopes.
- **POS-005**: Clear separation between `TokenType` (`"pat"` | `"oauth"`) in `accounts.json` allows `account list` to surface auth type without exposing the token value.

### Negative

- **NEG-001**: PAT tokens are long-lived and cannot be scoped narrowly — a leaked PAT grants access to all Zoho APIs the user's account has access to.
- **NEG-002**: Compile-time OAuth credentials mean a new binary release is required to rotate `client_id` / `client_secret` if they are ever compromised.
- **NEG-003**: The `needs_reauth` gate only applies to OAuth accounts — PAT accounts have no equivalent re-auth lifecycle, so scope changes on a PAT account silently succeed but the new scopes are not enforced at the token level.
- **NEG-004**: v2 OAuth PKCE flow is terminal-based with no browser; this is non-standard and may confuse users familiar with web-based OAuth flows.

## Alternatives Considered

##### Single concrete auth class (no interface)

- **ALT-001**: **Description**: Implement PAT auth directly in `ApiClient` without an abstraction layer.
- **ALT-002**: **Rejection Reason**: Locks the codebase into PAT-only auth permanently; adding OAuth in v2 would require modifying `ApiClient` logic and rewriting tests. The interface cost is low and the extensibility value is high.

##### Environment variable token injection

- **ALT-003**: **Description**: Read the Zoho PAT from an environment variable (e.g., `ZOHO_PAT`) instead of the OS keychain.
- **ALT-004**: **Rejection Reason**: Environment variables are readable by any process under the same user, logged by many shells, and commonly leak into CI logs. OS keychain storage is significantly more secure.

##### Runtime-configurable OAuth credentials

- **ALT-005**: **Description**: Accept `client_id` and `client_secret` as CLI flags or config file values.
- **ALT-006**: **Rejection Reason**: OAuth client credentials are not user credentials — they identify the application. Accepting them at runtime risks users sharing misconfigured credentials or embedding them in scripts. Compile-time constants enforce the correct ownership model.

##### Single-account, no account abstraction

- **ALT-007**: **Description**: Support only one configured account; skip the account name / multi-account model.
- **ALT-008**: **Rejection Reason**: Developers routinely maintain separate Zoho accounts per project or region. Multi-account support is a core requirement and motivates the `--account` global flag pattern.

## Implementation Notes

- **IMP-001**: Register `IAuthProvider` in the DI container; resolve the correct implementation based on `AccountEntry.TokenType` at runtime.
- **IMP-002**: `PatAuthProvider.StoreTokenAsync` must perform the ZohoCorp domain check (see ADR-0005) before writing to the keychain — this is the earliest safe intercept point.
- **IMP-003**: `OAuthProvider.cs` must contain `// TODO: Replace PLACEHOLDER_CLIENT_ID and PLACEHOLDER_CLIENT_SECRET before distributing` as a compile-time warning comment; consider a build-time `#error` guard that triggers when placeholders are detected in release builds.
- **IMP-004**: The `account add --auth-type oauth --token <value>` combination must be rejected with `INVALID_ARGS` — the interface contract explicitly separates user-supplied PAT tokens from OAuth flows.
- **IMP-005**: Success metric: `account add --auth-type pat` completes in under 1 second excluding network latency for the user-info fetch.

## References

- **REF-001**: [tech_spec.md — Section 6: Authentication](../tech_spec.md#6-authentication)
- **REF-002**: [tech_spec.md — Section 5: Data Models — AccountEntry](../tech_spec.md#5-data-models)
- **REF-003**: [ADR-0004: OS Keychain for Secret Storage](./adr-0004-os-keychain-for-secret-storage.md)
- **REF-004**: [ADR-0005: ZohoCorp Domain Hard Block](./adr-0005-zohocorp-domain-hard-block.md)
- **REF-005**: [zoho-cliq-oauth-authentication.md](../zoho-cliq-oauth-authentication.md)
