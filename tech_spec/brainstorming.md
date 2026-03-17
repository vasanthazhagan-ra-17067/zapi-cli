# zapi-cli — Brainstorming

> **Status:** Brainstorming complete — ready for technical spec phase.
> **Date:** 2026-03-17

---

## Project Vision

**zapi-cli** is a standalone, C# multi-platform CLI tool for interacting with **any Zoho product's REST APIs**. It manages multiple Zoho accounts, handles PAT/OAuth authentication via a pluggable interface, and exposes a general-purpose HTTP API invoker where the caller supplies the full base URL at invocation time.

**Primary consumer:** AI Agents, invoked as GitHub Copilot **CLI Skills**.

```sh
zapi-cli account add --name "work" --auth-type pat --token "myPAT"
zapi-cli account add --name "work" --auth-type oauth
zapi-cli account set-default --name "work"
zapi-cli api call --base-url "https://cliq.zoho.com/api/v2" --method GET --path "/channels"
zapi-cli api call --base-url "https://desk.zoho.com/api/v1" --method GET --path "/tickets"
zapi-cli api call --base-url "https://crm.zoho.com/crm/v5" --method POST --path "/Leads" --body '{"data":[{"Last_Name":"Doe"}]}'
```

**Why this exists:**
- AI agents need a deterministic, scriptable interface to any Zoho API without needing a browser or GUI.
- No existing tool combines Zoho auth management + general-purpose, module-agnostic API invocation in a single binary.
- Designed to be packaged as a self-contained GitHub Copilot Agent CLI Skill **and** Claude Agent Skill.

---

## Decisions & Data

### Platform

- **Targets:** Windows, macOS, Linux
- **Runtime:** .NET 10, C# 13+
- **Packaging:** Single self-contained binary (`dotnet publish -r <rid> /p:PublishSingleFile=true`)
- **Infrastructure:** Independent codebase — no shared library dependencies by default.

---

### Authentication

| Phase | Mechanism | Status |
|-------|-----------|--------|
| v1 | PAT (Personal Access Token) | Implement |
| v2 | OAuth2 via Zoho API (no browser) | Deferred |

**Design principle:** Interface-first. `IAuthProvider` abstraction — concrete implementations can be swapped without touching any command-layer code.

#### Auth Interface Contract

```csharp
public interface IAuthProvider
{
    Task<string> GetTokenAsync(string accountName);
    Task StoreTokenAsync(string accountName, string token);
    Task ClearTokenAsync(string accountName);
}

// v1
public class PatAuthProvider : IAuthProvider { ... }

// Future
public class OAuthProvider : IAuthProvider { ... }
```

#### PAT Flow (v1)

```
zapi-cli account add --name "work" --auth-type pat --token "xxx"
  → --token is required when --auth-type is pat
  → writes account metadata to accounts.json
  → stores PAT secret in OS keychain under key: zapi-cli:work:pat

zapi-cli api call --base-url "https://cliq.zoho.com/api/v2" --method GET --path "/channels"
  → resolves active account
  → fetches PAT from OS keychain via PatAuthProvider
  → injects as Authorization: Zoho-oauthtoken <token>
```

#### OAuth Flow (future)

```
zapi-cli account add --name "work" --auth-type oauth
  → --auth-type oauth triggers OAuth PKCE flow automatically; no --token input accepted
  → client-id and client-secret are compile-time constants in OAuthProvider.cs (not user input)
  → calls Zoho OAuth API entirely in terminal (no browser)
  → stores access + refresh tokens in OS keychain
  → stores scope list in accounts.json

zapi-cli scope add --account "work" --scope "ZohoDesk.Tickets.READ"
  → updates scopes[] for that account in accounts.json
  → sets needs_reauth = true on the account

zapi-cli account re-auth --name "work"
  → calls Zoho OAuth token refresh/exchange API
  → stores new tokens
  → clears needs_reauth flag
```

---

### Accounts

- **Multi-account:** Yes — multiple Zoho accounts can be configured simultaneously.
- **Active account:** Set via `zapi-cli account set-default --name "work"` — persisted in `accounts.json`.
- **Per-command override:** `--account "personal"` flag available on any command.

