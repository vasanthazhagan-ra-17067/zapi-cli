---
title: "ADR-0002: Authentication via OAuth Self-Client with Pluggable IAuthProvider"
status: "Accepted"
date: "2026-03-18"
authors: "zapi-cli Core Team"
tags: ["architecture", "decision", "authentication", "oauth", "security"]
supersedes: ""
superseded_by: ""
---

# ADR-0002: Authentication via OAuth Self-Client with Pluggable IAuthProvider

## Status

**Accepted**

## Context

zapi-cli must authenticate against Zoho's OAuth 2.0 infrastructure to call Zoho product REST APIs on behalf of users. The primary consumer is AI agents operating via GitHub Copilot CLI Skills — an unattended, non-interactive environment.

Key constraints:

- **No browser available** in agent runtime environments. Browser-redirect flows (Authorization Code) are not viable for the primary use case.
- **Multiple Zoho accounts** must be storable and independently authenticated (different datacenters, different scope sets).
- **Token refresh** must happen automatically at runtime when a token expires or when scopes are changed, without user interaction.
- **Credentials must never appear in logs, config files, or stdout.** OS keychain is the only acceptable store.
- **Pluggability:** The authentication mechanism may need to be swapped in future (e.g., a managed identity or service-account auth for P3 enterprise scenarios), so callers should not know which auth mechanism is in use.

## Decision

v1 supports **OAuth Self-Client** (also known as Self-Client Grant) only. The user supplies a pre-obtained access token, client ID, and client secret at `account add` time. These credentials are stored in the OS keychain and used for automatic token refresh.

All authentication is accessed through an `IAuthProvider` interface:

```csharp
public interface IAuthProvider
{
    Task<string> GetTokenAsync(string accountName, CancellationToken ct = default);
    Task StoreTokenAsync(string accountName, string token, CancellationToken ct = default);
    Task ClearTokenAsync(string accountName, CancellationToken ct = default);
}
```

`OAuthProvider` implements this interface and:
- Stores `access_token + client_id + client_secret` in the OS keychain under key `zapi-cli:<accountName>:oauth`.
- Injects tokens as `Authorization: Zoho-oauthtoken <token>` in every outgoing request.
- Automatically refreshes the access token via the Zoho OAuth token endpoint when `NeedsReauth == true` or when a 401 response is received. A single retry follows a successful refresh.
- If refresh fails, emits `AUTH_FAILURE` on stderr and exits with code 2.

Scope changes (`scope add`/`scope remove`) set `NeedsReauth = true` on the account, triggering auto-refresh on the next `api call`.

Browser-based flows and any form of device code flow are **explicitly excluded from v1**.

## Consequences

### Positive

- **POS-001**: Self-Client tokens require no browser interaction, making the flow fully compatible with unattended AI-agent and CI environments.
- **POS-002**: The `IAuthProvider` interface decouples `ApiClient` from the concrete authentication strategy, enabling future auth providers (device code, managed identity) without changes to calling code.
- **POS-003**: Automatic token refresh on 401 and on `NeedsReauth` prevents silent failures during long-running agent sessions where tokens have expired between calls.
- **POS-004**: Scopes are tracked per account in `accounts.json`, allowing incremental scope changes without requiring the user to manually reconstruct and supply all scopes on every re-auth.

### Negative

- **NEG-001**: Self-Client tokens must be pre-obtained by the user via the Zoho Developer Console — additional setup friction compared to browser-redirect flows that can obtain tokens inline.
- **NEG-002**: Client credentials (`client_id`, `client_secret`) are stored in the OS keychain; if the keychain is compromised, credentials could be used to obtain new tokens indefinitely until the app is revoked.
- **NEG-003**: No browser flow support means users who want to authenticate with Zoho accounts that require 2FA or SSO browser prompts cannot use v1 without obtaining tokens out-of-band.
- **NEG-004**: Automatic single-retry on 401 means a double HTTP round-trip on the first use after token expiry — acceptable in practice given Zoho API latency.

## Alternatives Considered

##### OAuth 2.0 Authorization Code + PKCE (Browser Redirect)

- **ALT-001**: **Description**: The canonical OAuth flow for interactive desktop apps. Opens a local browser page for user login; a local HTTP listener captures the redirect code.
- **ALT-002**: **Rejection Reason**: No browser or local HTTP server is available in AI agent runtime environments. This flow would prevent non-interactive use entirely.

##### OAuth 2.0 Device Authorization Grant

- **ALT-003**: **Description**: The user visits a verification URL on any device, enters a short code, and the CLI polls for token issuance. Works in headless/terminal environments.
- **ALT-004**: **Rejection Reason**: Zoho does not publicly document or support the Device Authorization Grant at this time. Implementing it would require unstable, undocumented API surface.

##### Static Long-Lived API Key / Bearer Token (No Refresh)

- **ALT-005**: **Description**: User provides a long-lived token; zapi-cli uses it as-is with no refresh. Simpler implementation.
- **ALT-006**: **Rejection Reason**: Zoho OAuth access tokens have a finite lifetime. A no-refresh design would require user intervention whenever a token expires, breaking unattended agent workflows.

##### Per-Request Token Injection via Environment Variable

- **ALT-007**: **Description**: Rather than storing credentials, require the user to set `ZAPI_CLI_TOKEN` before each invocation; zapi-cli reads it and makes no keychain calls.
- **ALT-008**: **Rejection Reason**: Environment variables are visible to child processes, may appear in shell history, and offer no refresh capability. This conflicts with the security requirement that tokens never appear in shell-visible contexts.

## Implementation Notes

- **IMP-001**: `OAuthProvider.GetTokenAsync` must never throw on token expiry; it returns the stored token and relies on the caller (`ApiClient`) to detect 401 and trigger refresh.
- **IMP-002**: After a successful auto-refresh, `AccountEntry.NeedsReauth` is set to `false` and `accounts.json` is written before the retried request is sent, preventing redundant refreshes on parallel invocations.
- **IMP-003**: `AaaServer.profile.READ` must be included in the Self-Client app's granted scopes at `account add` time to guarantee the user-info endpoint returns both `email` and `ZUIDSTRING`. This must be documented in user-facing `account add` help text.
- **IMP-004**: The keychain key format `zapi-cli:<accountName>:oauth` is a sealed constant — not configurable — to prevent key-collision attacks from crafted account names.
- **IMP-005**: Future `IAuthProvider` implementations (P3) should be registered in the DI container with a strategy selector keyed on `AccountEntry.AuthType` (to be added to the model in a future ADR).

## References

- **REF-001**: [ADR-0005: OS Keychain Abstraction](adr-0005-os-keychain-abstraction.md)
- **REF-002**: [ADR-0003: ZohoCorp Domain Block](adr-0003-zohocorp-domain-block.md)
