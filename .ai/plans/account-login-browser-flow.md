# Plan: `account login` — Browser OAuth Redirect Flow

## Status: Ready for implementation

## Reference Documents
- **OAuth API flow**: [`tech_spec/docs/Authentication.md`](../../tech_spec/docs/Authentication.md) — covers the Zoho Self-Client OAuth 2.0 flow: grant token generation, token exchange (Step 3), token refresh (Step 4), redirect URI requirements, and multi-datacenter endpoints. All HTTP calls in this plan follow the contracts defined there.

---

## TL;DR
Add a new `zapi-cli account login` command that starts a local HTTP callback server, opens the browser to the Zoho OAuth authorization URL, waits for the redirect with the grant code, then reuses the existing token exchange logic. The existing `account add --code` remains untouched for scripting/CI use.

**Key decisions:**
- New `account login` command — `account add` is unchanged
- `--scope` flag accepts comma-separated scopes (e.g. `scope1,scope2,scope3`)
- `--file <FILE>` flag accepts a pre-filled config file (template at `templates/account-login.json`)
- CLI flags override JSON file values when both are provided
- Random ephemeral port for local callback listener
- CSRF protection via cryptographically random `state` parameter
- 120-second timeout waiting for browser auth

---

## Phase 1: `LocalCallbackServer` — new file

**File:** `src/ZapiCli.Core/Auth/LocalCallbackServer.cs` — **CREATE**

- Allocate ephemeral port: bind `TcpListener` to port 0, read assigned port, release
- Start `HttpListener` on `http://localhost:{port}/callback/`
- Expose `Port` property so callers can build the `redirect_uri`
- `WaitForCallbackAsync(TimeSpan timeout, CancellationToken ct)`:
  - Returns `(string code, string state)` tuple on success
  - If callback contains `?error=...` (e.g. `access_denied`), throw `ZapiCliException` with that error immediately — do not wait for timeout
  - Throws `OperationCanceledException` on timeout
- On success: respond to the browser with a success HTML page ("Authentication successful! You can close this tab.")
- Implements `IDisposable` — `HttpListener` is disposed in `Dispose()`; callers must wrap in `await using` to guarantee cleanup on timeout, cancellation, or error

---

## Phase 2: `OAuthBrowserFlow` — new file

**Files:**
- `src/ZapiCli.Core/Auth/IOAuthBrowserFlow.cs` — **CREATE** (interface, for DI and testability; declares `GenerateState()`, `BuildAuthorizationUrl(...)`, `OpenBrowser(url)`)
- `src/ZapiCli.Core/Auth/OAuthBrowserFlow.cs` — **CREATE** (implementation)

- `GenerateState()` → URL-safe base64 of 32 cryptographically random bytes via `RandomNumberGenerator.GetBytes(32)`
- `BuildAuthorizationUrl(baseUrl, clientId, redirectUri, scopes, state)`:
  - Returns `{baseUrl}/oauth/v2/auth?response_type=code&client_id={clientId}&redirect_uri={redirectUri}&scope={scopes}&state={state}&access_type=offline`
  - `scopes` is a `string[]` joined with `,`
- `OpenBrowser(url)`:
  - macOS: `Process.Start("open", url)`
  - Linux: `Process.Start("xdg-open", url)`
  - Windows: `Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })`
  - Always print the URL to console as fallback for manual paste

---

## Phase 3: `AccountService` refactor — existing file

**File:** `src/ZapiCli.Core/Accounts/AccountService.cs` — **MODIFY**

> Constructor: inject `IOAuthBrowserFlow` alongside existing dependencies (`IAuthProvider`, `IAccountStore`, `HttpClient`, `ILogger`).

1. Extract private helper `ExchangeAndFinalizeAsync(name, code, redirectUri, clientId, clientSecret, dc)`:
   - Contains the shared body of `AddAccountAsync` today: token exchange → user-info validation → ZohoCorp guard → keychain store → accounts.json persist
   - `AddAccountAsync` delegates to it (no behavioral change)

