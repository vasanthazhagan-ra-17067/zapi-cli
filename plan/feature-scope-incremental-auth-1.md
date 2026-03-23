---
goal: Implement Zoho Incremental Authorization for scope add, remove NeedsReauth, fix scope handling in account add/login
version: 2.0
date_created: 2026-03-23
last_updated: 2026-03-23
owner: zapi-cli
status: 'Planned'
tags: [feature, bug, refactor, auth]
---

# Introduction

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Four related changes to the OAuth/scope subsystem:

1. **`account add` requires `--scope`**: The Self-Client grant code is scoped at generation time in the Zoho Developer Console. The CLI must accept and persist those same scopes so `scope list` reflects reality and `scope add` has a correct baseline to work from.
2. **`scope add` → Zoho Incremental Authorization**: Replace the current no-op local-only scope tracking with the real two-step Zoho API flow (`POST /scopeenhance` → browser `GET /addextrascope`), so the Zoho refresh token is actually updated with the new scopes.
3. **Remove `NeedsReauth`**: The flag was a workaround for a flow that never worked correctly. The 401 auto-retry in `ApiClient` already handles expired access tokens. Removing `NeedsReauth` eliminates dead code and a misleading field in `account list` / `account show` output.
4. **Fix empty scope list after `account login`**: `ExchangeAndFinalizeAsync` always persists `Scopes = []`, discarding the scopes the user passed at login time.

---

## 1. Requirements & Constraints

- **REQ-001**: `scope add` must perform a real Zoho incremental authorization using `POST /oauth/v2/token/scopeenhance` (Step 1) and browser redirect to `GET /oauth/v2/token/addextrascope` (Step 2).
- **REQ-002**: `scope add` must use the existing `LocalCallbackServer` and `IOAuthBrowserFlow` infrastructure already wired for `account login`.
- **REQ-003**: `scope add` must accept a `--port <PORT>` flag (default `8085`) for the local callback server redirect URI. The user must register `http://localhost:{PORT}/callback` in the Zoho Developer Console.
- **REQ-004**: After a successful scope consent, a token refresh (`RefreshTokenAsync`) must be called with the updated scope list to obtain a fresh access token carrying the new scopes.
- **REQ-005**: `AccountEntry.Scopes` must be populated with the scopes passed to both `account login` and `account add` at the time the account is created.
- **REQ-006**: `NeedsReauth` must be fully removed from: `AccountEntry`, `AccountListView`, `AccountShowView`, `AccountService`, and `ApiClient`.
- **REQ-007**: All existing functionality — 401 auto-retry in `ApiClient`, `ReAuthAsync`, `RefreshTokenAsync` — must continue to work unchanged.
- **REQ-008**: `account add` must accept a required `--scope <SCOPE>` flag (comma-separated). The `Validate()` method must return an error if `--scope` is missing or empty. The `IAccountService.AddAccountAsync` signature must be extended to accept `IEnumerable<string> scopes`.
- **SEC-001**: The `enhance_token` (Step 1 response) is short-lived (600 s). It must never be stored — use it immediately in Step 2 URL construction.
- **SEC-002**: `ZohoCorpGuard` must still fire in `AddScopesAsync` before any network call.
- **CON-001**: `LocalCallbackServer.WaitForCallbackAsync` parses `code` + `state`. The `addextrascope` callback returns `status=success&scope_enhanced=true` or `error=access_denied` — a new dedicated method is required without breaking the existing one.
- **CON-002**: `IAuthProvider.GetScopeEnhancementTokenAsync` must read keychain creds (refresh_token + client_id + client_secret) via the existing `OAuthProvider` keychain key scheme.
- **CON-003**: `AddScopesAsync` in `IAccountService` must accept a `callbackPort` parameter (default `8085`) — the interface signature changes.
- **GUD-001**: All new network calls follow the same error-wrapping pattern as `ExchangeAndFinalizeAsync` (catch non-cancellation exceptions, wrap in `ZapiCliException` with appropriate `ErrorCodes`).
- **GUD-002**: New error code `SCOPE_ENHANCE_DENIED` must be added to `ErrorCodes.cs` for the `access_denied` callback from Zoho.
- **GUD-003**: New error code `SCOPE_ENHANCE_FAILED` must be added to `ErrorCodes.cs` for HTTP-level failures on `POST /scopeenhance`.

---

## 2. Implementation Steps

### Phase 1 — `account add` Scope + Fix `ExchangeAndFinalizeAsync`

