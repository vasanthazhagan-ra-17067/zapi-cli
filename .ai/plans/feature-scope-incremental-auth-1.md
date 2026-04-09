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
- **GUD-001**: All new network calls follow the same error-wrapping pattern as `ExchangeAndFinalizeAsync`.
- **GUD-002**: New error code `SCOPE_ENHANCE_DENIED` must be added to `ErrorCodes.cs` for the `access_denied` callback from Zoho.
- **GUD-003**: New error code `SCOPE_ENHANCE_FAILED` must be added to `ErrorCodes.cs` for HTTP-level failures on `POST /scopeenhance`.

---

## 2. Implementation Steps

### Phase 1 — `account add` Scope + Fix `ExchangeAndFinalizeAsync`

- GOAL-001: Accept `--scope` in `account add`, thread it through the service layer, and persist it in `AccountEntry.Scopes` for both `account add` and `account login`.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | In `AccountCommands.cs` — `AccountAddSettings`: Add `[CommandOption("--scope <SCOPE>")] public string? Scope { get; init; }`. In `Validate()`, add: if `Scope` is null/empty → `ValidationResult.Error("--scope is required...")`. | | |
| TASK-002 | In `AccountAddCommand.ExecuteAsync`: Parse scopes via `settings.Scope!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)`. Pass `scopes` to `_service.AddAccountAsync(...)`. | | |
| TASK-003 | In `IAccountService.cs` — `AddAccountAsync`: Add `IEnumerable<string> scopes` parameter after `dc`. | | |
| TASK-004 | In `AccountService.cs` — `AddAccountAsync`: Add `IEnumerable<string> scopes` parameter. Pass to `ExchangeAndFinalizeAsync`. | | |
| TASK-005 | In `AccountService.cs` — `ExchangeAndFinalizeAsync`: Add `IEnumerable<string> scopes` parameter. | | |
| TASK-006 | In `AccountService.cs` — `ExchangeAndFinalizeAsync`: Change `Scopes = []` to `Scopes = [.. scopes]` in the `AccountEntry` initializer. | | |
| TASK-007 | In `AccountService.cs` — `LoginAsync` (internal overload): Update call to `ExchangeAndFinalizeAsync` to pass the `scopes` array. | | |

---

### Phase 2 — Refactor: Remove NeedsReauth

- GOAL-002: Eliminate the `NeedsReauth` flag entirely — dead code since the 401 auto-retry handles token expiry.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-008 | In `AccountEntry.cs`: Remove `public bool NeedsReauth { get; init; }`. | | |
| TASK-009 | In `AccountListView.cs`: Remove `public bool NeedsReauth { get; init; }`. | | |
| TASK-010 | In `AccountShowView.cs`: Remove `public bool NeedsReauth { get; init; }`. | | |
| TASK-011 | In `AccountService.cs` — `ExchangeAndFinalizeAsync`: Remove `NeedsReauth = false` from the `AccountEntry` initializer. | | |
| TASK-012 | In `AccountService.cs` — `ListAccountsAsync`: Remove `NeedsReauth = a.NeedsReauth` from the `AccountListView` projection. | | |
| TASK-013 | In `AccountService.cs` — `ShowAccountAsync`: Remove `NeedsReauth = account.NeedsReauth` from the `AccountShowView` projection. | | |
| TASK-014 | In `AccountService.cs` — `AddScopesAsync`: Remove `NeedsReauth = true` from the `account with { ... }` expression. | | |
| TASK-015 | In `AccountService.cs` — `ReAuthAsync`: Remove `NeedsReauth = false` from the `.Select` transform. | | |
| TASK-016 | In `ApiClient.cs`: Remove the entire `NeedsReauth` pre-refresh block — the `if (account.NeedsReauth) { ... }` block. | | |
| TASK-017 | In `ApiClient.cs`: Remove the entire `ClearNeedsReauthAsync` private method. | | |

---

### Phase 3 — Feature: Zoho Incremental Authorization in `scope add`

- GOAL-003: Implement the full two-step Zoho scope enhancement flow.