2. Add `LoginAsync(string name, string clientId, string clientSecret, string[] scopes, string dc, CancellationToken ct = default)`:
   1. Uniqueness check — same as `AddAccountAsync`
   2. Create `LocalCallbackServer` inside an `await using` block — guarantees disposal on timeout, cancellation, or error
   3. Resolve dc base URL via `DcResolver`
   4. Generate `state` via `OAuthBrowserFlow.GenerateState()`
   5. Build authorization URL with `redirect_uri = http://localhost:{port}/callback` (no trailing slash — Zoho validates this value; the `HttpListener` prefix uses a trailing slash internally)
   6. Print URL to console + call `OAuthBrowserFlow.OpenBrowser(url)`
   7. Print "Waiting for browser authentication..." to stderr
   8. `WaitForCallbackAsync(TimeSpan.FromSeconds(120), ct)` → `(code, returnedState)`
   9. Verify `returnedState == sentState` → throw `ZapiCliException("STATE_MISMATCH", ...)` if mismatch
   10. Call `ExchangeAndFinalizeAsync(name, code, $"http://localhost:{port}/callback", clientId, clientSecret, dc)`
   11. Return `(name, dc)` — same shape as `AddAccountAsync`

**File:** `src/ZapiCli.Core/Accounts/IAccountService.cs` — **ADD** `LoginAsync` signature (with `CancellationToken ct = default`)

---

## Phase 4: `LoginConfig` model — new file

**File:** `src/ZapiCli/Commands/LoginConfig.cs` — **CREATE**

CLI-layer input model — placed in `ZapiCli/Commands/` because it is only consumed by `AccountLoginCommand` and never reaches `AccountService` (which takes individual parameters). Deserializes the `--file` file. Kebab-case field names via `[JsonPropertyName]`:

| JSON key        | Type       | Required | Default |
|-----------------|------------|----------|---------|
| `name`          | `string`   | Yes      | —       |
| `client-id`     | `string`   | Yes      | —       |
| `client-secret` | `string`   | Yes      | —       |
| `scope`         | `string[]` | Yes      | —       |
| `dc`            | `string`   | No       | `"us"`  |

Includes `Validate()` returning a `List<string>` of missing/invalid field errors.

---

## Template Deliverable: `account-login.json`

**File:** `templates/account-login.json` — **CREATE**

Ships with the repo. Users copy it, fill in values, and pass it with `--file`. Independent of all phases — can be delivered at any point.

```json
{
  "name": "",
  "client-id": "",
  "client-secret": "",
  "scope": ["ZohoAPI.Resource.READ"],
  "dc": "us"
}
```

Usage:
```
cp templates/account-login.json my-login.json
# fill in your values, then:
zapi-cli account login --file my-login.json
```

---

## Phase 5: `AccountLoginCommand` — existing file

**File:** `src/ZapiCli/Commands/AccountCommands.cs` — **ADD**

### `AccountLoginSettings` (inherits `GlobalSettings`)

| Flag              | Type     | Required                         | Notes                                          |
|-------------------|----------|----------------------------------|------------------------------------------------|
| `--file <FILE>` | `string` | No                               | Path to filled-in `account-login.json`         |
| `--name <NAME>`   | `string` | Required if `--file` absent    |                                                |
| `--client-id`     | `string` | Required if `--file` absent    |                                                |
| `--client-secret` | `string` | Required if `--file` absent    |                                                |
| `--scope <SCOPE>` | `string` | Required if `--file` absent    | Comma-separated: `scope1,scope2` → `string[]`  |
| `--dc <DC>`       | `string` | No                               | Default `"us"`, same valid-DC set as `account add` |

### `Validate()` logic

1. If `--file` is provided:
   - Verify file exists and is readable → error if not
   - Deserialize JSON → `LoginConfig`, run `Validate()` → surface errors
2. If `--file` is absent: all of `--name`, `--client-id`, `--client-secret`, `--scope` are required
3. CLI flags always win over JSON file values when both are present

### `AccountLoginCommand.ExecuteAsync()`