#### Account Metadata Fields

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | User-given alias |
| `domain` | string | Datacenter domain (`zoho.com`, `zoho.eu`, `zoho.in`, `zoho.com.au`) — used for OAuth token endpoints only; **not** used to derive API base URLs |
| `email` | string | Account email address — used for ZohoCorp domain blocking |
| `scopes` | string[] | OAuth permission strings |
| `token_type` | `pat` \| `oauth` | Auth mechanism in use |
| `is_default` | bool | Whether this is the active account |
| `needs_reauth` | bool | Set to `true` when scopes have changed and a new token is required |

> The `domain` field is used for OAuth token endpoint resolution only. It is not used to derive an API base URL — the caller supplies `--base-url` directly on each `api call` invocation.

#### `accounts.json` Example

```json
{
  "accounts": [
    {
      "name": "work",
      "domain": "zoho.com",
      "email": "user@example.com",
      "scopes": ["ZohoCliq.Channels.READ", "ZohoDesk.Tickets.WRITE"],
      "token_type": "pat",
      "is_default": true,
      "needs_reauth": false
    },
    {
      "name": "personal",
      "domain": "zoho.eu",
      "email": "user@personal.com",
      "scopes": [],
      "token_type": "pat",
      "is_default": false,
      "needs_reauth": false
    }
  ]
}
```

---

### Scopes

- Scopes are **per-account** — each account maintains its own independent scope list.
- Scopes are OAuth permission strings (e.g., `ZohoCliq.Channels.READ`, `ZohoDesk.Tickets.READ`).
- Stored per-account in `accounts.json` alongside other metadata (secrets are never stored in JSON).
- All scope commands operate on a specific account (via `--account` flag or the active/default account).
- Adding or removing a scope sets `needs_reauth = true` on **that account**.
- **PAT phase:** scope metadata is recorded but no token re-generation is triggered — re-auth is a no-op with PAT. Fully activated in the OAuth phase.
- On the next `api call` with `needs_reauth = true`: the CLI outputs an error guiding the user to run `account re-auth`.

#### Re-auth Trigger Flow

```
zapi-cli scope add --account "work" --scope "ZohoDesk.Reports.READ"
  └─ update scopes[] for account "work" in accounts.json
  └─ set needs_reauth = true on account "work"

next api call using account "work"
  └─ error to stderr:
     { "error": "Account 'work' has scope changes pending. Run: zapi-cli account re-auth --name work",
       "code": "NEEDS_REAUTH", "exitCode": 2 }

zapi-cli account re-auth --name "work"
  └─ (OAuth) exchange new token via Zoho OAuth API
  └─ update OS keychain for account "work"
  └─ set needs_reauth = false on account "work"
```

---

### Storage

| What | Location | Format |
|------|----------|--------|
| Account metadata | `<configDir>/zapi-cli/accounts.json` | Plain JSON |
| Secrets (PAT / tokens) | OS Keychain (platform-native) | OS-managed, not on disk |
| Keychain key format | `zapi-cli:<accountName>:<tokenType>` | — |
| Keychain fallback | `<configDir>/zapi-cli/keystore/` (encrypted file) | Encrypted JSON |

**Platform config directory (`<configDir>`):**

| Platform | Path |
|----------|------|
| macOS | `~/Library/Application Support/zapi-cli/` |
| Windows | `%LOCALAPPDATA%\zapi-cli\` |
| Linux | `~/.config/zapi-cli/` |

**Security rules:**
- `accounts.json` written with restrictive permissions (`0600` on Unix, ACL-restricted on Windows).
- Tokens are **never** written to `stdout` or `accounts.json`.
- Errors referencing auth failures use generic messages — no token values in stderr.

---

### API Base URL

The base URL is **fully supplied by the caller** on every `api call` invocation via `--base-url`. There is no derivation from the account's `domain` field for API calls.

**Examples:**

| Zoho Product | `--base-url` |
|---|---|
| Cliq (US) | `https://cliq.zoho.com/api/v2` |
| Cliq (EU) | `https://cliq.zoho.eu/api/v2` |
| Desk (US) | `https://desk.zoho.com/api/v1` |
| CRM (US) | `https://crm.zoho.com/crm/v5` |
| People (US) | `https://people.zoho.com/people/api` |
| Creator (US) | `https://creator.zoho.com/api/v2` |
| Projects (US) | `https://projectsapi.zoho.com/restapi` |