#### Sub-phase 3a — Infrastructure

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-018 | In `ErrorCodes.cs`: Add `public const string SCOPE_ENHANCE_DENIED = "SCOPE_ENHANCE_DENIED";` and `public const string SCOPE_ENHANCE_FAILED = "SCOPE_ENHANCE_FAILED";`. | | |
| TASK-019 | In `IAuthProvider.cs`: Add `Task<(string EnhanceToken, string ClientId)> GetScopeEnhancementTokenAsync(string accountName, string dc, CancellationToken ct = default);`. | | |
| TASK-020 | In `OAuthProvider.cs`: Implement `GetScopeEnhancementTokenAsync`. (1) Read keychain creds. (2) `var baseUrl = DcResolver.GetAccountsBaseUrl(dc)`. (3) POST to `{baseUrl}/oauth/v2/token/scopeenhance` with form: `client_id`, `client_secret`, `grant_type=update_scopes_token`, `refresh_token`. (4) On non-success HTTP, wrap in `ZapiCliException(SCOPE_ENHANCE_FAILED, exitCode: 2)`. (5) Parse `access_token` — throw `SCOPE_ENHANCE_FAILED` if missing. (6) Return `(access_token, creds.ClientId)`. | | |
| TASK-021 | In `LocalCallbackServer.cs`: Add `public virtual async Task WaitForScopeEnhancedCallbackAsync(TimeSpan timeout, CancellationToken ct = default)`. Parse `query["error"]` — if present, throw `ZapiCliException(error, SCOPE_ENHANCE_DENIED, exitCode: 1)`. Parse `query["status"]` and `query["scope_enhanced"]` — if not `success`/`true`, throw `ZapiCliException("Scope enhancement was not approved.", SCOPE_ENHANCE_DENIED, exitCode: 1)`. Return on success. | | |

#### Sub-phase 3b — AccountService + Interface

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-022 | In `IAccountService.cs`: Update `AddScopesAsync` signature — add `int callbackPort = 8085` before `CancellationToken ct = default`. | | |
| TASK-023 | In `AccountService.cs` — replace the entire `AddScopesAsync` body with the new two-step flow: (1) Load account, throw `ACCOUNT_NOT_FOUND` if missing. (2) `ZohoCorpGuard.AssertNotZohoCorp(account.Email)`. (3) Build `updatedScopes` by merging `account.Scopes` + `scopesToAdd` (ordinal dedup). (4) `var (enhanceToken, clientId) = await _authProvider.GetScopeEnhancementTokenAsync(accountName, account.Dc, ct)`. (5) `await using var server = new LocalCallbackServer(callbackPort)`. (6) Build `addExtraScopeUrl`. (7) `_browserFlow.OpenBrowser(addExtraScopeUrl)`. (8) `await server.WaitForScopeEnhancedCallbackAsync(TimeSpan.FromSeconds(120), ct)`. (9) Persist updated account with `updatedScopes`. (10) `await _authProvider.RefreshTokenAsync(accountName, updatedScopes, account.Dc, ct)`. (11) Return `(updatedAccount.Name, updatedScopes)`. | | |

**addextrascope URL format:**
```
{baseUrl}/oauth/v2/token/addextrascope
  ?client_id={clientId}
  &response_type=update_scopes
  &redirect_uri={redirectUri}
  &scope={scopesToAdd joined with comma}
  &enhance_token={enhanceToken}
  &logout=true
```

#### Sub-phase 3c — CLI Layer

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-024 | In `ScopeCommands.cs` — `ScopeAddSettings`: Add `[CommandOption("--port <PORT>")] public int Port { get; init; } = 8085;`. | | |
| TASK-025 | In `ScopeAddCommand.ExecuteAsync`: Pass `settings.Port` as the `callbackPort` argument: `await _accountService.AddScopesAsync(accountName, incoming, settings.Port)`. | | |

---

## 3. Alternatives

- **ALT-001**: Implement `GetScopeEnhancementTokenAsync` inline in `AccountService`. Rejected — bypasses the `IAuthProvider` abstraction.
- **ALT-002**: Keep `NeedsReauth` as a soft flag. Rejected — it never actually updated the Zoho refresh token.
- **ALT-003**: Add new `WaitForScopeEnhancedCallbackAsync` on a subclass. Rejected — adds unnecessary class hierarchy.
- **ALT-004**: Return `enhance_token` + `client_id` as a struct vs. tuple. Chosen: tuple is simpler.
- **ALT-005**: Make `--scope` optional on `account add`. Rejected — a stored empty scope list is misleading and breaks `scope list` output.

---

## 4. Dependencies

- **DEP-001**: `DcResolver.GetAccountsBaseUrl(dc)` — reused without change.
- **DEP-002**: `LocalCallbackServer` — reused; only a new virtual method added.
- **DEP-003**: `IOAuthBrowserFlow` / `OAuthBrowserFlow` — reused without change.
- **DEP-004**: `IAuthProvider` / `OAuthProvider` — new method `GetScopeEnhancementTokenAsync` added.

---

## 5. Files

