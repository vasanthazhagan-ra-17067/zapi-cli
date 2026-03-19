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
   - [account add](#account-add)
   - [account login](#account-login)
   - [account list](#account-list)
   - [account show](#account-show)
   - [account set-default](#account-set-default)
   - [account remove](#account-remove)
   - [account re-auth](#account-re-auth)
   - [api call](#api-call)
   - [util time-ms](#util-time-ms)
   - [util uuid](#util-uuid)
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

Before making any API calls you need to add at least one account. See [Authentication Overview](#authentication-overview) for choosing the right method.

```bash
# Option A — Browser login (recommended for interactive setup)
zapi-cli account login --file templates/account-login.json

# Option B — Self-Client grant code (recommended for CI/CD)
zapi-cli account add \
  --name myaccount \
  --code <GRANT_CODE> \
  --client-id 1000.EXAMPLE_CLIENT_ID \
  --client-secret <CLIENT_SECRET> \
  --dc us
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

zapi-cli supports two OAuth methods. Both use Zoho's standard OAuth 2.0 Authorization Code flow. The difference is in how the grant code is obtained.

### Method 1 — Self-Client (recommended for automation)

Use `account add` when you:
- Are setting up a CI/CD pipeline or headless server.
- Are scripting account registration non-interactively.
- Already have a grant code from the Zoho Developer Console.

**How to get a grant code:**
1. Go to [https://api-console.zoho.com](https://api-console.zoho.com).
2. Open your application and switch to the **Self-Client** tab.
3. Select the OAuth scopes you need and generate a One-Time Code.
4. Copy the code — it expires in 10 minutes.

```bash
zapi-cli account add \
  --name myaccount \
  --code <GRANT_CODE> \
  --client-id 1000.EXAMPLE_CLIENT_ID \
  --client-secret <CLIENT_SECRET> \
  --dc us
```

### Method 2 — Browser Login (recommended for interactive use)

Use `account login` when you:
- Are setting up an account on a developer workstation for the first time.
- Want a guided browser-based OAuth flow.
- Do not want to manually copy grant codes.

> ⚠️ **Warning:** Before running `account login`, you **must** register the redirect URI `http://localhost:8085/callback` in your Zoho Developer Console. See [account login](#account-login) for full setup instructions.

```bash
zapi-cli account login \
  --name myaccount \
  --client-id 1000.EXAMPLE_CLIENT_ID \
  --client-secret <CLIENT_SECRET> \
  --scope "ZohoCliq.Channels.READ,ZohoCliq.Messages.CREATE" \
  --dc us
```

### Token storage

All tokens (access token + refresh token) are stored exclusively in the OS keychain. They are never written to disk in plaintext. See [Platform Notes](#platform-notes) for keychain locations per OS.

### Token refresh

Access tokens are automatically refreshed when:
- The stored token has expired and `needs_reauth` is set.
- A `401 Unauthorized` response is received from Zoho.

---

## Datacenters

The `--dc` flag is accepted by `account add` and `account login`. Use the value matching the datacenter where your Zoho organization was registered.

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

**Default:** `us`

---

## Command Reference

### account add

Add a new Zoho account using an OAuth Self-Client grant code.

```
USAGE:
    zapi-cli account add [OPTIONS]

OPTIONS:
    --name <NAME>                      Account alias (unique identifier)
    --code <CODE>                      OAuth grant code from Zoho Developer Console Self-Client
    --client-id <CLIENT_ID>            OAuth client ID
    --client-secret <CLIENT_SECRET>    OAuth client secret
    --redirect-uri <REDIRECT_URI>      Redirect URI (default: https://www.zoho.com)
    --dc <DC>                          Datacenter (default: us)
```

**All flags are required** except `--redirect-uri` (defaults to `https://www.zoho.com`) and `--dc` (defaults to `us`). The `--redirect-uri` value must match what is registered in your Zoho Developer Console app exactly.

**Grant code expiry:** The code generated from the Self-Client tab is valid for **10 minutes**. Run `account add` promptly after copying the code.

#### Example

```bash
zapi-cli account add \
  --name myaccount \
  --code 1000.abc123def456 \
  --client-id 1000.EXAMPLE_CLIENT_ID \
  --client-secret abc123xyz789 \
  --dc us
```

#### Example output (stdout)

```json
{
  "status": "ok",
  "data": {
    "name": "myaccount",
    "email": "user@example.com",
    "dc": "us",
    "is_default": false
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `ACCOUNT_ALREADY_EXISTS` | An account named `myaccount` already exists. | Choose a different `--name` or remove the existing account first. |
| `AUTH_FAILURE` | Grant code was invalid, expired, or already used. | Generate a new grant code from the Self-Client tab and retry within 10 minutes. |
| `ACCOUNT_DOMAIN_BLOCKED` | The authenticated email is `@zohocorp.*`. | zapi-cli blocks Zoho employee accounts by design. Use a customer account. |
| `EMAIL_REQUIRED` | Zoho did not return an email in the token response. | Ensure your OAuth scopes include user-profile access. |

---

### account login

Authenticate a new account via a browser-based OAuth redirect flow.

```
USAGE:
    zapi-cli account login [OPTIONS]

OPTIONS:
    --file <FILE>                      Path to a JSON config file
    --name <NAME>                      Account alias
    --client-id <CLIENT_ID>            OAuth client ID
    --client-secret <CLIENT_SECRET>    OAuth client secret
    --scope <SCOPE>                    Comma-separated OAuth scopes
    --dc <DC>                          Datacenter (default: us)
    --port <PORT>                      Local callback port (default: 8085)
```

Flags provided on the command line override corresponding fields in the `--file` config. For example, `--file login.json --name staging` uses all values from the file but replaces the `name` field with `"staging"`.

---

> ⚠️ **Required Setup — Redirect URI Registration**
>
> The redirect URI `http://localhost:8085/callback` **must be registered** in your Zoho Developer Console application **before** running this command. If it is not registered, Zoho will return an "Invalid Redirect Uri" error and the login will fail.
>
> **Steps to register the redirect URI:**
> 1. Go to [https://api-console.zoho.com](https://api-console.zoho.com).
> 2. Open the application you are using (e.g., your Self-Client app).
> 3. Navigate to the **Settings** or **Redirect URIs** section.
> 4. Add `http://localhost:8085/callback` to the list of allowed redirect URIs.
> 5. Save the changes.
>
> If you use a custom port via `--port 9090`, register `http://localhost:9090/callback` instead.

---

#### How the flow works

1. zapi-cli starts a local HTTP server listening on `http://localhost:{PORT}/callback`.
2. It generates a random CSRF state token.
3. It builds the Zoho OAuth authorization URL with your `client-id`, `scope`, datacenter, and state token.
4. It prints the authorization URL to **stderr** and opens your system browser to that URL.
5. You authenticate in the browser. Zoho redirects back to `http://localhost:{PORT}/callback?code=...&state=...`.
6. zapi-cli validates the state token (CSRF check), then exchanges the code for access + refresh tokens.
7. It fetches your user info (email, ZUID) to validate the account identity.
8. Credentials are stored in the OS keychain and the account is persisted.
9. The local HTTP server is shut down.

The flow times out after **120 seconds** if no callback is received.

#### JSON config file template

A template is provided at `templates/account-login.json`:

```json
{
  "name": "",
  "client-id": "",
  "client-secret": "",
  "scope": ["ZohoAPI.Resource.READ"],
  "dc": "us"
}
```

Fill in the values and run:

```bash
zapi-cli account login --file templates/account-login.json
```

#### Example (inline flags)

```bash
zapi-cli account login \
  --name myaccount \
  --client-id 1000.EXAMPLE_CLIENT_ID \
  --client-secret abc123xyz789 \
  --scope "ZohoCliq.Channels.READ,ZohoCliq.Messages.CREATE" \
  --dc us \
  --port 8085
```

#### Example (file with name override)

```bash
zapi-cli account login --file login.json --name staging
```

#### Example output (stdout)

```json
{
  "status": "ok",
  "data": {
    "name": "myaccount",
    "email": "user@example.com",
    "dc": "us",
    "is_default": false
  }
}
```

#### Stderr during flow

While the browser window is open, informational lines are printed to stderr (not stdout):
```
Authorization URL: https://accounts.zoho.com/oauth/v2/auth?...
Waiting for browser callback on http://localhost:8085/callback ...
```

These are for human visibility only. AI agents and scripts should ignore stderr during this command and check stdout + exit code after completion.

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `LOGIN_TIMEOUT` | Browser callback not received within 120 seconds. | Ensure the browser opened and you completed sign-in. Re-run the command. |
| `STATE_MISMATCH` | The `state` parameter in the callback did not match. Possible CSRF. | This indicates a tampered or replayed callback. Re-run to generate a fresh state token. |
| `AUTH_FAILURE` | Token exchange with Zoho failed. | Check that `client-id`, `client-secret`, and `redirect-uri` are correct. |
| `ACCOUNT_ALREADY_EXISTS` | An account with this name already exists. | Remove the existing account or use a different `--name`. |
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
        "is_default": true,
        "needs_reauth": false
      },
      {
        "name": "eu-staging",
        "email": "user@example.com",
        "dc": "eu",
        "is_default": false,
        "needs_reauth": false
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
| `needs_reauth` | boolean | Whether the token needs to be refreshed via `re-auth` |

---

### account show

Show full details for a single account. The access token is always masked.

```
USAGE:
    zapi-cli account show [OPTIONS]

OPTIONS:
    --name <NAME>    Account alias to show (uses default account if omitted)
```

#### Example

```bash
zapi-cli account show --name myaccount
```

#### Example output

```json
{
  "status": "ok",
  "data": {
    "name": "myaccount",
    "email": "user@example.com",
    "dc": "us",
    "is_default": true,
    "needs_reauth": false,
    "access_token": "***",
    "client_id": "1000.EXAMPLE_CLIENT_ID"
  }
}
```

> **Note:** The `access_token` field is always shown as `"***"` regardless of context. This is a hard guarantee — the real token value is never emitted to stdout.

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `ACCOUNT_NOT_FOUND` | No account with that name exists. | Run `account list` to see available accounts. |
| `NO_DEFAULT_ACCOUNT` | `--name` was omitted and no default is set. | Run `account set-default` or pass `--name` explicitly. |

---

### account set-default

Set the default account used when `--account` is not specified.

```
USAGE:
    zapi-cli account set-default [OPTIONS]

OPTIONS:
    --name <NAME>    Account alias to make the default
```

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
| `INVALID_ARGS` | `--name` was not provided. | Pass `--name <ACCOUNT>`. |

---

### account remove

Remove an account, revoke its OAuth token, and delete it from the keychain.

```
USAGE:
    zapi-cli account remove [OPTIONS]

OPTIONS:
    --name <NAME>    Account alias to remove
```

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
| `ACCOUNT_NOT_FOUND` | No account with that name exists. | Run `account list` to verify. |
| `KEYCHAIN_ERROR` | Failed to delete credentials from the OS keychain. | Check keychain permissions and retry. |

---

### account re-auth

Re-authenticate an existing account using its stored `client-id` and `client-secret` to obtain a fresh access token.

```
USAGE:
    zapi-cli account re-auth [OPTIONS]

OPTIONS:
    --name <NAME>    Account alias to re-authenticate (uses default if omitted)
```

Use this command when `needs_reauth: true` is shown in `account list` or `account show`, or when an `api call` returns a `NEEDS_REAUTH` error code.

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
    "name": "myaccount",
    "needs_reauth": false
  }
}
```

#### Common errors

| Error code | Cause | Resolution |
|---|---|---|
| `ACCOUNT_NOT_FOUND` | The named account does not exist. | Run `account list` to verify. |
| `AUTH_FAILURE` | Token refresh failed — refresh token may be expired or revoked. | Run `account remove` then re-add the account via `account add` or `account login`. |
| `KEYCHAIN_ERROR` | Could not read credentials from the OS keychain. | Check keychain permissions. |

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

**Token refresh is automatic.** If `needs_reauth` is set for the account, or if a `401` response is received, the token is refreshed transparently before the request is retried.

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
| `AUTH_FAILURE` | Token refresh failed during automatic retry. | Re-add the account via `account add` or `account login`. |
| `ACCOUNT_NOT_FOUND` | The account specified with `--account` does not exist. | Run `account list` to verify. |
| `NO_DEFAULT_ACCOUNT` | No account specified and no default set. | Run `account set-default --name <ACCOUNT>`. |

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
  "status": "ok",
  "data": {
    "ts": 1710789600000
  }
}
```

#### Usage in a pipeline

```bash
# Capture the timestamp into a shell variable
TS=$(zapi-cli util time-ms | jq -r '.data.ts')
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
  "status": "ok",
  "data": {
    "uuid": "550e8400-e29b-41d4-a716-446655440000"
  }
}
```

#### Usage in a pipeline

```bash
IDEMPOTENCY_KEY=$(zapi-cli util uuid | jq -r '.data.uuid')
zapi-cli api call \
  --url "https://www.zohoapis.com/crm/v6/Leads" \
  -X POST \
  --header "Idempotency-Key:$IDEMPOTENCY_KEY" \
  --body '{"data": [{"Last_Name": "Smith", "First_Name": "John"}]}'
```

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

NEEDS_REAUTH=$(echo "$ACCOUNT_STATUS" | jq -r '.data.needs_reauth')
if [ "$NEEDS_REAUTH" = "true" ]; then
  zapi-cli account re-auth --name myaccount
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
NOW=$(zapi-cli util time-ms | jq -r '.data.ts')
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
| `INTERNAL_ERROR` | 1 | An unhandled internal exception occurred. | File a bug report with the full stderr output. |

---

## Security

### Account domain block

All `@zohocorp.*` email addresses are hard-blocked at every entry point — including `account add`, `account login`, and `account re-auth`. This is a compile-time policy that cannot be overridden at runtime.

### Token storage

Tokens (access token + refresh token) are stored **exclusively in the OS keychain**:
- **macOS:** macOS Keychain Services.
- **Windows:** Windows Credential Manager.
- **Linux:** libsecret (GNOME Keyring), with automatic fallback to an AES-256-GCM encrypted file at `~/.config/zapi-cli/credentials.enc`.

Tokens are **never written to disk in plaintext** and are **never printed to stdout or stderr**.

### Token masking

The `account show` command always outputs the access token as `"***"`. This cannot be disabled. Even internal debug modes do not log raw tokens.

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

The tool never writes OAuth secrets, access tokens, or refresh tokens to stdout, stderr, log files, or environment variables. The `--client-secret` flag value is consumed in-process and discarded.

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