1. If `--file` provided: load `LoginConfig` as base values
2. Apply any CLI flag overrides (name, client-id, client-secret, scope, dc)
3. Parse `--scope` string → `string[]` (split on `,`, trim whitespace)
4. Call `_service.LoginAsync(name, clientId, clientSecret, scopes, dc)`
5. Write `{ "status": "ok", "data": { "name": "...", "dc": "..." } }` to stdout

---

## Phase 6: DI + CLI registration

**File:** `src/ZapiCli/DependencyInjectionRegistrar.cs` — **MODIFY**
- Register `IOAuthBrowserFlow → OAuthBrowserFlow` as singleton (constructor-injected into `AccountService`)
- `LocalCallbackServer` is instantiated per-login inside `AccountService.LoginAsync` (not DI-managed)

**File:** `src/ZapiCli/Program.cs` — **MODIFY**
- Add `account login` to the Spectre.Console.Cli command tree, registering `AccountLoginCommand`

---

## Relevant Files

| File | Action |
|------|--------|
| `templates/account-login.json` | CREATE — static template |
| `src/ZapiCli.Core/Auth/LocalCallbackServer.cs` | CREATE (`IDisposable`) |
| `src/ZapiCli.Core/Auth/IOAuthBrowserFlow.cs` | CREATE (interface) |
| `src/ZapiCli.Core/Auth/OAuthBrowserFlow.cs` | CREATE (implementation) |
| `src/ZapiCli/Commands/LoginConfig.cs` | CREATE |
| `src/ZapiCli.Core/Accounts/IAccountService.cs` | ADD `LoginAsync` signature |
| `src/ZapiCli.Core/Accounts/AccountService.cs` | ADD `LoginAsync`, extract `ExchangeAndFinalizeAsync`, inject `IOAuthBrowserFlow` |
| `src/ZapiCli.Core/ErrorCodes.cs` | ADD `STATE_MISMATCH`, `LOGIN_TIMEOUT` |
| `src/ZapiCli/Commands/AccountCommands.cs` | ADD `AccountLoginSettings` + `AccountLoginCommand` |
| `src/ZapiCli/DependencyInjectionRegistrar.cs` | ADD `IOAuthBrowserFlow → OAuthBrowserFlow` registration |
| `src/ZapiCli/Program.cs` | ADD `account login` to command tree |

---

## Verification Checklist

- [ ] `zapi-cli account login --name dev --client-id X --client-secret Y --scope ZohoAPI.Resource.READ,ZohoAPI.Resource.WRITE` → browser opens, terminal shows `{ "status": "ok", "data": { "name": "dev", "dc": "us" } }`
- [ ] `zapi-cli account login --file my-login.json` (copied from template, filled in) → same result, no individual flags needed
- [ ] `zapi-cli account login --file my-login.json --name staging` → file as base, `--name` overrides to `"staging"`
- [ ] `zapi-cli account login --file missing.json` → error: file not found
- [ ] `zapi-cli account login --file bad.json` (missing required fields) → error lists missing fields
- [ ] `zapi-cli account list` → newly logged-in account appears
- [ ] `zapi-cli api call --url https://... --account dev` → authenticated request succeeds
- [ ] Callback with wrong `state` value → `STATE_MISMATCH` error (CSRF guard)
- [ ] No browser action within 120s → timeout error
- [ ] Existing `zapi-cli account add --code ...` works unchanged
- [ ] All existing tests pass
- [ ] Unit: `LoginConfig.Validate()` — happy path and each required-field-missing case
- [ ] Unit: `OAuthBrowserFlow.BuildAuthorizationUrl()` — correct URL format and scope joining
- [ ] Unit: `LocalCallbackServer` — port allocation returns a non-zero port

---

## Further Considerations

1. **Zoho loopback URI matching**: Zoho may require exact `redirect_uri` match including port number (vs. RFC 8252 which allows any loopback port). If so, the user must pre-register `http://localhost:{port}` in the Developer Console. Mitigation: expose an optional `--port` flag as a fixed-port fallback.
2. **OAuth error in callback**: `LocalCallbackServer` must handle `?error=access_denied` (or any `?error=...`) in the redirect — surface it immediately rather than waiting for the 120s timeout.