- GOAL-001: Accept `--scope` in `account add`, thread it through the service layer, and persist it in `AccountEntry.Scopes` for both `account add` and `account login`.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | In `src/ZapiCli/Commands/AccountCommands.cs` — `AccountAddSettings`: Add `[CommandOption("--scope <SCOPE>")] public string? Scope { get; init; }`. XML doc: "Comma-separated list of OAuth scopes. Must match the scopes selected when generating the grant code in the Zoho Developer Console Self-Client." In `Validate()`, add: `if (string.IsNullOrWhiteSpace(Scope)) return ValidationResult.Error("--scope is required. Enter the same scopes you selected when generating the grant code in the Zoho Developer Console.");` — insert after the `--client-secret` check. | | |
| TASK-002 | In `src/ZapiCli/Commands/AccountCommands.cs` — `AccountAddCommand.ExecuteAsync`: Parse the scope string before calling the service: `var scopes = settings.Scope!.Split(',', StringSplitOptions.TrimEntries \| StringSplitOptions.RemoveEmptyEntries);`. Pass `scopes` to `_service.AddAccountAsync(settings.Name!, settings.Code!, settings.RedirectUri, settings.ClientId!, settings.ClientSecret!, settings.Dc, scopes)`. | | |
| TASK-003 | In `src/ZapiCli.Core/Accounts/IAccountService.cs` — `AddAccountAsync`: Add `IEnumerable<string> scopes` parameter after `dc` and before `CancellationToken ct = default`. Update XML doc to state: "scopes: The OAuth scope strings that were selected when generating the grant code. These are persisted in AccountEntry.Scopes." | | |
| TASK-004 | In `src/ZapiCli.Core/Accounts/AccountService.cs` — `AddAccountAsync`: Add `IEnumerable<string> scopes` parameter. Pass it to `ExchangeAndFinalizeAsync`: `return await ExchangeAndFinalizeAsync(name, code, redirectUri, clientId, clientSecret, dc, scopes, ct)`. | | |
| TASK-005 | In `src/ZapiCli.Core/Accounts/AccountService.cs` — `ExchangeAndFinalizeAsync`: Add `IEnumerable<string> scopes` parameter after `dc` and before `CancellationToken ct`. (Both the private method signature and the XML summary comment if present.) | | |
| TASK-006 | In `src/ZapiCli.Core/Accounts/AccountService.cs` — `ExchangeAndFinalizeAsync`: Change `Scopes = []` to `Scopes = [.. scopes]` in the `AccountEntry` initializer. | | |
| TASK-007 | In `src/ZapiCli.Core/Accounts/AccountService.cs` — `LoginAsync` (internal overload): Update the call to `ExchangeAndFinalizeAsync` to pass the `scopes` array: `await ExchangeAndFinalizeAsync(name, code, redirectUri, clientId, clientSecret, dc, scopes, ct)`. | | |

---

### Phase 2 — Refactor: Remove NeedsReauth

- GOAL-002: Eliminate the `NeedsReauth` flag entirely — it is dead code since the 401 auto-retry handles token expiry.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-008 | In `src/ZapiCli.Core/Accounts/AccountEntry.cs`: Remove the `public bool NeedsReauth { get; init; }` property. | | |
| TASK-009 | In `src/ZapiCli.Core/Accounts/AccountListView.cs`: Remove the `public bool NeedsReauth { get; init; }` property. | | |
| TASK-010 | In `src/ZapiCli.Core/Accounts/AccountShowView.cs`: Remove the `public bool NeedsReauth { get; init; }` property. | | |
| TASK-011 | In `src/ZapiCli.Core/Accounts/AccountService.cs` — `ExchangeAndFinalizeAsync`: Remove `NeedsReauth = false` from the `AccountEntry` initializer. | | |
| TASK-012 | In `src/ZapiCli.Core/Accounts/AccountService.cs` — `ListAccountsAsync`: Remove `NeedsReauth = a.NeedsReauth` from the `AccountListView` projection. | | |
| TASK-013 | In `src/ZapiCli.Core/Accounts/AccountService.cs` — `ShowAccountAsync`: Remove `NeedsReauth = account.NeedsReauth` from the `AccountShowView` projection. | | |
| TASK-014 | In `src/ZapiCli.Core/Accounts/AccountService.cs` — `AddScopesAsync`: Remove `NeedsReauth = true` from the `account with { ... }` expression. The `with` expression still sets `Scopes = updatedScopes`. | | |
| TASK-015 | In `src/ZapiCli.Core/Accounts/AccountService.cs` — `ReAuthAsync`: Remove `NeedsReauth = false` from the `.Select(a => a.Name == name ? a with { NeedsReauth = false } : a)` call — simplify the select to `a with { }` or remove the transform entirely if no other fields change. | | |
| TASK-016 | In `src/ZapiCli.Core/Api/ApiClient.cs`: Remove the entire `NeedsReauth` pre-refresh block — the `if (account.NeedsReauth) { ... }` block that calls `RefreshTokenAsync` and `ClearNeedsReauthAsync`. | | |
| TASK-017 | In `src/ZapiCli.Core/Api/ApiClient.cs`: Remove the entire `ClearNeedsReauthAsync` private method. | | |

