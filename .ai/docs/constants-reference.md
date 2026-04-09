# Constants Reference

## Convention

All app-wide constants live in named constants files inside `ZapiCli.Core`. They must be documented
in this file every time one is added or changed. The goal is a single index that answers:
_"Where does this magic value come from and why does it exist?"_

## Constants Registry

| Symbol | File | Value | Purpose |
|---|---|---|---|
| `OAuthConstants.RequiredProfileScope` | `src/ZapiCli.Core/OAuthConstants.cs` | `AaaServer.profile.READ` | Zoho profile scope always injected into every login to guarantee that the user-info endpoint returns the account email and ZUID. Without it, account creation fails with `EMAIL_REQUIRED`. |
| `OAuthConstants.DefaultCallbackPort` | `src/ZapiCli.Core/OAuthConstants.cs` | `8085` | Fixed local TCP port for the OAuth callback HTTP server. Register `http://localhost:8085/callback` as a redirect URI in the Zoho Developer Console for every app type that uses the browser flow (`account login`, `scope add`). |
| `ErrorCodes.ACCOUNT_NOT_FOUND` | `src/ZapiCli.Core/ErrorCodes.cs` | `"ACCOUNT_NOT_FOUND"` | Returned when a named account does not exist in accounts.json. |
| `ErrorCodes.ACCOUNT_ALREADY_EXISTS` | `src/ZapiCli.Core/ErrorCodes.cs` | `"ACCOUNT_ALREADY_EXISTS"` | Returned by `account add` / `account login` when the account name is already used. |
| `ErrorCodes.NO_DEFAULT_ACCOUNT` | `src/ZapiCli.Core/ErrorCodes.cs` | `"NO_DEFAULT_ACCOUNT"` | Returned when a command requires a default account but none is set. |
| `ErrorCodes.AUTH_FAILURE` | `src/ZapiCli.Core/ErrorCodes.cs` | `"AUTH_FAILURE"` | Returned when the OAuth token exchange or refresh call fails. |
| `ErrorCodes.NEEDS_REAUTH` | `src/ZapiCli.Core/ErrorCodes.cs` | `"NEEDS_REAUTH"` | Legacy value retained for forward compatibility; not raised by any command after Story 14. |
| `ErrorCodes.API_ERROR` | `src/ZapiCli.Core/ErrorCodes.cs` | `"API_ERROR"` | Returned when `api call` receives a non-2xx response from the Zoho API. |
| `ErrorCodes.INVALID_ARGS` | `src/ZapiCli.Core/ErrorCodes.cs` | `"INVALID_ARGS"` | Returned when Spectre's `Validate()` rejects the provided flags. |
| `ErrorCodes.IO_ERROR` | `src/ZapiCli.Core/ErrorCodes.cs` | `"IO_ERROR"` | Returned on filesystem read/write failure (accounts.json, registry.json, etc.). |
| `ErrorCodes.KEYCHAIN_ERROR` | `src/ZapiCli.Core/ErrorCodes.cs` | `"KEYCHAIN_ERROR"` | Returned when the OS keychain interaction fails. |
| `ErrorCodes.ACCOUNT_DOMAIN_BLOCKED` | `src/ZapiCli.Core/ErrorCodes.cs` | `"ACCOUNT_DOMAIN_BLOCKED"` | Returned when a login attempt targets a `@zohocorp.com` email address (ADR-0003). |
| `ErrorCodes.EMAIL_REQUIRED` | `src/ZapiCli.Core/ErrorCodes.cs` | `"EMAIL_REQUIRED"` | Returned when the Zoho user-info endpoint does not return an email address. |
| `ErrorCodes.HOST_NOT_ALLOWED` | `src/ZapiCli.Core/ErrorCodes.cs` | `"HOST_NOT_ALLOWED"` | Returned when `api call` targets a host outside the compile-time allowlist (ADR-0004). |
| `ErrorCodes.STATE_MISMATCH` | `src/ZapiCli.Core/ErrorCodes.cs` | `"STATE_MISMATCH"` | Returned when the OAuth CSRF `state` parameter in the callback does not match the sent state. |
| `ErrorCodes.LOGIN_TIMEOUT` | `src/ZapiCli.Core/ErrorCodes.cs` | `"LOGIN_TIMEOUT"` | Returned when the 120-second browser authentication window expires before a callback is received. |
| `ErrorCodes.SESSION_NOT_FOUND` | `src/ZapiCli.Core/ErrorCodes.cs` | `"SESSION_NOT_FOUND"` | Returned when a `trace session` command's UUID or name matches no session. |
| `ErrorCodes.SESSION_AMBIGUOUS` | `src/ZapiCli.Core/ErrorCodes.cs` | `"SESSION_AMBIGUOUS"` | Returned when a `trace session` command resolves by `--name` but multiple sessions share that name. |
| `ErrorCodes.EXPORT_PATH_NOT_SET` | `src/ZapiCli.Core/ErrorCodes.cs` | `"EXPORT_PATH_NOT_SET"` | Returned by `trace session start` when no export path is given and no default has been configured. |
| `ErrorCodes.INTERNAL_ERROR` | `src/ZapiCli.Core/ErrorCodes.cs` | `"INTERNAL_ERROR"` | Returned by the global exception handler for unhandled non-`ZapiCliException` errors. |
| `ErrorCodes.REGISTRY_ENTRY_NOT_FOUND` | `src/ZapiCli.Core/ErrorCodes.cs` | `"REGISTRY_ENTRY_NOT_FOUND"` | Returned when an `api registry` operation targets an id that does not exist. |
| `ErrorCodes.REGISTRY_ENTRY_ALREADY_EXISTS` | `src/ZapiCli.Core/ErrorCodes.cs` | `"REGISTRY_ENTRY_ALREADY_EXISTS"` | Returned by `api registry add` when the id is already in the registry. |
| `ErrorCodes.SCOPE_ENHANCE_DENIED` | `src/ZapiCli.Core/ErrorCodes.cs` | `"SCOPE_ENHANCE_DENIED"` | Returned by `scope add` when the user rejects the scope enhancement consent or the callback returns an unexpected state. |
| `ErrorCodes.SCOPE_ENHANCE_FAILED` | `src/ZapiCli.Core/ErrorCodes.cs` | `"SCOPE_ENHANCE_FAILED"` | Returned by `scope add` when the POST to `/oauth/v2/token/scopeenhance` fails. |
| `ErrorCodes.DCL_MISSING` | `src/ZapiCli.Core/ErrorCodes.cs` | `"DCL_MISSING"` | Returned by the Mobile OAuth flow when the token response lacks a `dc_locations` object. |
| `ErrorCodes.RSA_DECRYPT_FAILURE` | `src/ZapiCli.Core/ErrorCodes.cs` | `"RSA_DECRYPT_FAILURE"` | Returned by the Mobile OAuth flow when RSA decryption of `gt_sec` fails. |

## Adding a New Constant

1. Determine if the constant is app-wide (shared across two or more files) or local to one class.
   - **App-wide**: create or update a constants file in `ZapiCli.Core` (e.g., `OAuthConstants.cs`).
   - **Local**: define it inside the class that uses it.
2. For app-wide constants: add a row to the table above with `Symbol`, `File`, `Value`, and `Purpose`.
3. If the constant is a new error code: also add it to `ErrorCodes.cs` and document the conditions under which it is thrown in `docs/HELP.md` (error handling reference table and the affected command section).