The final request URL is assembled as: `<base-url><path>`, where `--path` is the resource path relative to the base.

**Stored base URL (optional shorthand):** To avoid repeating long base URLs, accounts can optionally store a default base URL per product alias using the `api base-url` subcommand (see API Registry epic). This is a convenience layer; `--base-url` on `api call` always takes precedence.

---

### Host Allowlist

The outgoing request host must end with one of the following suffixes. Any call to an unlisted host is rejected with `HOST_NOT_ALLOWED`, exit 1.

| Suffix | Covers |
|---|---|
| `zoho.com` | US Zoho product APIs + Accounts |
| `zoho.eu` | EU Zoho product APIs |
| `zoho.in` | IN Zoho product APIs |
| `zoho.com.au` | AU Zoho product APIs |
| `zohoapis.com` | US Zoho API domain (used by some internal endpoints) |
| `zohoapis.in` | IN Zoho API domain |

> Additional Zoho product API domains (e.g., `projectsapi.zoho.com`) resolve to suffixes already covered. Extend as confirmed Zoho infrastructure domains are identified.

---

### Output Contract

- **stdout:** JSON always (structured result or success envelope).
- **stderr:** JSON error envelope on all failures.
- **Exit codes:** `0` = success, `1` = general error, `2` = auth failure / needs-reauth.

#### Success Example

```json
{
  "status": "ok",
  "data": { ... }
}
```

#### Error Envelope (stderr)

```json
{ "error": "account 'nonexistent' not found", "code": "ACCOUNT_NOT_FOUND", "exitCode": 1 }
{ "error": "authentication failed", "code": "AUTH_FAILURE", "exitCode": 2 }
{ "error": "API returned 403 Forbidden", "code": "API_ERROR", "detail": "...", "exitCode": 1 }
{ "error": "Account 'work' has scope changes pending. Run: zapi-cli account re-auth --name work", "code": "NEEDS_REAUTH", "exitCode": 2 }
{ "error": "ZohoCorp accounts are not permitted. Use a personal or external Zoho account.", "code": "ACCOUNT_DOMAIN_BLOCKED", "exitCode": 1 }
{ "error": "Host 'evil.example.com' is not in the allowed Zoho domain list.", "code": "HOST_NOT_ALLOWED", "exitCode": 1 }
```

---

## Use Cases (Stories)

### Epic 1 — Account Management

| ID | Command | Description |
|----|---------|-------------|
| UC-1a | `zapi-cli account add --name "work" --auth-type pat --token "xxx"` | Add a PAT-backed account (`--token` required with `--auth-type pat`) |
| UC-1b | `zapi-cli account add --name "work" --auth-type oauth` | Add an OAuth account (no `--token`; OAuth PKCE flow runs automatically) |
| UC-2 | `zapi-cli account list` | List all configured accounts |
| UC-3 | `zapi-cli account remove --name "work"` | Remove account + clear keychain secret |
| UC-4 | `zapi-cli account show --name "work"` | Show account details (token masked as `***`) |
| UC-5 | `zapi-cli account set-default --name "work"` | Set the active/default account |
| UC-6 | `zapi-cli account re-auth --name "work"` | Re-authenticate after scope change (v2 OAuth) |

### Epic 2 — Scope Management

All scope commands operate on a specific account. `--account` defaults to the active/default account if omitted.

| ID | Command | Description |
|----|---------|-------------|
| UC-7 | `zapi-cli scope add --scope "ZohoDesk.Tickets.READ" [--account "work"]` | Add scope to an account |
| UC-8 | `zapi-cli scope remove --scope "ZohoDesk.Tickets.READ" [--account "work"]` | Remove a scope from an account |
| UC-9 | `zapi-cli scope list [--account "work"]` | List scopes for an account |

