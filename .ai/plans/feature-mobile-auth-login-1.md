---
goal: Implement Zoho Mobile OAuth 2.0 login flow (AppXMacAuth-style) in zapi-cli
version: "1.0"
date_created: 2026-03-25
owner: zapi-cli
status: 'Planned'
tags: [feature, auth, mobile-oauth]
---

# Introduction

![Status: Planned](https://img.shields.io/badge/status-planned-blue)

Implement the `account mobile-login` command following the Zoho Mobile OAuth 2.0 flow documented
in [`.ai/docs/AppXMacAuth-Pod.md`](../docs/AppXMacAuth-Pod.md).

Unlike `account login` (Self-Client flow), this flow:
- Requires **no `client_secret` from the user** — Zoho delivers it RSA-encrypted in the redirect.
- Navigates to `/oauth/v2/mobile/auth` instead of `/oauth/v2/auth`.
- Returns `gt_sec` (RSA-encrypted client_secret), `gt_hash`, and `accounts-server` in the redirect.
- Requires `dc_locations` in the token response (absence is a hard error).
- Uses the per-user `accounts-server` URL for all subsequent API calls (DCL routing).

Test scenarios live in `tests/ZapiCli.Tests/Auth/MobileLoginAsyncTests.cs` (12 scenarios).
All tests are **RED** (failing with `NotImplementedException`) until this plan is fully executed.

---

## 1. Requirements & Constraints

- **REQ-001**: `account mobile-login` must not require `--client-secret` — it is delivered via the OAuth redirect.
- **REQ-002**: CLI must generate a fresh 2048-bit RSA key pair per login attempt (never reused).
- **REQ-003**: Public key must be sent as `ss_id` (URL-encoded base64 `SubjectPublicKeyInfo`) in the auth URL.
- **REQ-004**: Auth URL must target `/oauth/v2/mobile/auth` with `newmobilepage=true` and `access_type=offline`.
- **REQ-005**: `gt_sec` from the callback must be decrypted using RSA PKCS#1 v1.5 padding to obtain `client_secret`.
- **REQ-006**: Token exchange POST must include `rt_hash` (= `gt_hash` from callback) alongside the standard params.
- **REQ-007**: The `accounts-server` value from the callback overrides the DC-derived base URL for all subsequent calls.
- **REQ-008**: If the token response does not contain `dc_locations`, abort with `DCL_MISSING` error.
- **REQ-009**: CSRF state verification (same as `LoginAsync`) must be applied before any decryption or token exchange.
- **REQ-010**: RSA private key must be held only in memory for the duration of the login; never written to disk or keychain.
- **SEC-001**: `client_secret` received via decryption must be stored in the OS keychain (never logged or written to disk).
- **SEC-002**: RSA key pair must use `System.Security.Cryptography.RSA` — no third-party crypto libraries.
- **CON-001**: The `--port` flag default (8085) and redirect URI format (`http://localhost:{port}/callback`) are unchanged.
- **CON-002**: `ExchangeAndFinalizeAsync` must remain backward-compatible — `account add` and `account login` are unchanged.
- **GUD-001**: Follow the `LoginAsync` internal-overload testability pattern — public method delegates to internal overload that accepts `IRsaKeyPairProvider` and `Func<LocalCallbackServer>`.

---

## 2. Implementation Steps

### Phase 1: `RsaKeyPairProvider` — production implementation

- GOAL-001: Create the concrete `RsaKeyPairProvider` that the DI container will inject into `AccountService`.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | Create `src/ZapiCli.Core/Auth/RsaKeyPairProvider.cs`. Implement `IRsaKeyPairProvider.Generate()`: `RSA.Create(2048)`, export public key as `Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())`. Return `(PublicKeyBase64, rsa)`. The `RSA` instance is owned by the caller who must dispose it. | | |

```csharp
// src/ZapiCli.Core/Auth/RsaKeyPairProvider.cs
using System.Security.Cryptography;

namespace ZapiCli.Core.Auth;

public sealed class RsaKeyPairProvider : IRsaKeyPairProvider
{
    public (string PublicKeyBase64, RSA PrivateKey) Generate()
    {
        var rsa = RSA.Create(2048);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        return (publicKey, rsa);
    }
}
```

---

### Phase 2: `ExchangeAndFinalizeAsync` — add override parameters

- GOAL-002: Extend `ExchangeAndFinalizeAsync` with `accountsServerOverride`, `rtHash`, and `requireDcLocations` to support the mobile flow without duplicating code.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-002 | In `AccountService.cs`, add three optional parameters to `ExchangeAndFinalizeAsync`: `string? accountsServerOverride = null`, `string? rtHash = null`, `bool requireDcLocations = false`. | | |
| TASK-003 | Replace `var baseUrl = DcResolver.GetAccountsBaseUrl(dc);` with `var baseUrl = accountsServerOverride ?? DcResolver.GetAccountsBaseUrl(dc);` | | |
| TASK-004 | In the token exchange form data block, add: `if (rtHash is not null) tokenFormData["rt_hash"] = rtHash;` | | |
| TASK-005 | After parsing `access_token` and `refresh_token`, add the `requireDcLocations` check: if `requireDcLocations == true` and the root JSON does not contain `dc_locations` property with `JsonValueKind.Object`, throw `new ZapiCliException("Token exchange response did not contain 'dc_locations'.", ErrorCodes.DCL_MISSING, exitCode: 2)`. | | |

**Signature after change:**
```csharp
private async Task<(string Name, string Dc)> ExchangeAndFinalizeAsync(
    string name,
    string code,
    string redirectUri,
    string clientId,
    string clientSecret,
    string dc,
    IEnumerable<string> scopes,
    CancellationToken ct,
    string? accountsServerOverride = null,
    string? rtHash = null,
    bool requireDcLocations = false)
```

---

### Phase 3: `AccountService.MobileLoginAsync` — replace stub with real implementation

- GOAL-003: Implement the full Zoho Mobile OAuth 2.0 login orchestration.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-006 | Replace the public `MobileLoginAsync` stub with a delegate call to the internal overload — identical pattern to `LoginAsync`. Public method creates `new RsaKeyPairProvider()` and `() => new LocalCallbackServer(callbackPort)`. | | |
| TASK-007 | Implement the internal `MobileLoginAsync(name, clientId, scopes, dc, callbackPort, rsaProvider, ct, serverFactory)` overload. | | |
| TASK-008 | **Step 1 — Uniqueness check**: abort with `ACCOUNT_ALREADY_EXISTS` if name exists in store. Do NOT open browser yet. | | |
| TASK-009 | **Step 2 — RSA key pair generation**: call `rsaProvider.Generate()`. Wrap in `using` to ensure `privateKey.Dispose()` in `finally` block. | | |
| TASK-010 | **Step 3 — Build mobile auth URL**: call `_browserFlow.BuildMobileAuthorizationUrl(baseUrl, clientId, redirectUri, scopes, state, publicKeyBase64)`. | | |
| TASK-011 | **Step 4 — Open browser and wait for callback**: `await using var server = serverFactory();`, generate state, build redirect URI, call `_browserFlow.OpenBrowser(authUrl)`, then `var mobileResult = await server.WaitForMobileCallbackAsync(TimeSpan.FromSeconds(120), ct)`. | | |
| TASK-012 | **Step 5 — CSRF state check**: if `mobileResult.State != state`, throw `ZapiCliException` with `STATE_MISMATCH`. | | |
| TASK-013 | **Step 6 — RSA decryption of `gt_sec`**: `Convert.FromBase64String(mobileResult.GtSec)` then `privateKey.Decrypt(cipherBytes, RSAEncryptionPadding.Pkcs1)`. Catch `CryptographicException` and `FormatException` → rethrow as `ZapiCliException` with `RSA_DECRYPT_FAILURE`. | | |
| TASK-014 | **Step 7 — Token exchange + finalize**: call `ExchangeAndFinalizeAsync(name, mobileResult.Code, redirectUri, clientId, clientSecret, dc, scopes, ct, accountsServerOverride: mobileResult.AccountsServer, rtHash: mobileResult.GtHash, requireDcLocations: true)`. | | |

**Pseudocode for the internal overload:**
```csharp
internal async Task<(string Name, string Dc)> MobileLoginAsync(
    string name, string clientId, string[] scopes, string dc,
    int callbackPort, IRsaKeyPairProvider rsaProvider,
    CancellationToken ct, Func<LocalCallbackServer> serverFactory)
{
    // Step 1: Uniqueness check (before any browser/crypto work)
    var existing = await _accountStore.FindAsync(name, ct).ConfigureAwait(false);
    if (existing is not null)
        throw new ZapiCliException($"Account '{name}' already exists...",
            ErrorCodes.ACCOUNT_ALREADY_EXISTS, exitCode: 1);

    // Step 2: Generate RSA key pair
    var (publicKeyBase64, privateKey) = rsaProvider.Generate();
    using var _ = privateKey; // ensure disposal

    // Step 3: Start callback server + build auth URL
    var baseUrl = DcResolver.GetAccountsBaseUrl(dc);
    await using var server = serverFactory();
    var state = _browserFlow.GenerateState();
    var redirectUri = $"http://localhost:{server.Port}/callback";
    var authUrl = _browserFlow.BuildMobileAuthorizationUrl(
        baseUrl, clientId, redirectUri, scopes, state, publicKeyBase64);

    _browserFlow.OpenBrowser(authUrl);

    // Step 4: Wait for mobile callback
    var result = await server.WaitForMobileCallbackAsync(
        TimeSpan.FromSeconds(120), ct).ConfigureAwait(false);

    // Step 5: CSRF guard
    if (result.State != state)
        throw new ZapiCliException("OAuth state mismatch — possible CSRF attack.",
            ErrorCodes.STATE_MISMATCH, exitCode: 1);

    // Step 6: Decrypt gt_sec → client_secret
    string clientSecret;
    try
    {
        var cipherBytes = Convert.FromBase64String(result.GtSec);
        var plain = privateKey.Decrypt(cipherBytes, RSAEncryptionPadding.Pkcs1);
        clientSecret = Encoding.UTF8.GetString(plain);
    }
    catch (Exception ex) when (ex is FormatException or CryptographicException)
    {
        throw new ZapiCliException(
            "Failed to decrypt the client secret from the OAuth redirect.",
            ErrorCodes.RSA_DECRYPT_FAILURE, exitCode: 2);
    }

    // Step 7: Token exchange and finalize (DCL routing + dc_locations check)
    return await ExchangeAndFinalizeAsync(
        name, result.Code, redirectUri, clientId, clientSecret, dc, scopes, ct,
        accountsServerOverride: result.AccountsServer,
        rtHash: result.GtHash,
        requireDcLocations: true).ConfigureAwait(false);
}
```

---

### Phase 4: `AccountMobileLoginCommand` — CLI command

- GOAL-004: Expose `account mobile-login` via the Spectre.Console.Cli command tree.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-015 | Add `AccountMobileLoginSettings : GlobalSettings` to `AccountCommands.cs`. Required options: `--name <NAME>`, `--client-id <CLIENT_ID>`, `--scope <SCOPE>` (comma-separated). Optional: `--dc <DC>` (default `"us"`), `--port <PORT>` (default `8085`). No `--client-secret` option. | | |
| TASK-016 | Add `AccountMobileLoginCommand : AsyncCommand<AccountMobileLoginSettings>`. `ExecuteAsync` calls `_service.MobileLoginAsync(name, clientId, scopes, dc, port)` and writes `{ "status": "ok", "data": { "name", "dc" } }`. | | |

---

### Phase 5: DI registration

- GOAL-005: Register `IRsaKeyPairProvider` and wire `account mobile-login` into the CLI.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-017 | In `DependencyInjectionRegistrar.cs`, add `services.AddSingleton<IRsaKeyPairProvider, RsaKeyPairProvider>();`. | | |
| TASK-018 | In `Program.cs`, add `config.AddCommand<AccountCommands.AccountMobileLoginCommand>("mobile-login")` inside the `account` branch. | | |

---

### Phase 6: Tests — make 12 scenarios GREEN

- GOAL-006: All 12 test scenarios in `MobileLoginAsyncTests.cs` must pass.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-019 | Remove `NotImplementedException` from `AccountService.MobileLoginAsync` public stub. | | |
| TASK-020 | Remove `NotImplementedException` from `AccountService.MobileLoginAsync` internal overload. | | |
| TASK-021 | Run `dotnet test src/zapi-cli.sln` — all 12 new scenarios must pass, all prior tests must remain green. | | |

---

## 3. Alternatives

- **ALT-001**: Generate `rook_cook` AES fingerprint (as AppXMacAuth does). Skipped — server uses it for anomaly detection only, not access control. Can be added in a follow-up story.
- **ALT-002**: Store `dc_locations` JSON in `AccountEntry` for URL-transform support. Deferred — no current CLI command needs per-user URL transforms.
- **ALT-003**: Use `RSAEncryptionPadding.OaepSHA256` instead of PKCS#1 v1.5. Not viable — AppXMacAuth pod uses PKCS#1 v1.5; the server encrypts with that padding.
- **ALT-004**: Add `--client-secret` as an optional flag. Deferred — the whole point of this flow is to NOT require the user to copy the secret.

---

## 4. Dependencies

- **DEP-001**: `System.Security.Cryptography` (in-box .NET 8+, no NuGet package needed).
- **DEP-002**: `IRsaKeyPairProvider` interface — created in this plan (Phase 1).
- **DEP-003**: `MobileCallbackResult` record — test scaffolding (already exists).
- **DEP-004**: `LocalCallbackServer.WaitForMobileCallbackAsync` — stub already added.
- **DEP-005**: `IOAuthBrowserFlow.BuildMobileAuthorizationUrl` — stub already added in `OAuthBrowserFlow`.

---

## 5. Files

| File | Action |
|------|--------|
| `src/ZapiCli.Core/Auth/RsaKeyPairProvider.cs` | **CREATE** — production implementation |
| `src/ZapiCli.Core/Auth/IRsaKeyPairProvider.cs` | Already created (stub) — no changes needed |
| `src/ZapiCli.Core/Auth/MobileCallbackResult.cs` | Already created — no changes needed |
| `src/ZapiCli.Core/Auth/LocalCallbackServer.cs` | Modified (stub `WaitForMobileCallbackAsync`) — complete |
| `src/ZapiCli.Core/Auth/IOAuthBrowserFlow.cs` | Modified (`BuildMobileAuthorizationUrl` added) — complete |
| `src/ZapiCli.Core/Auth/OAuthBrowserFlow.cs` | Modified (mobile URL builder implementation) — complete |
| `src/ZapiCli.Core/ErrorCodes.cs` | Modified (`DCL_MISSING`, `RSA_DECRYPT_FAILURE` added) — complete |
| `src/ZapiCli.Core/Accounts/IAccountService.cs` | Modified (`MobileLoginAsync` signature added) — complete |
| `src/ZapiCli.Core/Accounts/AccountService.cs` | **MODIFY** — replace `NotImplementedException` stubs (Phases 2–3) |
| `src/ZapiCli/Commands/AccountCommands.cs` | **MODIFY** — add `AccountMobileLoginSettings` + command (Phase 4) |
| `src/ZapiCli/DependencyInjectionRegistrar.cs` | **MODIFY** — register `IRsaKeyPairProvider` (Phase 5) |
| `src/ZapiCli/Program.cs` | **MODIFY** — add `mobile-login` command (Phase 5) |
| `tests/ZapiCli.Tests/Auth/MobileLoginAsyncTests.cs` | Already created (12 scenarios) — no changes needed |
| `tests/ZapiCli.Tests/Fakes/FakeRsaKeyPairProvider.cs` | Already created — no changes needed |

---

## 6. Testing

| Test | Scenario | Verifies |
|------|----------|----------|
| TEST-001 | Scenario 1 | `IRsaKeyPairProvider.Generate()` is called before browser opens |
| TEST-002 | Scenario 2 | Auth URL path contains `/oauth/v2/mobile/auth` |
| TEST-003 | Scenario 3 | Auth URL `ss_id` parameter equals the generated public key base64 |
| TEST-004 | Scenario 4 | Token exchange POST body contains `client_secret=<decrypted value>` |
| TEST-005 | Scenario 5 | Token exchange POST body contains `rt_hash=<gt_hash value>` |
| TEST-006 | Scenario 6 | Token exchange URL host matches `accounts-server` from the redirect |
| TEST-007 | Scenario 7 | Throws `DCL_MISSING` when `dc_locations` absent from token response |
| TEST-008 | Scenario 8 | Throws `STATE_MISMATCH` when CSRF state doesn't match |
| TEST-009 | Scenario 9 | Throws `ACCOUNT_ALREADY_EXISTS` before browser opens |
| TEST-010 | Scenario 10 | Throws `LOGIN_TIMEOUT` when callback server times out |
| TEST-011 | Scenario 11 | Throws `RSA_DECRYPT_FAILURE` when `gt_sec` is corrupt/undecodable |
| TEST-012 | Scenario 12 | Happy path: keychain written once, account persisted with correct email/ZUID |

---

## 7. Risks & Assumptions

- **RISK-001**: Zoho may change the mobile auth endpoint or add required fields in future versions.
- **RISK-002**: RSA PKCS#1 v1.5 is susceptible to Bleichenbacher padding oracle attacks in some contexts. In this usage it is acceptable: the decryption is local, one-shot, and the attacker would need to control the Zoho server.
- **ASSUMPTION-001**: The Zoho Developer Console Self-Client type "Mobile/Desktop App" uses `/oauth/v2/mobile/auth` and returns `gt_sec`, `gt_hash`, and `accounts-server` in the redirect.
- **ASSUMPTION-002**: The `ZUID` field in the user-info response may be returned as a number or string (handled already in `ExchangeAndFinalizeAsync`).

---

## 8. Related Specifications / Further Reading

- [AppXMacAuth Pod Technical Documentation](./../docs/AppXMacAuth-Pod.md)
- [ADR-0002 — OAuth Authentication Strategy](../spec/adr/adr-0002-oauth-self-client-authentication.md)
- [MobileLoginAsyncTests — 12 TDD Scenarios](../../tests/ZapiCli.Tests/Auth/MobileLoginAsyncTests.cs)
