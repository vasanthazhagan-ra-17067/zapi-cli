# zapi-cli

**zapi-cli** is a standalone, multi-platform CLI binary for interacting with any Zoho product's REST APIs. It manages multiple Zoho accounts, handles OAuth authentication, and exposes a general-purpose HTTP API invoker designed for scriptable, deterministic use by both human developers and AI agents (GitHub Copilot CLI Skills, Claude Agent Skills).

Ships as a single self-contained binary — no runtime, no dependencies, no installation required beyond placing the binary in your `PATH`.

---

## Table of Contents

1. [Installation & Getting Started](#installation--getting-started)
2. [Output Contract](#output-contract)
3. [Authentication Overview](#authentication-overview)
4. [Datacenters](#datacenters)
5. [Command Reference](#command-reference)
   - [account login](#account-login)
   - [account list](#account-list)
   - [account show](#account-show)
   - [account set-default](#account-set-default)
   - [account remove](#account-remove)
   - [account re-auth](#account-re-auth)
   - [account rename](#account-rename)
   - [api call](#api-call)
   - [api registry list](#api-registry-list)
   - [api registry add](#api-registry-add)
   - [api registry update](#api-registry-update)
   - [api registry show](#api-registry-show)
   - [api registry remove](#api-registry-remove)
   - [util time-ms](#util-time-ms)
   - [util uuid](#util-uuid)
   - [util time-now](#util-time-now)
   - [scope add](#scope-add)
   - [scope list](#scope-list)
   - [trace session start](#trace-session-start)
   - [trace session list](#trace-session-list)
   - [trace session export](#trace-session-export)
   - [trace session close](#trace-session-close)
   - [trace session reopen](#trace-session-reopen)
   - [trace session remove](#trace-session-remove)
   - [trace config set](#trace-config-set)
   - [trace config show](#trace-config-show)
   - [config set env-file](#config-set-env-file)
   - [config set scope-file](#config-set-scope-file)
   - [config set app-dir](#config-set-app-dir)
   - [config show](#config-show)
6. [Global Flags](#global-flags)
7. [Scripting & AI Agent Usage](#scripting--ai-agent-usage)
8. [Error Handling Reference](#error-handling-reference)
9. [Security](#security)
10. [Platform Notes](#platform-notes)

---

## Installation & Getting Started

### Download the binary

Pre-built binaries are available for all major platforms:

| Platform | Binary path (in release archive) |
|---|---|
| macOS (Apple Silicon) | `build/osx-arm64/zapi-cli` |
| macOS (Intel) | `build/osx-x64/zapi-cli` |
| Linux (x64) | `build/linux-x64/zapi-cli` |
| Linux (ARM64) | `build/linux-arm64/zapi-cli` |
| Windows (x64) | `build/win-x64/zapi-cli.exe` |
| Windows (ARM64) | `build/win-arm64/zapi-cli.exe` |

### Make it executable and place it in your PATH

**macOS / Linux:**
```bash
chmod +x ./zapi-cli
mv ./zapi-cli /usr/local/bin/zapi-cli
```

**Windows (PowerShell):**
```powershell
# Copy to a directory already on your PATH, e.g.:
Copy-Item .\zapi-cli.exe "$env:USERPROFILE\bin\zapi-cli.exe"
```

### Verify installation

```bash
zapi-cli --help
```

Expected output:
```
USAGE:
    zapi-cli [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help    Prints help information

COMMANDS:
    account
    api
    util
```

### First-time setup

Before making any API calls you need to add at least one account. See [Authentication Overview](#authentication-overview) for setup prerequisites.

```bash
# Set up credentials via env-file (one-time)
zapi-cli config set env-file /path/to/.env

# Add an account via Mobile OAuth browser flow
zapi-cli account login --scope "ZohoCliq.Channels.READ,ZohoCliq.Messages.CREATE"
```

Once an account is added, set it as the default so you don't need to pass `--account` on every invocation:

```bash
zapi-cli account set-default --name myaccount
```

---

## Output Contract

All output follows a strict machine-readable JSON contract. This is intentional — it makes the tool safe to use in pipelines and AI agent contexts without any fragile screen-scraping.

### Success

Printed to **stdout**:
```json
{ "status": "ok", "data": { ... } }
```

The shape of `data` varies per command and is documented in each command's section.

### Error

Printed to **stderr**:
```json
{ "error": "<human-readable message>", "code": "<ERROR_CODE>", "exitCode": <n> }
```

### Exit codes

| Exit code | Meaning |
|---|---|
| `0` | Success |
| `1` | General error (see `code` field in stderr JSON) |
| `2` | Authentication failure (token exchange failed, re-auth required) |

> **Note for AI agents:** Always check the exit code first. Exit code 2 means the account needs re-authentication — invoke `zapi-cli account re-auth --name <ACCOUNT>` before retrying the failed command.

---

## Authentication Overview

zapi-cli uses the **Zoho Mobile OAuth 2.0 flow** (`/oauth/v2/mobile/auth`). This requires a **Mobile Application** (or Desktop Application) client registered in the Zoho Developer Console — not a Self-Client or Server-based client.

For full setup instructions including how to create the Mobile Application client and register the redirect URI, see [docs/zoho-mobile-app-setup.md](zoho-mobile-app-setup.md).

### Prerequisites

1. A Mobile Application client registered at [https://api-console.zoho.com](https://api-console.zoho.com).
2. The redirect URI `http://localhost:8085/callback` registered in the client's settings.
3. `ZOHO_CLIENT_ID` set in your environment (or via a configured env-file — see [`config set env-file`](#config-set-env-file)).
4. `ZOHO_CLIENT_SECRET` set in your environment (optional for RSA-capable clients, required otherwise).

```bash
# Set up credentials via env-file (one-time setup)
zapi-cli config set env-file /path/to/.env

# Optional: configure a scope file so you don't need --scope on every login
zapi-cli config set scope-file /path/to/scopes.txt

# Add an account — opens browser, DC auto-detected from callback
zapi-cli account login --scope "ZohoCliq.Channels.READ,ZohoCliq.Messages.CREATE"
```

### Token storage

All tokens (access token + refresh token) are stored exclusively in the OS keychain. They are never written to disk in plaintext. See [Platform Notes](#platform-notes) for keychain locations per OS.

### Token refresh

Access tokens are automatically refreshed when:
- A `401 Unauthorized` response is received from Zoho.
- A manual `account re-auth` is run.

---

## Datacenters

`account login` automatically detects the datacenter from the `location` parameter returned in the OAuth callback. You do not need to specify a datacenter manually.

The detected datacenter is stored with the account and used for all subsequent API calls. The following datacenter values may be detected:

| Value | Region | Accounts base URL |
|---|---|---|
| `us` | United States | `https://accounts.zoho.com` |
| `eu` | Europe | `https://accounts.zoho.eu` |
| `in` | India | `https://accounts.zoho.in` |
| `au` | Australia | `https://accounts.zoho.com.au` |
| `cn` | China | `https://accounts.zoho.com.cn` |
| `jp` | Japan | `https://accounts.zoho.jp` |
| `sa` | Saudi Arabia | `https://accounts.zoho.sa` |
| `uk` | United Kingdom | `https://accounts.zoho.uk` |
| `ca` | Canada | `https://accounts.zohocloud.ca` |

---

## Command Reference

### account login

Authenticate a new account using the Zoho Mobile OAuth 2.0 flow. Opens a browser window where you sign in, then completes token exchange automatically. DC is auto-detected from the callback. No flags are required — all credentials come from environment variables.

For full setup instructions (creating the Mobile Application client, registering the redirect URI, and configuring credentials via env-file) see [docs/zoho-mobile-app-setup.md](zoho-mobile-app-setup.md).

```
USAGE:
    zapi-cli account login [OPTIONS]

OPTIONS:
    --name <NAME>      Account alias (optional; derived from email if omitted)
    --scope <SCOPE>    Additional comma-separated OAuth scopes (additive to scope-file)

ENVIRONMENT VARIABLES (resolved from env-file if configured):
    ZOHO_CLIENT_ID       Zoho client ID (required)
    ZOHO_CLIENT_SECRET   Zoho client secret (optional for RSA-capable Mobile clients)
```

**Credential resolution:** Set `ZOHO_CLIENT_ID` and optionally `ZOHO_CLIENT_SECRET` in your environment or in a `.env` file configured with `zapi-cli config set env-file <path>`.

**Scope resolution:** Scopes are merged from two sources (deduplicated case-insensitively):
1. `--scope` flag: comma-separated scopes inline.
2. Configured scope-file (set via `zapi-cli config set scope-file <path>`): one scope per line or comma-separated; lines starting with `#` are treated as comments.

At least one scope must be resolved from one of these sources, or `SCOPE_FILE_NOT_CONFIGURED` is thrown. `AaaServer.profile.READ` is always injected automatically.

**Account name:** If `--name` is omitted, the name is derived from the authenticated email address by replacing `@` and `.` with `_` (e.g. `user@example.com` → `user_example_com`).

---

> ⚠️ **Required Setup — Redirect URI Registration**
>
> The redirect URI `http://localhost:8085/callback` **must be registered** in your Zoho Developer Console application before running this command. See [docs/zoho-mobile-app-setup.md](zoho-mobile-app-setup.md) for step-by-step instructions.

---

#### How the flow works

1. zapi-cli reads `ZOHO_CLIENT_ID` from the environment (loaded from env-file if configured).
2. It generates an RSA key pair. The public key (`ss_id`) is embedded in the auth URL.
3. It starts a local HTTP server on `http://localhost:8085/callback`.
4. It builds the Zoho Mobile OAuth URL (`/oauth/v2/mobile/auth`) using `https://accounts.zoho.com` as the global entry point.
5. It opens the browser to that URL. You authenticate interactively.
6. Zoho redirects to the callback with `code`, `state` (CSRF), `gt_sec` (RSA-encrypted client secret), and `location` (detected datacenter).
7. zapi-cli validates the CSRF state, decrypts `gt_sec` with the RSA private key to recover `client_secret`.
8. It posts to the Zoho accounts server indicated by the callback to exchange the code for tokens.
9. It fetches user info (email, ZUID) to derive the account name (if `--name` was omitted).
10. Credentials are stored in the OS keychain and the account is persisted.

The flow times out after **120 seconds** if no callback is received.

#### Example (scope from flag)

```bash
zapi-cli account login --scope "ZohoCliq.Channels.READ,ZohoCliq.Messages.CREATE"
```

#### Example (named account, scope from configured scope-file)

```bash
zapi-cli account login --name work
```

#### Example output (stdout)

```json
{"status":"ok","data":{"name":"user_example_com","dc":"us"}}
```

#### Stderr during flow

While the browser is open, informational lines are printed to stderr:
```
Redirect URI (must be registered in Zoho Developer Console): http://localhost:8085/callback
Waiting for browser authentication... (timeout: 120s)
```

AI agents and scripts should ignore stderr during this command and check stdout + exit code after completion.

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `ENV_FILE_NOT_CONFIGURED` | `ZOHO_CLIENT_ID` is not set in the environment or configured env-file. | Run `zapi-cli config set env-file <path>` (pointing to a file with `ZOHO_CLIENT_ID=...`) or export the variable. |
| `SCOPE_FILE_NOT_CONFIGURED` | No scopes were resolved from `--scope` or the configured scope-file. | Pass `--scope <SCOPES>` or run `zapi-cli config set scope-file <path>`. |
| `IO_ERROR` | The configured scope-file path does not exist at runtime. | Update with `zapi-cli config set scope-file <path>`. |
| `LOGIN_TIMEOUT` | Browser callback not received within 120 seconds. | Ensure the browser opened and you completed sign-in. Re-run the command. |
| `STATE_MISMATCH` | The `state` parameter in the callback did not match. Possible CSRF. | Re-run to generate a fresh state token. |
| `AUTH_FAILURE` | Token exchange with Zoho failed. | Verify `ZOHO_CLIENT_ID`, that the redirect URI is registered, and that your Zoho credentials are correct. |
| `ACCOUNT_ALREADY_EXISTS` | An account with the resolved name already exists. | Remove the existing account or pass `--name <different-name>`. |
| `ACCOUNT_DOMAIN_BLOCKED` | Authenticated as a `@zohocorp.*` account. | Use a customer Zoho account. |

---

### account list

List all configured accounts.

```
USAGE:
    zapi-cli account list [OPTIONS]
```

Returns all accounts with tokens masked. Safe to log — no secrets are exposed.

#### Example

```bash
zapi-cli account list
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "accounts": [
      {
        "name": "myaccount",
        "email": "user@example.com",
        "dc": "us",
        "is_default": true
      },
      {
        "name": "eu-staging",
        "email": "user@example.com",
        "dc": "eu",
        "is_default": false
      }
    ]
  }
}
```

#### Fields

| Field | Type | Description |
|---|---|---|
| `name` | string | Account alias |
| `email` | string | Zoho account email address |
| `dc` | string | Datacenter code |
| `is_default` | boolean | Whether this is the default account |

---

### account show

Show full details for a single account. Credentials are never included in output (ADR-0008).

```
USAGE:
    zapi-cli account show [OPTIONS]

OPTIONS:
    --name <NAME>              Account alias
    --email <EMAIL>            Identify account by email address
    --zuidstring <ZUIDSTRING>  Identify account by Zoho User ID string

```

Exactly one of `--name`, `--email`, or `--zuidstring` is required. Use `--name` with the default account if no identifier is passed will use the default account (if set).

#### Example

```bash
zapi-cli account show --name myaccount
```

#### Example output

```json
{
  "name": "myaccount",
  "dc": "us",
  "email": "user@example.com",
  "is_default": true,
  "scopes": ["ZohoFiles.files.READ"],
  "zuid": null
}
```

> **Note:** No credential fields (`access_token`, `client_id`, `client_secret`) are ever emitted in `account show` output (ADR-0008).

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `ACCOUNT_NOT_FOUND` | No account with that name/email/zuidstring exists. | Run `account list` to see available accounts. |
| `NO_DEFAULT_ACCOUNT` | No identifier was provided and no default is set. | Run `account set-default` or pass `--name`/`--email`/`--zuidstring`. |
| `DUPLICATE_IDENTIFIER` | More than one account matched the given email or ZUID. | Use `--name` to identify the account unambiguously. |
| `INVALID_ARGS` | More than one of `--name`, `--email`, `--zuidstring` was provided. | Pass exactly one identifier. |

---

### account set-default

Set the default account used when `--account` is not specified.

```
USAGE:
    zapi-cli account set-default [OPTIONS]

OPTIONS:
    --name <NAME>              Account alias
    --email <EMAIL>            Identify account by email address
    --zuidstring <ZUIDSTRING>  Identify account by Zoho User ID string
```

Exactly one of `--name`, `--email`, or `--zuidstring` is required.

#### Example

```bash
zapi-cli account set-default --name myaccount
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "name": "myaccount",
    "is_default": true
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `ACCOUNT_NOT_FOUND` | The named account does not exist. | Run `account list` to verify the account name. |
| `INVALID_ARGS` | None or more than one of `--name`, `--email`, `--zuidstring` was provided. | Pass exactly one identifier. |
| `DUPLICATE_IDENTIFIER` | More than one account matched the given email or ZUID. | Use `--name` to identify the account unambiguously. |

---

### account remove

Remove an account, revoke its OAuth token, and delete it from the keychain.

```
USAGE:
    zapi-cli account remove [OPTIONS]

OPTIONS:
    --name <NAME>              Account alias
    --email <EMAIL>            Identify account by email address
    --zuidstring <ZUIDSTRING>  Identify account by Zoho User ID string
```

Exactly one of `--name`, `--email`, or `--zuidstring` is required.

This command attempts a server-side token revocation call to Zoho (best-effort — the account is removed locally even if revocation fails). The access token and refresh token are deleted from the OS keychain.

> ⚠️ **Warning:** This action is irreversible. The associated refresh token is revoked at Zoho's servers. To use the same OAuth app again you will need to generate a new grant code or run `account login` again.

#### Example

```bash
zapi-cli account remove --name myaccount
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "name": "myaccount",
    "removed": true
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `ACCOUNT_NOT_FOUND` | No account with that name/email/zuidstring exists. | Run `account list` to verify. |
| `INVALID_ARGS` | None or more than one identifier was provided. | Pass exactly one of `--name`, `--email`, `--zuidstring`. |
| `DUPLICATE_IDENTIFIER` | More than one account matched the given email or ZUID. | Use `--name` to identify the account unambiguously. |
| `KEYCHAIN_ERROR` | Failed to delete credentials from the OS keychain. | Check keychain permissions and retry. |

---

### account re-auth

Re-authenticate an existing account using its stored `client-id` and `client-secret` to obtain a fresh access token.

```
USAGE:
    zapi-cli account re-auth [OPTIONS]

OPTIONS:
    --name <NAME>              Account alias
    --email <EMAIL>            Identify account by email address
    --zuidstring <ZUIDSTRING>  Identify account by Zoho User ID string
```

Exactly one of `--name`, `--email`, or `--zuidstring` is required.

Use this command when an `api call` returns a `NEEDS_REAUTH` error code, or when a token refresh is needed after adding new scopes.

Re-auth uses the stored refresh token to silently obtain a new access token without opening a browser.

#### Example

```bash
zapi-cli account re-auth --name myaccount
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "name": "myaccount"
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `ACCOUNT_NOT_FOUND` | The named account does not exist. | Run `account list` to verify. |
| `INVALID_ARGS` | None or more than one identifier was provided. | Pass exactly one of `--name`, `--email`, `--zuidstring`. |
| `DUPLICATE_IDENTIFIER` | More than one account matched the given email or ZUID. | Use `--name` to identify the account unambiguously. |
| `AUTH_FAILURE` | Token refresh failed — refresh token may be expired or revoked. | Run `account remove` then re-add the account via `account login`. |
| `KEYCHAIN_ERROR` | Could not read credentials from the OS keychain. | Check keychain permissions. |

---

### account rename

Rename an existing account to a new alias. The account's keychain entry is also updated to the new name.

```
USAGE:
    zapi-cli account rename [OPTIONS]

OPTIONS:
    --name <NAME>              Current account alias
    --email <EMAIL>            Identify account by email address
    --zuidstring <ZUIDSTRING>  Identify account by Zoho User ID string
    --new-name <NEW_NAME>      New account alias (required)
```

Exactly one of `--name`, `--email`, or `--zuidstring` is required to identify the account to rename. `--new-name` is always required.

The new name must not contain `/`, `\`, `:`, `*`, or `?` characters.

#### Example

```bash
# Rename by current name
zapi-cli account rename --name work --new-name work-eu

# Rename by email
zapi-cli account rename --email user@example.com --new-name personal
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "old_name": "work",
    "new_name": "work-eu"
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `ACCOUNT_NOT_FOUND` | No account matched the identifier. | Run `account list` to verify. |
| `ACCOUNT_ALREADY_EXISTS` | An account with `--new-name` already exists. | Choose a different name or remove the conflicting account. |
| `INVALID_ARGS` | `--new-name` was omitted, contains invalid characters, or zero/multiple source identifiers were provided. | Provide exactly one source identifier and a valid `--new-name`. |
| `DUPLICATE_IDENTIFIER` | More than one account matched the given email or ZUID. | Use `--name` to identify the account unambiguously. |
| `KEYCHAIN_ERROR` | Failed to rename credentials in the OS keychain. | Check keychain permissions. |
| `ACCOUNT_RENAME_FAILED` | Rename completed for accounts.json but keychain update failed. | The account is saved under the new name in accounts.json; manually update or remove and re-add. |

---

### api call

Invoke a Zoho API endpoint and return the raw JSON response.

```
USAGE:
    zapi-cli api call [OPTIONS]

OPTIONS:
    --url <URL>              Full Zoho API endpoint URL (required)
    -X, --method <METHOD>    HTTP method: GET, POST, PUT, PATCH, DELETE (required)
    --body <BODY>            Inline JSON request body
    --body-file <FILE>       Read JSON request body from a file
    --header <HEADER>        Add a request header (repeatable, format: Key:Value)
    --query <PARAM>          Add a query parameter (repeatable, format: key=value)
    -a, --account <ACCOUNT>  Use a specific account (overrides default)
```

**Authentication is automatic.** zapi-cli injects `Authorization: Zoho-oauthtoken <token>` on every request. You do not need to pass auth headers manually.

**Token refresh is automatic.** If a `401` response is received, the token is refreshed transparently before the request is retried.

**Host allowlist:** Only the following host suffixes are permitted. Any other URL is rejected with `HOST_NOT_ALLOWED`:
- `zoho.com`
- `zoho.eu`
- `zoho.in`
- `zoho.com.au`
- `zohoapis.com`
- `zohoapis.in`

#### GET request

```bash
zapi-cli api call \
  --url "https://www.zohoapis.com/cliq/v2/channels" \
  -X GET
```

#### GET with query parameters

```bash
zapi-cli api call \
  --url "https://www.zohoapis.com/cliq/v2/channels" \
  -X GET \
  --query "limit=50" \
  --query "page=1"
```

#### POST with inline JSON body

```bash
zapi-cli api call \
  --url "https://www.zohoapis.com/cliq/v2/channels/general/message" \
  -X POST \
  --body '{"text": "Hello from zapi-cli"}'
```

#### POST with body from file

```bash
zapi-cli api call \
  --url "https://www.zohoapis.com/cliq/v2/channels/general/message" \
  -X POST \
  --body-file ./message.json
```

#### PUT with custom headers

```bash
zapi-cli api call \
  --url "https://www.zohoapis.com/crm/v6/Leads/1234567890" \
  -X PUT \
  --header "Content-Type:application/json" \
  --body '{"data": [{"Last_Name": "Smith"}]}'
```

#### Using a non-default account

```bash
zapi-cli api call \
  --url "https://www.zohoapis.in/cliq/v2/channels" \
  -X GET \
  --account eu-staging
```

#### Example output (stdout)

The `data` field contains the raw Zoho API response body parsed as JSON:

```json
{
  "status": "ok",
  "data": {
    "channels": [
      {
        "id": "123456789",
        "name": "general",
        "type": "open"
      }
    ]
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `HOST_NOT_ALLOWED` | The URL host is not on the Zoho allowlist. | Use a valid Zoho API URL (e.g., `zohoapis.com`). |
| `INVALID_ARGS` | `--url` or `--method` was not provided. | Both flags are required. |
| `API_ERROR` | Zoho returned a non-2xx HTTP response. | Check the `data` field in stderr for the Zoho error details. |
| `NEEDS_REAUTH` | The stored token needs refreshing. | Run `account re-auth --name <ACCOUNT>` then retry. |
| `AUTH_FAILURE` | Token refresh failed during automatic retry. | Re-add the account via `account login`. |
| `ACCOUNT_NOT_FOUND` | The account specified with `--account` does not exist. | Run `account list` to verify. |
| `NO_DEFAULT_ACCOUNT` | No account specified and no default set. | Run `account set-default --name <ACCOUNT>`. |

---

### api registry list

List all entries in the local API registry. No account required.

```
USAGE:
    zapi-cli api registry list [OPTIONS]

OPTIONS:
    --account, -a <ACCOUNT>    Use a specific account (not required for registry commands).
    --help                     Show help.
```

#### Example output (stdout)

```json
[
  {
    "id": "cliq-channels",
    "url": "https://cliq.zoho.com/api/v2/channels",
    "method": "GET",
    "purpose": "List all Cliq channels"
  }
]
```

Returns an empty array `[]` if no entries have been registered.

---

### api registry add

Add a new named API endpoint to the local registry. No account required. The `--url` is validated against the host allowlist (ADR-0004).

```
USAGE:
    zapi-cli api registry add [OPTIONS]

OPTIONS:
    --id <ID>                  Unique identifier for this registry entry (required).
    --url <URL>                Full endpoint URL (required). Must be a Zoho domain.
    --method <METHOD>          HTTP method: GET, POST, PUT, PATCH, DELETE (required).
    --purpose <PURPOSE>        Human-readable description of what this endpoint does (required).
    --help                     Show help.
```

#### Example

```bash
zapi-cli api registry add \
  --id cliq-channels \
  --url https://cliq.zoho.com/api/v2/channels \
  --method GET \
  --purpose "List all Cliq channels"
```

#### Example output (stdout)

```json
{ "status": "ok", "data": { "id": "cliq-channels" } }
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `REGISTRY_ENTRY_ALREADY_EXISTS` | An entry with the same `--id` already exists. | Use `api registry update --id <ID>` to modify it or choose a different id. |
| `HOST_NOT_ALLOWED` | The `--url` host is not on the Zoho allowlist. | Use a URL under `zoho.com`, `zohoapis.com`, etc. |
| `INVALID_ARGS` | A required flag is missing or `--method` is invalid. | All four flags are required. Method must be one of `GET POST PUT PATCH DELETE`. |

---

### api registry update

Update one or more fields of an existing registry entry. Only the supplied flags are changed; omitted flags retain their existing values.

```
USAGE:
    zapi-cli api registry update [OPTIONS]

OPTIONS:
    --id <ID>                  Id of the entry to update (required).
    --url <URL>                New full endpoint URL (optional).
    --method <METHOD>          New HTTP method (optional). GET, POST, PUT, PATCH, DELETE.
    --purpose <PURPOSE>        New human-readable description (optional).
    --help                     Show help.
```

At least one of `--url`, `--method`, or `--purpose` must be provided.

#### Example

```bash
# Update only the purpose
zapi-cli api registry update --id cliq-channels --purpose "Fetch all Cliq channels"
```

#### Example output (stdout)

```json
{ "status": "ok", "data": { "id": "cliq-channels" } }
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `REGISTRY_ENTRY_NOT_FOUND` | No entry with the given `--id` exists. | Run `api registry list` to see all ids. |
| `HOST_NOT_ALLOWED` | The new `--url` host is not on the Zoho allowlist. | Use a URL under `zoho.com`, `zohoapis.com`, etc. |
| `INVALID_ARGS` | No updatable field was provided, or `--method` value is invalid. | Provide at least one of `--url`, `--method`, or `--purpose`. |

---

### api registry show

Show a single registry entry by its id.

```
USAGE:
    zapi-cli api registry show [OPTIONS]

OPTIONS:
    --id <ID>                  Id of the entry to show (required).
    --help                     Show help.
```

#### Example

```bash
zapi-cli api registry show --id cliq-channels
```

#### Example output (stdout)

```json
{
  "id": "cliq-channels",
  "url": "https://cliq.zoho.com/api/v2/channels",
  "method": "GET",
  "purpose": "List all Cliq channels"
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `REGISTRY_ENTRY_NOT_FOUND` | No entry with the given `--id` exists. | Run `api registry list` to see all registered ids. |
| `INVALID_ARGS` | `--id` was not provided. | `--id` is required. |

---

### api registry remove

Remove an entry from the local API registry by its id.

```
USAGE:
    zapi-cli api registry remove [OPTIONS]

OPTIONS:
    --id <ID>                  Id of the entry to remove (required).
    --help                     Show help.
```

#### Example

```bash
zapi-cli api registry remove --id cliq-channels
```

#### Example output (stdout)

```json
{ "status": "ok", "data": { "id": "cliq-channels" } }
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `REGISTRY_ENTRY_NOT_FOUND` | No entry with the given `--id` exists. | Run `api registry list` to see all registered ids. |
| `INVALID_ARGS` | `--id` was not provided. | `--id` is required. |

---

### scope add

Add one or more OAuth scopes to an existing account. After adding scopes, run `account re-auth` to obtain a new access token that includes the updated scopes.

```
USAGE:
    zapi-cli scope add [OPTIONS]

OPTIONS:
    --scope <SCOPE>          OAuth scope(s) to add, comma-separated (required)
    -a, --account <ACCOUNT>  Account alias (uses default account if omitted)
    --port <PORT>            Local callback port for incremental OAuth flow (default: 8085)
```

Scopes are deduplicated — adding a scope that already exists is a no-op for that scope.

#### Example

```bash
zapi-cli scope add --scope "ZohoDesk.Tickets.READ,ZohoDesk.Reports.READ"
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "account": "myaccount",
    "scopes": [
      "ZohoCliq.Channels.READ",
      "ZohoDesk.Tickets.READ",
      "ZohoDesk.Reports.READ"
    ]
  }
}
```

> **Note:** After `scope add`, run `account re-auth --name <ACCOUNT>` to obtain a new access token that includes the updated scopes. The new token is stored silently using the stored `client_id` and `client_secret`.

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `INVALID_ARGS` | `--scope` was not provided or is empty. | Pass `--scope <SCOPE>`. |
| `ACCOUNT_NOT_FOUND` | The target account does not exist. | Run `account list` to verify. |
| `ACCOUNT_DOMAIN_BLOCKED` | The account email is `@zohocorp.*`. | Use a customer Zoho account. |
| `NO_DEFAULT_ACCOUNT` | `--account` omitted and no default account is set. | Run `account set-default` or pass `--account` explicitly. |

---

### scope list

List all OAuth scopes configured for an account.

```
USAGE:
    zapi-cli scope list [OPTIONS]

OPTIONS:
    -a, --account <ACCOUNT>  Account alias (uses default account if omitted)
```

#### Example

```bash
zapi-cli scope list --account myaccount
```

#### Example output

```json
[
  "ZohoCliq.Channels.READ",
  "ZohoDesk.Tickets.READ",
  "ZohoDesk.Reports.READ"
]
```

> **Note:** Output is a plain JSON array of strings — no `{"status":"ok","data":...}` wrapper.

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `ACCOUNT_NOT_FOUND` | The target account does not exist. | Run `account list` to verify. |
| `ACCOUNT_DOMAIN_BLOCKED` | The account email is `@zohocorp.*`. | Use a customer Zoho account. |
| `NO_DEFAULT_ACCOUNT` | `--account` omitted and no default account is set. | Run `account set-default` or pass `--account` explicitly. |

---

### util time-ms

Output the current UTC time as a Unix millisecond timestamp.

```
USAGE:
    zapi-cli util time-ms [OPTIONS]
```

Useful for constructing time-range query parameters for Zoho APIs that accept Unix millisecond timestamps.

#### Example

```bash
zapi-cli util time-ms
```

#### Example output

```json
{
  "ts": 1710789600000
}
```

#### Usage in a pipeline

```bash
# Capture the timestamp into a shell variable
TS=$(zapi-cli util time-ms | jq -r '.ts')
echo "Current time: $TS ms"
```

---

### util uuid

Generate a random UUID v4.

```
USAGE:
    zapi-cli util uuid [OPTIONS]
```

Useful for generating idempotency keys or unique identifiers when constructing API request bodies.

#### Example

```bash
zapi-cli util uuid
```

#### Example output

```json
{
  "uuid": "550e8400-e29b-41d4-a716-446655440000"
}
```

#### Usage in a pipeline

```bash
IDEMPOTENCY_KEY=$(zapi-cli util uuid | jq -r '.uuid')
zapi-cli api call \
  --url "https://www.zohoapis.com/crm/v6/Leads" \
  -X POST \
  --header "Idempotency-Key:$IDEMPOTENCY_KEY" \
  --body '{"data": [{"Last_Name": "Smith", "First_Name": "John"}]}'
```

---

### util time-now

Output the current India Standard Time (IST, GMT+5:30) as a formatted timestamp.

```
USAGE:
    zapi-cli util time-now [OPTIONS]
```

The time is formatted as `DD/MM/YY HH:mm:ss.fff` (24-hour clock with milliseconds). Uses a fixed +05:30 offset — no OS timezone database dependency.

#### Example

```bash
zapi-cli util time-now
```

#### Example output

```json
{
  "now": "20/03/26 21:15:30.427"
}
```

#### Usage in a pipeline

```bash
NOW_IST=$(zapi-cli util time-now | jq -r '.now')
echo "Current IST time: $NOW_IST"
```

---

### trace session start

Start a named trace session. All subsequent `api call` invocations will write trace entries live to the resolved export file until the session is closed.

```
USAGE:
    zapi-cli trace session start [OPTIONS]

OPTIONS:
    --name <NAME>          Session name (required; allowed chars: [a-zA-Z0-9_.-], max 64)
    --export-path <PATH>   File or directory path for trace output (optional)
```

**Export path resolution order:**
1. If `--export-path` ends in `.json` → used as the exact file path.
2. If `--export-path` is a directory → file is created as `<name>-<short-uuid>.json` in that directory.
3. If `--export-path` is omitted → falls back to the default configured via `trace config set`.
4. If no export path is available → `EXPORT_PATH_NOT_SET` error, exit 1.

Session names are **non-unique** — multiple sessions may share the same name. The `unique_id` UUID is the primary key for all session operations.

#### Example

```bash
zapi-cli trace session start --name my-session --export-path /tmp/traces/
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "unique_id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "my-session",
    "export_path": "/tmp/traces/my-session-550e8400.json",
    "start_time": "2026-03-20T15:30:00.000+00:00",
    "status": "active"
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `INVALID_ARGS` | `--name` is missing or contains invalid characters. | Name must match `[a-zA-Z0-9_.-]` and be at most 64 characters. |
| `EXPORT_PATH_NOT_SET` | No `--export-path` given and no default is configured. | Run `trace config set --default-export-path <PATH>` or pass `--export-path` explicitly. |

---

### trace session list

List all known trace sessions with their current status and entry counts.

```
USAGE:
    zapi-cli trace session list [OPTIONS]
```

#### Example

```bash
zapi-cli trace session list
```

#### Example output

```json
[
  {
    "unique_id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "my-session",
    "start_time": "2026-03-20T15:30:00.000+00:00",
    "entry_count": 12,
    "status": "active",
    "export_path": "/tmp/traces/my-session-550e8400.json"
  }
]
```

**Session status values:**

| Status | Meaning |
|---|---|
| `active` | Session is live; `api call` writes trace entries. |
| `closing` | Session is draining in-flight writes; new entries are silently dropped. |
| `closed` | Session is sealed; no further entries are written. |

---

### trace session export

Read and return the entries from a trace file, with optional type filtering and body truncation.

```
USAGE:
    zapi-cli trace session export [OPTIONS]

OPTIONS:
    --id <ID>                Session UUID (mutually exclusive with --name)
    --name <NAME>            Session name (mutually exclusive with --id)
    --type <TYPE>            Filter entries by type: api or pex
    --truncate-body <CHARS>  Truncate request_body and response_body to N characters in output
```

Either `--id` or `--name` is required. If `--name` matches multiple sessions, `SESSION_AMBIGUOUS` is returned — use `--id` to disambiguate.

> **Note:** `--truncate-body` affects **output only**. The trace file on disk is never modified.

> **Security:** The `authorization`, `cookie`, `x-auth-token`, and `x-api-key` request headers, and the `set-cookie` and `www-authenticate` response headers, are **never written to the trace file**.

#### Example

```bash
zapi-cli trace session export --id 550e8400-e29b-41d4-a716-446655440000 --type api --truncate-body 200
```

#### Example output

```json
[
  {
    "seq": 1,
    "type": "api",
    "session": "my-session",
    "session_id": "550e8400-e29b-41d4-a716-446655440000",
    "timestamp": "2026-03-20T15:30:05.123+00:00",
    "duration_ms": 312,
    "account": "myaccount",
    "method": "GET",
    "base_url": "https://www.zohoapis.com",
    "url": "https://www.zohoapis.com/cliq/v2/channels",
    "request_headers": { "Content-Type": "application/json" },
    "request_body": null,
    "response_status": 200,
    "response_headers": { "Content-Type": "application/json" },
    "response_body": "{\"channels\":[...]}",
    "error": null
  }
]
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `SESSION_NOT_FOUND` | No session with the specified `--id` or `--name` exists. | Run `trace session list` to see available sessions. |
| `SESSION_AMBIGUOUS` | Multiple sessions share the specified `--name`. | Use `--id` with the specific `unique_id` from `trace session list`. |
| `INVALID_ARGS` | Both `--id` and `--name` provided, or neither, or `--type` is not `api`/`pex`. | Provide exactly one of `--id` or `--name`. |

---

### trace session close

Seal a trace session. In-flight `api call` writes are drained for `--wait-ms` milliseconds, then the session is marked `closed` and no further entries are accepted.

```
USAGE:
    zapi-cli trace session close [OPTIONS]

OPTIONS:
    --id <ID>        Session UUID (mutually exclusive with --name)
    --name <NAME>    Session name (mutually exclusive with --id)
    --wait-ms <MS>   Drain window before sealing, in milliseconds (default: 5000)
```

#### Example

```bash
zapi-cli trace session close --id 550e8400-e29b-41d4-a716-446655440000
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "unique_id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "my-session"
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `SESSION_NOT_FOUND` | No session with that `--id` or `--name` exists. | Run `trace session list` to see available sessions. |
| `SESSION_AMBIGUOUS` | Multiple sessions share the specified `--name`. | Use `--id` with the specific `unique_id` instead. |

---

### trace session reopen

Re-activate a closed session. Subsequent `api call` invocations append entries to the existing trace file, with sequence numbers continuing from the last `entry_count`.

```
USAGE:
    zapi-cli trace session reopen [OPTIONS]

OPTIONS:
    --id <ID>        Session UUID (mutually exclusive with --name)
    --name <NAME>    Session name (mutually exclusive with --id)
```

#### Example

```bash
zapi-cli trace session reopen --id 550e8400-e29b-41d4-a716-446655440000
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "unique_id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "my-session",
    "status": "active"
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `SESSION_NOT_FOUND` | No session with that `--id` or `--name` exists. | Run `trace session list` to see available sessions. |
| `SESSION_AMBIGUOUS` | Multiple sessions share the specified `--name`. | Use `--id` with the specific `unique_id` instead. |

---

### trace session remove

Remove a session from the sessions index. The trace file at `export_path` is **preserved** on disk.

```
USAGE:
    zapi-cli trace session remove [OPTIONS]

OPTIONS:
    --id <ID>        Session UUID (mutually exclusive with --name)
    --name <NAME>    Session name (mutually exclusive with --id)
```

> **Note:** This only removes the session metadata entry. The trace file (NDJSON) at `export_path` is not deleted.

#### Example

```bash
zapi-cli trace session remove --id 550e8400-e29b-41d4-a716-446655440000
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "unique_id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "my-session"
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `SESSION_NOT_FOUND` | No session with that `--id` or `--name` exists. | Run `trace session list` to see available sessions. |
| `SESSION_AMBIGUOUS` | Multiple sessions share the specified `--name`. | Use `--id` with the specific `unique_id` instead. |

---

### trace config set

Persist the default export path used when `trace session start` is called without `--export-path`.

```
USAGE:
    zapi-cli trace config set [OPTIONS]

OPTIONS:
    --default-export-path <PATH>    Directory or file path for trace output (required)
```

This value is stored in `trace-config.json` in the zapi-cli config directory.

#### Example

```bash
zapi-cli trace config set --default-export-path /tmp/traces/
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "default_export_path": "/tmp/traces/"
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `INVALID_ARGS` | `--default-export-path` was not provided. | Pass `--default-export-path <PATH>`. |

---

### trace config show

Show the current trace configuration.

```
USAGE:
    zapi-cli trace config show [OPTIONS]
```

#### Example

```bash
zapi-cli trace config show
```

#### Example output

```json
{
  "default_export_path": "/tmp/traces/"
}
```

> **Note:** Output is plain JSON — no `{"status":"ok","data":...}` wrapper. `default_export_path` is `null` if no default has been configured.

---

### config set env-file

Persist the absolute path to a `.env` file that is automatically loaded at every CLI startup. Run this once — the path is stored in `cli-settings.json` in the platform config directory. Once set, all commands (including `account login`) will pick up `ZOHO_CLIENT_ID`, `ZOHO_CLIENT_SECRET`, and any other variables from that file without needing to export them or pass flags.

OS environment variables always take precedence over values in the `.env` file.

For a guide on creating and populating the `.env` file see [docs/zoho-mobile-app-setup.md](zoho-mobile-app-setup.md#step-3--configure-your-environment).

```
USAGE:
    zapi-cli config set env-file <PATH>

ARGUMENTS:
    <PATH>    Absolute or relative path to a .env file (must exist)
```

#### Example

```bash
zapi-cli config set env-file /home/user/projects/zapi-cli/.env
```

#### Example output (stdout)

```json
{"status":"ok","data":{"env_file":"/home/user/projects/zapi-cli/.env"}}
```

The path is resolved to an absolute path before being stored.

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `INVALID_ARGS` | The file at `<PATH>` does not exist. | Create the file first (e.g., `cp .env.example .env`). |

---

### config set scope-file

Persist the absolute path to a scope file that is automatically read when `account login` resolves scopes. Run this once — the path is stored in `cli-settings.json`. Each line (or comma-separated entry) in the file is treated as an OAuth scope. Lines starting with `#` are treated as comments.

`AaaServer.profile.READ` is always included automatically — you do not need to add it to your scope file.

```
USAGE:
    zapi-cli config set scope-file <PATH>

ARGUMENTS:
    <PATH>    Absolute or relative path to a scope file (file need not exist at configuration time)
```

#### Example command

```bash
zapi-cli config set scope-file /home/user/projects/zapi-cli/scopes.txt
```

#### Example output (stdout)

```json
{"status":"ok","data":{"scope_file":"/home/user/projects/zapi-cli/scopes.txt"}}
```

#### Scope file format

The file contents can be in any of these formats (scopes must **not** have quotes):

**Format 1: Comma-separated on one line**
```
ZohoCliq.Chats.READ, ZohoCliq.Messages.READ, ZohoCliq.Channels.CREATE, ZohoCliq.Designations.ALL
```

**Format 2: One scope per line**
```
ZohoCliq.Chats.READ
ZohoCliq.Messages.READ
ZohoCliq.Channels.CREATE
ZohoCliq.Designations.ALL
```

**Format 3: Mixed with comments**
```
# Cliq communication scopes
ZohoCliq.Chats.READ, ZohoCliq.Messages.READ

# Channel and designation scopes
ZohoCliq.Channels.CREATE
ZohoCliq.Designations.ALL
```

The path is resolved to an absolute path before being stored. The file does not need to exist at configuration time — it is read when `account login` runs.

---

### config set app-dir

Persist a custom application data directory where `accounts.json` is stored. By default, the platform config directory is used. Use this to store account data in a project-specific location.

The directory is created if it does not already exist.

If an `accounts.json` file exists in the current app data directory and is absent in the new directory, it is automatically copied to the new location. The original file is preserved. If `accounts.json` already exists in the new directory, no migration occurs and the existing file is kept.

The `migrated` field in the response indicates whether a file copy was performed.

```
USAGE:
    zapi-cli config set app-dir <PATH>

ARGUMENTS:
    <PATH>    Absolute or relative path to the desired app data directory
```

#### Example

```bash
zapi-cli config set app-dir /home/user/projects/myproject/.zapi-cli
```

#### Example output (stdout, with migration)

```json
{"status":"ok","data":{"app_data_dir":"/home/user/projects/myproject/.zapi-cli","migrated":true}}
```

#### Example output (stdout, no migration)

```json
{"status":"ok","data":{"app_data_dir":"/home/user/projects/myproject/.zapi-cli","migrated":false}}
```

---

### config show

Show the current persisted CLI configuration.

```
USAGE:
    zapi-cli config show [OPTIONS]
```

#### Example

```bash
zapi-cli config show
```

#### Example output

```json
{
  "env_file": "/home/user/projects/zapi-cli/.env",
  "scope_file": "/home/user/projects/zapi-cli/scopes.txt",
  "app_data_dir": null,
  "trace_default_export_path": null
}
```

> **Note:** Output is plain JSON — no `{"status":"ok","data":...}` wrapper. Fields are `null` if not configured.

---

## Global Flags

These flags are accepted by all subcommands:

| Flag | Type | Description |
|---|---|---|
| `-a, --account <ACCOUNT>` | string | Override the default account for this invocation only |
| `--json` | boolean | Force JSON output mode (useful if a subcommand has non-JSON output) |
| `--no-input` | boolean | Disable all interactive prompts; fail instead of prompting |
| `-h, --help` | boolean | Print help for the current command |

---

## Scripting & AI Agent Usage

zapi-cli is designed to be a first-class citizen in automated pipelines and AI agent contexts. All output is JSON, all errors are structured, and all exit codes are deterministic.

### Checking exit code and parsing output

```bash
# Run a command and check success
if zapi-cli account list > /tmp/accounts.json; then
  cat /tmp/accounts.json | jq '.data.accounts[].name'
else
  # Read the structured error from stderr
  echo "Command failed"
fi
```

### Capturing data fields with jq

```bash
# Get all account names
zapi-cli account list | jq -r '.data.accounts[].name'

# Get default account name
zapi-cli account list | jq -r '.data.accounts[] | select(.is_default == true) | .name'

# Make an API call and extract a specific field
zapi-cli api call \
  --url "https://www.zohoapis.com/cliq/v2/channels" \
  -X GET \
  | jq '.data.channels[].name'
```

### Handling errors in scripts

```bash
OUTPUT=$(zapi-cli api call --url "https://www.zohoapis.com/cliq/v2/channels" -X GET 2>/tmp/err.json)
EXIT_CODE=$?

if [ $EXIT_CODE -eq 0 ]; then
  echo "$OUTPUT" | jq '.data'
elif [ $EXIT_CODE -eq 2 ]; then
  # Auth failure — attempt re-auth
  ERROR_CODE=$(cat /tmp/err.json | jq -r '.code')
  ACCOUNT=$(cat /tmp/err.json | jq -r '.account // "myaccount"')
  zapi-cli account re-auth --name "$ACCOUNT"
  # Retry the original command
  zapi-cli api call --url "https://www.zohoapis.com/cliq/v2/channels" -X GET
else
  echo "Error: $(cat /tmp/err.json | jq -r '.error')"
  exit 1
fi
```

### Redirecting stderr

Because errors go to stderr and results go to stdout, you can cleanly separate them:

```bash
# Capture stdout only (ignore stderr)
RESULT=$(zapi-cli api call --url "..." -X GET 2>/dev/null)

# Capture stderr only (for error inspection)
ERROR=$(zapi-cli api call --url "..." -X GET 2>&1 >/dev/null)

# Capture both to separate files
zapi-cli api call --url "..." -X GET >result.json 2>error.json
```

### Non-interactive mode (AI agents)

Pass `--no-input` to ensure the CLI never blocks waiting for keyboard input. This is critical in headless agent contexts.

```bash
zapi-cli --no-input account list
zapi-cli --no-input api call --url "..." -X GET --account myaccount
```

### Recommended pattern for AI agents

```bash
# 1. Verify the account exists and is healthy
ACCOUNT_STATUS=$(zapi-cli account show --name myaccount 2>/dev/null)
if [ $? -ne 0 ]; then
  echo "Account not found or error. Aborting." >&2
  exit 1
fi

# 2. Make the API call
zapi-cli api call \
  --url "https://www.zohoapis.com/cliq/v2/channels" \
  -X GET \
  --account myaccount
```

### Chaining util commands

```bash
# Build a time-windowed API query
NOW=$(zapi-cli util time-ms | jq -r '.ts')
ONE_HOUR_AGO=$((NOW - 3600000))

zapi-cli api call \
  --url "https://www.zohoapis.com/cliq/v2/search" \
  -X GET \
  --query "from=$ONE_HOUR_AGO" \
  --query "to=$NOW"
```

---

## Error Handling Reference

All error responses are emitted on **stderr** as:

```json
{ "error": "<message>", "code": "<CODE>", "exitCode": <n> }
```

### Complete error code table

| Code | Exit | Meaning | Recommended action |
|---|---|---|---|
| `ACCOUNT_NOT_FOUND` | 1 | The named account does not exist in local storage. | Run `account list` to enumerate valid account names. |
| `ACCOUNT_ALREADY_EXISTS` | 1 | An account with that `--name` already exists. | Choose a different name or remove the existing account first with `account remove`. |
| `DUPLICATE_IDENTIFIER` | 1 | More than one account matched the given email or ZUID. | Use `--name` to identify the account unambiguously. |
| `ACCOUNT_RENAME_FAILED` | 1 | The keychain entry could not be renamed after accounts.json was updated. | The account is saved under the new name; manually remove and re-add if the keychain is inconsistent. |
| `NO_DEFAULT_ACCOUNT` | 1 | No `--account` flag was provided and no default account has been set. | Run `account set-default --name <ACCOUNT>` or pass `--account` explicitly. |
| `AUTH_FAILURE` | 2 | OAuth token exchange or refresh request failed. | Verify `client-id`, `client-secret`, and the grant code. Re-add the account if the issue persists. |
| `NEEDS_REAUTH` | 2 | The account's refresh token has expired or been revoked and needs re-authentication. | Run `account re-auth --name <ACCOUNT>`. If that fails with `AUTH_FAILURE`, re-add the account. |
| `API_ERROR` | 1 | Zoho API returned a non-2xx HTTP status code. | Inspect the `data` field in the error JSON for the Zoho error body. |
| `INVALID_ARGS` | 1 | A required flag is missing or a flag value is in an invalid format. | Check the `error` field for which flag is missing. Run `--help` on the subcommand. |
| `IO_ERROR` | 1 | A file read or write operation failed (e.g., `--body-file` path not found). | Verify that the file path exists and is readable. |
| `KEYCHAIN_ERROR` | 1 | The OS keychain could not be read from or written to. | Check OS keychain permissions. On Linux, verify libsecret / GNOME Keyring is running. |
| `ACCOUNT_DOMAIN_BLOCKED` | 1 | The authenticated email address is `@zohocorp.*`. | Only customer Zoho accounts are permitted. Use a non-Zoho-corp account. |
| `EMAIL_REQUIRED` | 1 | Zoho did not return an email address in the token or userinfo response. | Ensure your OAuth scopes include the necessary user profile permissions. |
| `HOST_NOT_ALLOWED` | 1 | The `--url` hostname is not on the Zoho domain allowlist. | Only URLs under `zoho.com`, `zoho.eu`, `zoho.in`, `zoho.com.au`, `zohoapis.com`, and `zohoapis.in` are accepted. |
| `STATE_MISMATCH` | 1 | The OAuth callback `state` parameter did not match the generated CSRF token. | Indicates a possible CSRF attack or a stale/replayed callback. Discard and re-run `account login`. |
| `LOGIN_TIMEOUT` | 1 | The browser-based OAuth callback was not received within 120 seconds. | Ensure the browser opened and you completed the sign-in before the timeout. Re-run `account login`. |
| `ENV_FILE_NOT_CONFIGURED` | 1 | `ZOHO_CLIENT_ID` is not set in the environment or configured env-file. | Run `zapi-cli config set env-file <path>` or export `ZOHO_CLIENT_ID`. |
| `SCOPE_FILE_NOT_CONFIGURED` | 1 | No scopes resolved from `--scope` or the configured scope-file. | Pass `--scope <SCOPES>` or run `zapi-cli config set scope-file <path>`. |
| `INTERNAL_ERROR` | 1 | An unhandled internal exception occurred. | File a bug report with the full stderr output. |
| `SESSION_NOT_FOUND` | 1 | The specified session `--id` or `--name` does not exist in the sessions index. | Run `trace session list` to enumerate valid sessions. |
| `SESSION_AMBIGUOUS` | 1 | Multiple sessions share the specified `--name`; cannot resolve to a unique session. | Use `--id` with the specific `unique_id` from `trace session list`. |
| `EXPORT_PATH_NOT_SET` | 1 | `trace session start` had no `--export-path` and no default is configured. | Run `trace config set --default-export-path <PATH>` or pass `--export-path` explicitly. |
| `REGISTRY_ENTRY_NOT_FOUND` | 1 | The specified `--id` does not match any entry in the local API registry. | Run `api registry list` to enumerate valid ids. |
| `REGISTRY_ENTRY_ALREADY_EXISTS` | 1 | `api registry add` was called with an `--id` that already exists in the registry. | Use `api registry update --id <ID>` to modify the existing entry, or choose a different id. |

---

## Security

### Account domain block

All `@zohocorp.*` email addresses are hard-blocked at every entry point — including `account login` and `account re-auth`. This is a compile-time policy that cannot be overridden at runtime.

### Token storage

Tokens (access token + refresh token) are stored **exclusively in the OS keychain**:
- **macOS:** macOS Keychain Services.
- **Windows:** Windows Credential Manager.
- **Linux:** libsecret (GNOME Keyring), with automatic fallback to an AES-256-GCM encrypted file at `~/.config/zapi-cli/credentials.enc`.

Tokens are **never written to disk in plaintext** and are **never printed to stdout or stderr**.

### Token masking

The `account show` command never emits credential fields (`access_token`, `client_id`, `client_secret`) in its output. This is a hard guarantee — real token values are never printed to stdout or stderr under any circumstances.

### Host allowlist

Outgoing HTTP requests from `api call` are restricted to the following domain suffixes. This list is **compile-time fixed** and cannot be overridden with flags or environment variables:

- `zoho.com`
- `zoho.eu`
- `zoho.in`
- `zoho.com.au`
- `zohoapis.com`
- `zohoapis.in`

Any other host results in a `HOST_NOT_ALLOWED` error (exit code 1) before any network connection is made.

### CSRF protection

The `account login` browser flow generates a cryptographically random state token for every invocation. The callback handler validates this token before exchanging the grant code. A mismatch raises `STATE_MISMATCH` and aborts the flow.

### No secret logging

The tool never writes OAuth secrets, access tokens, or refresh tokens to stdout, stderr, log files, or environment variables.

---

## Platform Notes

### macOS

- **Keychain:** macOS Keychain Services via the `security` framework.
- **Config directory:** `~/Library/Application Support/zapi-cli/`
- **Browser launch:** `open` command is used to open the authorization URL.

### Windows

- **Keychain:** Windows Credential Manager (`DPAPI`-backed).
- **Config directory:** `%APPDATA%\zapi-cli\` (typically `C:\Users\<user>\AppData\Roaming\zapi-cli\`)
- **Browser launch:** `start` command is used to open the authorization URL.

### Linux

- **Keychain:** libsecret (requires GNOME Keyring or compatible secret service). If unavailable, automatically falls back to an AES-256-GCM encrypted credentials file at `~/.config/zapi-cli/credentials.enc`.
- **Config directory:** `~/.config/zapi-cli/`
- **Browser launch:** `xdg-open` is used to open the authorization URL. Ensure a desktop environment or browser is accessible when running `account login` on Linux.

> **Headless Linux note:** On headless servers without a display, `account login` cannot open a browser. Use `account add` (Self-Client grant code) instead, which does not require a browser.

### Binary naming

| Platform | Binary |
|---|---|
| macOS, Linux | `zapi-cli` |
| Windows | `zapi-cli.exe` |

All examples in this document use `zapi-cli`. Substitute `zapi-cli.exe` on Windows.