### Epic 3 — General-Purpose API Invocation

The `--base-url` flag is **required** on every `api call`. It specifies the root of the API being called; `--path` is the resource path appended to it.

| ID | Command | Description |
|----|---------|-------------|
| UC-10 | `zapi-cli api call --base-url "https://cliq.zoho.com/api/v2" --method GET --path "/channels"` | GET request to Cliq |
| UC-11 | `zapi-cli api call --base-url "https://desk.zoho.com/api/v1" --method GET --path "/tickets"` | GET request to Desk |
| UC-12 | `zapi-cli api call --base-url "https://crm.zoho.com/crm/v5" --method POST --path "/Leads" --body '{"data":[...]}'` | POST with inline JSON body |
| UC-13 | `zapi-cli api call --base-url "..." --method POST --path "..." --body-file ./req.json` | POST with body from file |
| UC-14 | `zapi-cli api call --base-url "..." --method PUT --path "..." --header "X-Custom: val"` | Custom request headers |
| UC-15 | `zapi-cli api call --base-url "..." --method GET --path "..." --account "personal"` | Override account for a single call |
| UC-16 | `zapi-cli api call --base-url "..." --method GET --path "..." --query "key=value"` | Append query parameters |

#### `api call` Flags

| Flag | Required | Description |
|------|----------|-------------|
| `--base-url <url>` | **Yes** | Root URL of the target Zoho product API (e.g. `https://desk.zoho.com/api/v1`) |
| `--method <verb>` | Yes | HTTP method: `GET`, `POST`, `PUT`, `PATCH`, `DELETE` |
| `--path <path>` | Yes | Resource path relative to `--base-url` (e.g. `/tickets`) |
| `--body <json>` | No | Inline JSON request body |
| `--body-file <file>` | No | Path to a JSON file to use as the request body |
| `--header <k:v>` | No | Additional request header (repeatable) |
| `--query <k=v>` | No | Query parameter to append (repeatable) |
| `--account <name>` | No | Override the active account for this invocation |

### Epic 4 — WebSocket (Future)

| ID | Command | Description |
|----|---------|-------------|
| UC-17 | `zapi-cli ws connect --url "wss://..." --name "ws-1"` | Open a named WebSocket connection |
| UC-18 | `zapi-cli ws send --name "ws-1" --message '{"type":"ping"}'` | Send a message via the connection |
| UC-19 | `zapi-cli ws listen --name "ws-1"` | Stream incoming messages to stdout as JSON lines |
| UC-20 | `zapi-cli ws close --name "ws-1"` | Close the connection |

### Epic 5 — ZohoCorp Account Restriction (Privacy & Security)

> **Rationale:** ZohoCorp accounts (`*@zohocorp.com`) are internal employee accounts tied to Zoho's own corporate infrastructure. Allowing a CLI tool — particularly one used by AI agents — to authenticate with, store credentials for, or make API calls on behalf of these accounts creates an unacceptable privacy and security risk. All operations involving a ZohoCorp-domain account must be hard-blocked at the earliest possible entry point.

**Blocked entry points (in order of evaluation):**

| Entry point | Check performed |
|-------------|----------------|
| `account add` | Token's associated email domain resolved at add time; reject if `zohocorp.com` |
| `api call` (active account) | Resolved account's stored email checked before any HTTP request is dispatched |
| `api call --account <name>` | Same check applied to the named account override |
| Any `scope` command | Account resolved first; reject if ZohoCorp domain |

**Detection:** An account is identified as a ZohoCorp account when its email address matches the pattern `@zohocorp.<tld>`. The match is: `email.Split('@')[1].Split('.')[0].Equals("zohocorp", StringComparison.OrdinalIgnoreCase)` — covering `zohocorp.com`, `zohocorp.eu`, `zohocorp.in`, `zohocorp.com.au`, and all future datacenter TLDs automatically.