---

### Phase 3 — Feature: Zoho Incremental Authorization in `scope add`

- GOAL-003: Implement the full two-step Zoho scope enhancement flow so `scope add` actually appends scopes to the Zoho refresh token via user consent.

#### Sub-phase 3a — Infrastructure

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-018 | In `src/ZapiCli.Core/ErrorCodes.cs`: Add `public const string SCOPE_ENHANCE_DENIED = "SCOPE_ENHANCE_DENIED";` and `public const string SCOPE_ENHANCE_FAILED = "SCOPE_ENHANCE_FAILED";`. | | |
| TASK-019 | In `src/ZapiCli.Core/Auth/IAuthProvider.cs`: Add method `Task<(string EnhanceToken, string ClientId)> GetScopeEnhancementTokenAsync(string accountName, string dc, CancellationToken ct = default);`. XML doc: "Exchanges the stored refresh token for a short-lived scope enhancement token via POST /oauth/v2/token/scopeenhance. Returns (enhanceToken, clientId). Throws ZapiCliException with SCOPE_ENHANCE_FAILED on HTTP failure or missing access_token in response." | | |
| TASK-020 | In `src/ZapiCli.Core/Auth/OAuthProvider.cs`: Implement `GetScopeEnhancementTokenAsync`. **(1)** Read keychain creds via `_keychain.GetAsync(MakeKey(accountName))` and deserialize to `OAuthCredentials`. **(2)** `var baseUrl = DcResolver.GetAccountsBaseUrl(dc)`. **(3)** POST to `{baseUrl}/oauth/v2/token/scopeenhance` with form: `client_id`, `client_secret`, `grant_type=update_scopes_token`, `refresh_token`. **(4)** On non-success HTTP status, wrap in `ZapiCliException(SCOPE_ENHANCE_FAILED, exitCode: 2)`. **(5)** Parse `access_token` from JSON response — throw `SCOPE_ENHANCE_FAILED` if missing or empty. **(6)** Return `(access_token, creds.ClientId)`. | | |
| TASK-021 | In `src/ZapiCli.Core/Auth/LocalCallbackServer.cs`: Add `public virtual async Task WaitForScopeEnhancedCallbackAsync(TimeSpan timeout, CancellationToken ct = default)`. **(1)** Use the same `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter` + `GetContextAsync` pattern as `WaitForCallbackAsync`. **(2)** Parse `query["error"]` — if present, respond with error HTML and throw `ZapiCliException(error, SCOPE_ENHANCE_DENIED, exitCode: 1)`. **(3)** Parse `query["status"]` and `query["scope_enhanced"]` — if `status != "success"` or `scope_enhanced != "true"`, respond with error HTML and throw `ZapiCliException("Scope enhancement was not approved.", SCOPE_ENHANCE_DENIED, exitCode: 1)`. **(4)** Respond with success HTML and return. | | |

#### Sub-phase 3b — AccountService + Interface

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-022 | In `src/ZapiCli.Core/Accounts/IAccountService.cs`: Update `AddScopesAsync` signature — add `int callbackPort = 8085` before `CancellationToken ct = default`. Update XML doc to describe: the two-step browser consent flow; the `callbackPort` param requirement (user must register `http://localhost:{callbackPort}/callback` in Zoho Developer Console); error codes `SCOPE_ENHANCE_DENIED` and `SCOPE_ENHANCE_FAILED`. | | |
| TASK-023 | In `src/ZapiCli.Core/Accounts/AccountService.cs` — replace the entire `AddScopesAsync` body with the new two-step flow: **(1)** Load account, throw `ACCOUNT_NOT_FOUND` if missing. **(2)** `ZohoCorpGuard.AssertNotZohoCorp(account.Email)`. **(3)** Build `updatedScopes` by merging `account.Scopes` + `scopesToAdd` (ordinal dedup — unchanged logic). **(4)** `var (enhanceToken, clientId) = await _authProvider.GetScopeEnhancementTokenAsync(accountName, account.Dc, ct)`. **(5)** `await using var server = new LocalCallbackServer(callbackPort);` **(6)** `var redirectUri = $"http://localhost:{server.Port}/callback";` **(7)** `var baseUrl = DcResolver.GetAccountsBaseUrl(account.Dc);` **(8)** Build `addExtraScopeUrl`: `$"{baseUrl}/oauth/v2/token/addextrascope?client_id={Uri.EscapeDataString(clientId)}&response_type=update_scopes&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(string.Join(",", scopesToAdd))}&enhance_token={Uri.EscapeDataString(enhanceToken)}&logout=true"` **(9)** `Console.Error.WriteLine($"Redirect URI (must be registered in Zoho Developer Console): {redirectUri}");` **(10)** `_browserFlow.OpenBrowser(addExtraScopeUrl);` **(11)** `Console.Error.WriteLine("Waiting for browser scope consent... (timeout: 120s)");` **(12)** `await server.WaitForScopeEnhancedCallbackAsync(TimeSpan.FromSeconds(120), ct);` **(13)** Persist: `var updatedAccount = account with { Scopes = updatedScopes }; ... SaveAsync(...)`. **(14)** `await _authProvider.RefreshTokenAsync(accountName, updatedScopes, account.Dc, ct);` **(15)** Return `(updatedAccount.Name, updatedScopes)`. | | |