- **FILE-001**: `src/ZapiCli/Commands/AccountCommands.cs` — Add `--scope` to `AccountAddSettings`.
- **FILE-002**: `src/ZapiCli.Core/Accounts/IAccountService.cs` — Add `scopes` param; update `AddScopesAsync` signature.
- **FILE-003**: `src/ZapiCli.Core/Accounts/AccountEntry.cs` — Remove `NeedsReauth`.
- **FILE-004**: `src/ZapiCli.Core/Accounts/AccountListView.cs` — Remove `NeedsReauth`.
- **FILE-005**: `src/ZapiCli.Core/Accounts/AccountShowView.cs` — Remove `NeedsReauth`.
- **FILE-006**: `src/ZapiCli.Core/Accounts/AccountService.cs` — Add `scopes` threading; remove `NeedsReauth`; rewrite `AddScopesAsync`.
- **FILE-007**: `src/ZapiCli.Core/Auth/IAuthProvider.cs` — Add `GetScopeEnhancementTokenAsync`.
- **FILE-008**: `src/ZapiCli.Core/Auth/OAuthProvider.cs` — Implement `GetScopeEnhancementTokenAsync`.
- **FILE-009**: `src/ZapiCli.Core/Auth/LocalCallbackServer.cs` — Add `WaitForScopeEnhancedCallbackAsync`.
- **FILE-010**: `src/ZapiCli.Core/ErrorCodes.cs` — Add `SCOPE_ENHANCE_DENIED`, `SCOPE_ENHANCE_FAILED`.
- **FILE-011**: `src/ZapiCli.Core/Api/ApiClient.cs` — Remove `NeedsReauth` pre-refresh block and `ClearNeedsReauthAsync`.
- **FILE-012**: `src/ZapiCli/Commands/ScopeCommands.cs` — Add `--port` to `ScopeAddSettings`.

---

## 6. Testing

- **TEST-001**: Unit test `OAuthProvider.GetScopeEnhancementTokenAsync` — mock HTTP to return `{ "access_token": "enhance-tok" }` → asserts return value.
- **TEST-002**: Unit test `OAuthProvider.GetScopeEnhancementTokenAsync` — mock HTTP non-success → asserts `SCOPE_ENHANCE_FAILED`.
- **TEST-003**: Unit test `LocalCallbackServer.WaitForScopeEnhancedCallbackAsync` via test subclass: `?status=success&scope_enhanced=true` → no exception; `?error=access_denied` → `SCOPE_ENHANCE_DENIED`.
- **TEST-004**: Unit test `AccountService.AddScopesAsync` end-to-end using fake callback server.
- **TEST-005**: Unit test `AccountService.LoginAsync` (internal) — verifies `AccountEntry.Scopes` populated.
- **TEST-006**: Unit test `AccountService.AddAccountAsync` — verifies `AccountEntry.Scopes` populated.
- **TEST-007**: Verify `AccountEntry`, `AccountListView`, `AccountShowView` no longer have `NeedsReauth`.
- **TEST-008**: Verify `ApiClient` no longer reads `NeedsReauth`.

---

## 7. Risks & Assumptions

- **RISK-001**: The `addextrascope` redirect parameter names must match Zoho documentation exactly. Validate against a live test before marking Phase 3 done.
- **RISK-002**: Existing `accounts.json` files may still have `"needs_reauth": true` serialized. Since `AccountEntry` uses `System.Text.Json` record deserialization, unknown properties are ignored by default — safe with no migration needed.
- **RISK-003**: `scope add` now requires browser interaction. AI agents using zapi-cli via `scope add` will not be able to complete the consent step headlessly. This is intentional.
- **ASSUMPTION-001**: Zoho responds to `POST /scopeenhance` with `{ "access_token": "...", "token_type": "Bearer", "expires_in": 600 }`.
- **ASSUMPTION-002**: The `addextrascope` success redirect is `{redirect_uri}?status=success&scope_enhanced=true`.
- **ASSUMPTION-003**: `RefreshTokenAsync` called immediately after consent will return an access token bearing the newly added scopes.
- **ASSUMPTION-004**: With `account add`, the scopes the user enters via `--scope` match the scopes selected in the Zoho Developer Console Self-Client UI.

---

## 8. Related Specifications / Further Reading

- [zoho-oauth-incremental-authorization.md](./../docs/zoho-oauth-incremental-authorization.md)
- [ADR-0002](../spec/adr/adr-0002-oauth-self-client-authentication.md) — OAuth Self-Client flow and token refresh strategy
- [ADR-0003](../spec/adr/adr-0003-zohocorp-domain-block.md) — ZohoCorp domain block