| ID | Scenario | Expected behaviour |
|----|----------|--------------------|
| UC-21 | `account add` with a `@zohocorp.com` PAT | Rejected immediately; keychain write never happens; exit 1 with `ACCOUNT_DOMAIN_BLOCKED` |
| UC-22 | `api call` resolves to a ZohoCorp account (default or `--account`) | Rejected before any HTTP request; exit 1 with `ACCOUNT_DOMAIN_BLOCKED` |
| UC-23 | `scope add/remove/list` targets a ZohoCorp account | Rejected; no mutation to `accounts.json`; exit 1 with `ACCOUNT_DOMAIN_BLOCKED` |
| UC-24 | Existing accounts.json already contains a ZohoCorp account | Any command that resolves to that account is rejected; tooling emits a warning on startup |

**Error output (stderr):**

```json
{ "error": "ZohoCorp accounts are not permitted. Use a personal or external Zoho account.", "code": "ACCOUNT_DOMAIN_BLOCKED", "exitCode": 1 }
```

**Design notes:**
- The blocked-domain list must be a constant in `ZapiCli.Core` — not a runtime config overridable via flags or environment variables.
- The check must run in `PatAuthProvider.StoreTokenAsync` and at the start of `ApiClient.CallAsync`.
- On `account add`, the email must be fetched from the Zoho user-info endpoint before the account is persisted, so the block applies even if the user does not supply their email explicitly.

---

### Epic 6 — Pex / Real-time Chat (Future)

> Pex is Zoho Cliq's proprietary real-time protocol. Included as a future epic since `zapi-cli` can host Cliq-specific protocol extensions without breaking the product-agnostic API layer.

| ID | Command | Description |
|----|---------|-------------|
| UC-25 | `zapi-cli pex connect --account "work"` | Open a Pex/WMS WebSocket for the named account |
| UC-26 | `zapi-cli pex send --account "work" --message '{"type":"ping"}'` | Send a raw message over the Pex socket |
| UC-27 | `zapi-cli pex drain --account "work"` | Return all buffered Pex callback events since last drain, then clear the buffer |
| UC-28 | `zapi-cli pex clear --account "work"` | Discard all buffered Pex events without returning them |
| UC-29 | `zapi-cli pex listen --account "work"` | Stream incoming Pex events to stdout as newline-delimited JSON (blocking) |
| UC-30 | `zapi-cli pex close --account "work"` | Close the Pex WebSocket for the named account |

**Buffer model:** Pex events are appended to `<configDir>/zapi-cli/pex-buffer/<account>.jsonl`. `drain` atomically reads and truncates this file.

---

### Epic 7 — API Registry (Future)

> A persistent `registry.json` of known API endpoints (`id`, `method`, `urlTemplate`, `baseUrl`, `purpose`). Agents use it to discover prerequisite endpoints without re-documenting them each session. Each entry carries a `baseUrl` field, since entries can span multiple Zoho products.

**Storage:** `<configDir>/zapi-cli/registry.json`

```json
{
  "apis": [
    {
      "id": "cliq-list-channels",
      "baseUrl": "https://cliq.zoho.com/api/v2",
      "method": "GET",
      "urlTemplate": "/channels",
      "purpose": "Returns all Cliq channels the authenticated user can access"
    },
    {
      "id": "desk-list-tickets",
      "baseUrl": "https://desk.zoho.com/api/v1",
      "method": "GET",
      "urlTemplate": "/tickets",
      "purpose": "Returns all Desk tickets for the authenticated user's org"
    }
  ]
}
```

| ID | Command | Description |
|----|---------|-------------|
| UC-31 | `zapi-cli api registry list` | List all registered API entries |
| UC-32 | `zapi-cli api registry add --id "desk-list-tickets" --base-url "https://desk.zoho.com/api/v1" --method GET --url-template "/tickets" --purpose "..."` | Upsert an entry (add or overwrite by id) |
| UC-33 | `zapi-cli api registry show --id "desk-list-tickets"` | Show a single entry by id |
| UC-34 | `zapi-cli api registry remove --id "desk-list-tickets"` | Delete an entry by id |

---

### Epic 8 — Utility Commands

| ID | Command | Description |
|----|---------|-------------|
| UC-35 | `zapi-cli util time-ms` | Current UTC time as a Unix millisecond timestamp (`{"ts": 1710000000000}`) |
| UC-36 | `zapi-cli util uuid` | Generate a random UUID v4 |