#### Sub-phase 3c — CLI Layer

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-024 | In `src/ZapiCli/Commands/ScopeCommands.cs` — `ScopeAddSettings`: Add `[CommandOption("--port <PORT>")] public int Port { get; init; } = 8085;`. XML doc: "Local port for the OAuth callback server. Register http://localhost:{PORT}/callback in the Zoho Developer Console as a redirect URI." | | |
| TASK-025 | In `src/ZapiCli/Commands/ScopeCommands.cs` — `ScopeAddCommand.ExecuteAsync`: Pass `settings.Port` as the `callbackPort` argument: `await _accountService.AddScopesAsync(accountName, incoming, settings.Port)`. | | |

---

## 3. Alternatives

- **ALT-001**: Implement `GetScopeEnhancementTokenAsync` inline in `AccountService` by reading keychain directly. Rejected — direct keychain access from `AccountService` bypasses the `IAuthProvider` abstraction that owns all credential I/O.
- **ALT-002**: Keep `NeedsReauth` as a soft flag that triggers `scope add` without browser. Rejected — it never actually updated the Zoho refresh token, making it misleading and functionally useless.
- **ALT-003**: Add a new `WaitForScopeEnhancedCallbackAsync` override on a subclass of `LocalCallbackServer` instead of adding to the base. Rejected — adds unnecessary class hierarchy. A virtual method on the same class keeps the server reusability straightforward.
- **ALT-004**: Return the `enhance_token` + `client_id` as a single `(string, string)` tuple from `GetScopeEnhancementTokenAsync` vs. adding a separate `GetClientIdAsync`. Chosen: tuple return is simpler and avoids an extra keychain read.
- **ALT-005**: Make `--scope` optional on `account add`, falling back to `[]`. Rejected — a stored empty scope list is misleading and breaks `scope list` output. The scopes are known at grant code generation time and the user must supply them.

---

## 4. Dependencies

- **DEP-001**: `DcResolver.GetAccountsBaseUrl(dc)` already maps all datacenter short names to `https://accounts.zoho.{tld}` — reused without change.
- **DEP-002**: `LocalCallbackServer` — reused; only a new virtual method added.
- **DEP-003**: `IOAuthBrowserFlow` / `OAuthBrowserFlow` — reused without change. `AccountService` already has `_browserFlow` injected.
- **DEP-004**: `IAuthProvider` / `OAuthProvider` — new method `GetScopeEnhancementTokenAsync` added.

---

## 5. Files