---

### Epic 9 — Trace Sessions

> Automatically records every `api call` and `pex drain` into a named, session-scoped append-only log. Agents export the trace at the end of an analysis session for developer reference.

#### Design principles

- **Passive — always-on when a session is active.** `api call` and `pex drain` automatically append a trace entry.
- **Named sessions.** One analysis run = one session, identified by a developer-readable name.
- **Non-destructive export.** `trace session export` writes to stdout but does not delete the file.
- **Two entry types.** `"api"` and `"pex"` entries. PEX entries carry a `relatedApiSeq` back-reference.
- **Sequential numbering.** Every entry gets a monotonically increasing `seq` number.

#### Storage

```
<configDir>/zapi-cli/traces/
  sessions.json               ← index: name, startTime, entryCount, status (active | closed)
  <session-name>/
    trace.jsonl               ← one JSON object per line (append-only)
```

#### Trace entry schemas

**`"api"` entry:**

```json
{
  "seq": 1,
  "type": "api",
  "session": "Desk-Tickets-2026-03-17",
  "timestamp": "2026-03-17T10:23:45.123Z",
  "durationMs": 342,
  "account": "work",
  "method": "GET",
  "baseUrl": "https://desk.zoho.com/api/v1",
  "url": "https://desk.zoho.com/api/v1/tickets",
  "requestHeaders": { "Content-Type": "application/json" },
  "requestBody": null,
  "responseStatus": 200,
  "responseHeaders": { "Content-Type": "application/json" },
  "responseBody": "{\"data\":[...]}",
  "error": null
}
```

- `baseUrl` is recorded alongside `url` so the trace is self-describing across multiple Zoho products.
- `requestHeaders` excludes the `Authorization` header — tokens are **never** written to the trace.

**`"pex"` entry:**

```json
{
  "seq": 3,
  "type": "pex",
  "session": "Desk-Tickets-2026-03-17",
  "timestamp": "2026-03-17T10:23:47.891Z",
  "account": "work",
  "relatedApiSeq": 2,
  "handlerClass": "ChatHandler",
  "callbackMethod": "onMessageReceived",
  "payload": "{...}"
}
```

#### Use cases

| ID | Command | Description |
|----|---------|-------------|
| UC-37 | `zapi-cli trace session start --name "Desk-Tickets-2026-03-17"` | Create and activate a named trace session |
| UC-38 | `zapi-cli trace session list` | List all sessions: name, start time, entry count, status |
| UC-39 | `zapi-cli trace session export --name "Desk-Tickets-2026-03-17"` | Dump full session trace to stdout as a JSON array |
| UC-40 | `zapi-cli trace session close --name "Desk-Tickets-2026-03-17"` | Mark session inactive — stops auto-appending; file preserved |
| UC-41 | `zapi-cli trace session remove --name "Desk-Tickets-2026-03-17"` | Delete session entry and all trace files |

**Optional export flags:**
- `--truncate-body <bytes>` — truncate `requestBody` and `responseBody` per entry.
- `--type api|pex` — export only entries of a given type.
- `--product <name>` — export only entries where `baseUrl` matches a registered product alias (e.g. `desk`, `crm`).

#### Fallback when no session is active

Trace entries are silently dropped — no implicit session created.

---

## Command Reference

```
zapi-cli [--account <name>] [--json] [--no-input] [--help] [--version]
         <group> <subcommand> [flags]

account add flags:
  --name <name>          Account alias (required)
  --auth-type pat|oauth  Authentication mechanism (required)
  --token <pat>          Personal Access Token — required when --auth-type is pat; not accepted when --auth-type is oauth

Groups:
  account   Manage Zoho accounts
  scope     Manage OAuth scopes per account
  api       Invoke Zoho REST API endpoints; manage local API registry
  pex       (future) Pex/WMS real-time WebSocket sessions
  trace     Session-scoped API call trace (start, export, close, remove)
  util      Utility helpers for agents (timestamps, UUIDs, etc.)
  ws        (future) Generic WebSocket connections

Global Flags:
  --account   string   Override active account for this invocation
  --json               Force JSON output to stdout (default: true)
  --no-input           Never prompt interactively; fail instead
  --help               Show help for current command / group
  --version            Print version and exit

api subcommands:
  api call             Fire an HTTP request against any Zoho product API endpoint
    --base-url <url>   Root URL of the target API (required)
    --method <verb>    HTTP method (required)
    --path <path>      Resource path relative to --base-url (required)
    --body <json>      Inline JSON request body
    --body-file <file> Path to JSON body file
    --header <k:v>     Additional request header (repeatable)
    --query <k=v>      Query parameter to append (repeatable)
    --account <name>   Override active account

  api registry list    List all entries in the local API registry
  api registry add     Upsert an endpoint into the registry
    --id <id>          Unique identifier for the entry
    --base-url <url>   Root URL of the product API
    --method <verb>    HTTP method
    --url-template <t> Resource path template (e.g. /tickets/{id})
    --purpose <text>   Human-readable description
  api registry show    Show a single registry entry by id
  api registry remove  Delete an entry from the registry

pex subcommands:
  pex connect          Open a Pex/WMS WebSocket for an account
  pex send             Send a raw message over the socket
  pex drain            Return + clear the buffered event log
  pex clear            Discard buffered events without returning them
  pex listen           Stream events to stdout as newline-delimited JSON (blocking)
  pex close            Close the socket

trace subcommands:
  trace session start  --name <name>                    Create & activate a named trace session
  trace session list                                    List all sessions
  trace session export --name <name>                    Dump full session trace to stdout as JSON array
                        [--truncate-body <n>]           Truncate request/response bodies
                        [--type api|pex]                Filter by entry type
                        [--product <name>]              Filter by product base URL alias
  trace session close  --name <name>                    Deactivate session; file preserved
  trace session remove --name <name>                    Delete session + all trace files

util subcommands:
  util time-ms         Current UTC time as a millisecond epoch timestamp
  util uuid            Generate a random UUID v4
```

---

## Tech Choices

| Concern | Choice | Reasoning |
|---------|--------|-----------|
| Runtime | .NET 10 | Latest stable, cross-platform, single-binary publish |
| Language | C# 13+ | Records, file-scoped namespaces, pattern matching, nullable types |
| CLI Framework | `Spectre.Console.Cli` | Mature, convention-based, handles commands/subcommands/flags cleanly |
| JSON | `System.Text.Json` | Built-in stdlib, no extra dependency, good performance |
| HTTP | `HttpClient` (stdlib) | Sufficient for a general API invoker |
| DI | `Microsoft.Extensions.DependencyInjection` | Standard .NET DI container |
| OS Keychain | TBD: `Microsoft.Windows.Security.Credentials` / Security.framework / `libsecret` via P/Invoke | Platform-native secret storage |
| Config I/O | Custom `AccountStore` (lightweight JSON read/write) | Simple flat store; no config framework needed |
| Logging | `Microsoft.Extensions.Logging` | Structured, standard |
| Publish | `dotnet publish -r <rid> /p:PublishSingleFile=true` | Self-contained single binary |
| Test | xUnit | Convention-aligned, cross-platform |

### Suggested Project Structure