- **FILE-001**: `src/ZapiCli/Commands/AccountCommands.cs` — Add `--scope` to `AccountAddSettings`; parse and pass in `AccountAddCommand`.
- **FILE-002**: `src/ZapiCli.Core/Accounts/IAccountService.cs` — Add `scopes` param to `AddAccountAsync`; update `AddScopesAsync` signature.
- **FILE-003**: `src/ZapiCli.Core/Accounts/AccountEntry.cs` — Remove `NeedsReauth`.
- **FILE-004**: `src/ZapiCli.Core/Accounts/AccountListView.cs` — Remove `NeedsReauth`.
- **FILE-005**: `src/ZapiCli.Core/Accounts/AccountShowView.cs` — Remove `NeedsReauth`.
- **FILE-006**: `src/ZapiCli.Core/Accounts/AccountService.cs` — Add `scopes` to `AddAccountAsync` + `ExchangeAndFinalizeAsync`; fix scope persistence; remove all `NeedsReauth`; rewrite `AddScopesAsync` with incremental auth flow.
- **FILE-007**: `src/ZapiCli.Core/Auth/IAuthProvider.cs` — Add `GetScopeEnhancementTokenAsync`.
- **FILE-008**: `src/ZapiCli.Core/Auth/OAuthProvider.cs` — Implement `GetScopeEnhancementTokenAsync`.
- **FILE-009**: `src/ZapiCli.Core/Auth/LocalCallbackServer.cs` — Add `WaitForScopeEnhancedCallbackAsync`.
- **FILE-010**: `src/ZapiCli.Core/ErrorCodes.cs` — Add `SCOPE_ENHANCE_DENIED`, `SCOPE_ENHANCE_FAILED`.
- **FILE-011**: `src/ZapiCli.Core/Api/ApiClient.cs` — Remove `NeedsReauth` pre-refresh block and `ClearNeedsReauthAsync`.
- **FILE-012**: `src/ZapiCli/Commands/ScopeCommands.cs` — Add `--port` to `ScopeAddSettings`; pass to `AddScopesAsync`.

---

## 6. Testing

- **TEST-001**: Unit test `OAuthProvider.GetScopeEnhancementTokenAsync` — mock HTTP to return `{ "access_token": "enhance-tok", ... }` → asserts return value is `("enhance-tok", clientId)`.
- **TEST-002**: Unit test `OAuthProvider.GetScopeEnhancementTokenAsync` — mock HTTP to return non-success status → asserts `ZapiCliException` with `SCOPE_ENHANCE_FAILED`.
- **TEST-003**: Unit test `LocalCallbackServer.WaitForScopeEnhancedCallbackAsync` — via test subclass injecting synthetic query strings: `?status=success&scope_enhanced=true` → no exception; `?error=access_denied` → `ZapiCliException` with `SCOPE_ENHANCE_DENIED`.
- **TEST-004**: Unit test `AccountService.AddScopesAsync` end-to-end using fake callback server (same pattern as `LoginAsync` tests) — verifies scopes are merged, token refresh called, accounts.json updated.
- **TEST-005**: Unit test `AccountService.LoginAsync` (via internal overload) — verifies `AccountEntry.Scopes` is populated with the scopes passed at login time.
- **TEST-006**: Unit test `AccountService.AddAccountAsync` (via `ExchangeAndFinalizeAsync`) — verifies `AccountEntry.Scopes` is populated with the scopes passed via `--scope`.
- **TEST-007**: Verify `AccountEntry`, `AccountListView`, `AccountShowView` no longer have `NeedsReauth` — existing tests that set/assert `NeedsReauth` must be updated or removed.
- **TEST-008**: Verify `ApiClient` no longer reads `NeedsReauth` — confirm the pre-refresh block is gone and the 401 auto-retry path still passes its existing tests.

---

## 7. Risks & Assumptions

- **RISK-001**: The `addextrascope` redirect returns `status` and `scope_enhanced` as query parameters, but the exact parameter names must match the Zoho documentation exactly. Validate against a live test before marking Phase 3 done.
- **RISK-002**: Existing `accounts.json` files on disk may still have `"needs_reauth": true` serialized. Since `AccountEntry` uses `System.Text.Json` with record deserialization, unknown properties are ignored by default — this is safe with no migration needed.
- **RISK-003**: `scope add` now requires browser interaction. AI agents using zapi-cli via `scope add` will not be able to complete the consent step headlessly. This is intentional — the Zoho incremental auth protocol mandates user consent.
- **ASSUMPTION-001**: Zoho responds to `POST /scopeenhance` with `{ "access_token": "...", "token_type": "Bearer", "expires_in": 600 }` as documented.
- **ASSUMPTION-002**: The `addextrascope` success redirect is `{redirect_uri}?status=success&scope_enhanced=true` exactly (not URL-encoded differently).
- **ASSUMPTION-003**: `RefreshTokenAsync` called immediately after consent will return an access token bearing the newly added scopes, since Zoho has appended them to the refresh token.
- **ASSUMPTION-004**: With `account add`, the scopes the user enters via `--scope` match the scopes they selected in the Zoho Developer Console Self-Client UI when generating the grant code. The CLI cannot verify this — it stores them as-is.

---

## 8. Related Specifications / Further Reading

- [zoho-oauth-incremental-authorization.md](../tech_spec/docs/zoho-oauth-incremental-authorization.md)
- [ADR-0002](../tech_spec/docs/adr/) — OAuth Self-Client flow and token refresh strategy
- [ADR-0003](../tech_spec/docs/adr/) — ZohoCorp domain block