```
zapi-cli/
├── src/
│   ├── ZapiCli/                          ← Entry point + Spectre command wiring
│   │   ├── Program.cs
│   │   └── Commands/
│   │       ├── AccountCommands.cs        ← add, list, remove, show, set-default, re-auth
│   │       ├── ScopeCommands.cs          ← add, remove, list
│   │       ├── ApiCommands.cs            ← api call (--base-url required)
│   │       ├── ApiRegistryCommands.cs    ← api registry list/add/show/remove
│   │       ├── PexCommands.cs            ← pex connect/send/drain/clear/listen/close
│   │       ├── TraceCommands.cs          ← trace session start/list/export/close/remove
│   │       └── UtilCommands.cs           ← util time-ms, util uuid
│   │
│   ├── ZapiCli.Core/                     ← Domain logic (no CLI concerns)
│   │   ├── Auth/
│   │   │   ├── IAuthProvider.cs
│   │   │   └── PatAuthProvider.cs
│   │   ├── Accounts/
│   │   │   ├── AccountStore.cs           ← JSON config read/write
│   │   │   └── AccountConfig.cs          ← Model: account metadata + DTO
│   │   ├── Api/
│   │   │   ├── ApiClient.cs              ← HttpClient wrapper; injects auth header; host allowlist; writes trace entries
│   │   │   └── ApiRegistry.cs            ← registry.json read/write (entries carry baseUrl field)
│   │   ├── Pex/
│   │   │   └── PexBuffer.cs              ← per-account JSONL event buffer (future)
│   │   └── Trace/
│   │       ├── TraceSession.cs           ← session index read/write (sessions.json)
│   │       ├── TraceWriter.cs            ← append-only JSONL writer for trace entries
│   │       ├── TraceEntry.cs             ← record types: ApiTraceEntry (carries baseUrl), PexTraceEntry
│   │       └── TraceExporter.cs          ← reads JSONL, emits JSON array with optional filters
│   │
│   └── ZapiCli.Keychain/                 ← OS keychain abstraction
│       ├── IKeychainProvider.cs
│       ├── MacOsKeychainProvider.cs
│       ├── WindowsKeychainProvider.cs
│       ├── LinuxKeychainProvider.cs
│       └── EncryptedFileKeychainProvider.cs   ← fallback when no OS keychain
│
└── tests/
    └── ZapiCli.Tests/
```

---

## Open Questions / Further Considerations

1. **`--base-url` shorthand / product aliases** — To reduce verbosity, consider allowing accounts to register named product aliases (e.g. `desk → https://desk.zoho.com/api/v1`) so agents can write `--product desk` instead of repeating the full URL. Stored in a `products.json` alongside `accounts.json`. Decision needed before defining the `api call` flag set fully.

2. **Keychain library choice** — Evaluate P/Invoke per-platform vs adopting `git-credential-manager`'s keyring abstractions as a NuGet. The latter is battle-tested but adds a transitive dependency.

3. **Pex protocol details** — Before the Pex epic, capture the exact message format, connection lifecycle, and server handshake from Cliq internal specs. This is a non-standard protocol — no design work until fully documented.

4. **WebSocket session state** — Named WS sessions (`--name`) imply a running daemon or persisted connection ID. Decide upfront: long-lived background process vs ephemeral connect-send-close per invocation.

5. **AI Skill packaging** — The binary is the delivery unit for both GitHub Copilot CLI Skills and Claude Agent Skills:

   **GitHub Copilot CLI Skill (`awesome-copilot/skills/zapi-cli/SKILL.md`)**
   - `name`: `zapi-cli`; `description`: wraps the purpose and all command groups.
   - List all command groups and their flags, emphasising that `--base-url` is required on every `api call`.
   - Bundle the pre-built binary (or a shell wrapper that locates it on `PATH`).
   - The skill must NOT expose token values — all auth is handled by the binary.
   - Expected usage: agent calls `zapi-cli <group> <subcommand> [flags]` as a subprocess and parses stdout JSON.

   **Installation prerequisite note (in skill instructions):**
   > Before using this skill, install `zapi-cli`:
   > - macOS/Linux: `dotnet tool install -g zapicli` (or download the release binary and add to PATH)
   > - Windows: same via `winget` or direct binary download

6. **Datacenter auto-detection** — When adding an account, consider auto-detecting the datacenter from the token/user info API response instead of requiring manual `--domain` input.

7. **Blocked-domain list extensibility** — The current design blocks all accounts whose email domain's first label is `zohocorp`. Decide whether additional internal org-domain labels should be blockable via a compile-time list (still not overridable at runtime).

8. **Trace body size limits** — Recommended default: write full body to `.jsonl`, apply `--truncate-body` only at export time. Preserves full fidelity in storage and gives the agent control at export.

9. **Multi-product trace filtering** — The `--product` export filter requires a `products.json` alias registry to work (see point 1). Tie the two features together or defer both.
