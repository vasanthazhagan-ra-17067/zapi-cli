# zapi-cli — Technical Specification

> **Status:** Draft
> **Derived from:** `tech_spec/brainstorming.md`
> **Date:** 2026-03-17

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Runtime & Language](#2-runtime--language)
3. [Project Structure](#3-project-structure)
4. [CLI Structure & Commands](#4-cli-structure--commands)
5. [Data Models](#5-data-models)
6. [Authentication](#6-authentication)
7. [ZohoCorp Account Restriction](#7-zohocorp-account-restriction)
8. [Storage](#8-storage)
9. [API Client](#9-api-client)
10. [Trace Sessions](#10-trace-sessions)
11. [Output Contract](#11-output-contract)
12. [Error Handling](#12-error-handling)
13. [Dependencies](#13-dependencies)
14. [Non-Goals (v1)](#14-non-goals-v1)

---

## 1. Project Overview

**zapi-cli** is a standalone, multi-platform CLI binary for interacting with **any Zoho product's REST APIs**. It manages multiple Zoho accounts, handles authentication via a pluggable `IAuthProvider` interface, and exposes a general-purpose HTTP API invoker where the caller supplies the full base URL at invocation time.

**Primary consumer:** AI Agents via GitHub Copilot CLI Skills.

**Scope:**
- **P1 (v1):** Account management (OAuth Self-Client), REST API invocation (`--url` required), utility commands, ZohoCorp account blocking
- **P2:** Scope management, trace sessions, API Registry
- **P3 (future):** WebSocket connections, Pex real-time chat

---

## 2. Runtime & Language

| Concern | Choice |
|---------|--------|
| Runtime | .NET 10 |
| Language | C# 13+ |
| Target frameworks | `net10.0` |
| Nullable reference types | Enabled (`<Nullable>enable</Nullable>`) |
| Implicit usings | Enabled |
| Publish | `dotnet publish -r <rid> /p:PublishSingleFile=true --self-contained true` |
| Supported RIDs | `win-x64`, `osx-x64`, `osx-arm64`, `linux-x64` |

---

## 3. Project Structure

```
zapi-cli/
├── src/
│   ├── ZapiCli/                              ← Entry point + Spectre command wiring
│   │   ├── ZapiCli.csproj
│   │   ├── Program.cs
│   │   └── Commands/
│   │       ├── AccountCommands.cs            ← add, list, remove, show, set-default, re-auth
│   │       ├── ScopeCommands.cs              ← add, remove, list
│   │       ├── ApiCommands.cs                ← api call (--url required)
│   │       ├── ApiRegistryCommands.cs        ← api registry list/add/show/remove
│   │       ├── PexCommands.cs                ← pex connect/send/drain/clear/listen/close
│   │       ├── TraceCommands.cs              ← trace session start/list/export/close/remove
│   │       └── UtilCommands.cs               ← util time-ms, util uuid
│   │
│   ├── ZapiCli.Core/                         ← Domain logic (no CLI concerns)
│   │   ├── ZapiCli.Core.csproj
│   │   ├── Auth/
│   │   │   ├── IAuthProvider.cs
│   │   │   └── OAuthProvider.cs
│   │   ├── Accounts/
│   │   │   ├── AccountStore.cs               ← JSON config read/write
│   │   │   └── AccountConfig.cs              ← AccountEntry model + AccountsRoot DTO
│   │   ├── Api/
│   │   │   ├── ApiClient.cs                  ← HttpClient wrapper; injects auth; host allowlist; writes trace entries
│   │   │   └── ApiRegistry.cs                ← registry.json read/write (entries carry full `url` field)
│   │   ├── Pex/
│   │   │   └── PexBuffer.cs                  ← per-account JSONL event buffer (future)
│   │   └── Trace/
│   │       ├── TraceSession.cs               ← session index read/write (sessions.json)
│   │       ├── TraceWriter.cs                ← append-only JSONL writer for trace entries
│   │       └── TraceEntry.cs                 ← record types: ApiTraceEntry (url + base_url for traceability), PexTraceEntry
│   │       └── TraceExporter.cs              ← reads JSONL, emits JSON array with optional filters
│   │
│   └── ZapiCli.Keychain/                     ← OS keychain abstraction
│       ├── ZapiCli.Keychain.csproj
│       ├── IKeychainProvider.cs
│       ├── MacOsKeychainProvider.cs           ← macOS Security.framework via P/Invoke
│       ├── WindowsKeychainProvider.cs         ← Windows Credential Manager via P/Invoke
│       ├── LinuxKeychainProvider.cs           ← libsecret / Secret Service via P/Invoke
│       └── EncryptedFileKeychainProvider.cs   ← fallback: AES-256 encrypted file
│
└── tests/
    └── ZapiCli.Tests/
        ├── ZapiCli.Tests.csproj
        ├── AccountStoreTests.cs
│       ├── OAuthProviderTests.cs
        └── ApiClientTests.cs
```

### Project References

```
ZapiCli  →  ZapiCli.Core
ZapiCli  →  ZapiCli.Keychain
ZapiCli.Core  →  ZapiCli.Keychain
```

---

## 4. CLI Structure & Commands

### Root

```
zapi-cli [global-flags] <group> <subcommand> [flags]
```

### Global Flags

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--account` | string | active account | Override account for this invocation |
| `--json` | bool | `true` | Force JSON output to stdout |
| `--no-input` | bool | `false` | Never prompt; fail instead |
| `--help` | — | — | Show help for current command/group |
| `--version` | — | — | Print version and exit |

---

### Group: `account`

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `add` | `--name` (req), `--token` (req), `--client-id` (req), `--client-secret` (req), `--dc` (default: `us`; values: `us\|eu\|in\|au\|cn\|jp\|sa\|uk\|ca`) | Add a new OAuth Self-Client account |
| `list` | — | List all configured accounts (token masked) |
| `remove` | `--name` (req) | Revoke token via Zoho OAuth revoke endpoint, clear keychain secrets, remove account |
| `show` | `--name` (req) | Show account details (token masked as `***`) |
| `set-default` | `--name` (req) | Set active/default account |
| `re-auth` | `--name` (req) | Manually trigger token refresh for an account (also triggered automatically on expiry / scope change) |


---

### Group: `scope`

All scope subcommands target an account. `--account` defaults to the active account if omitted.

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `add` | `--scope` (req, comma-separated), `[--account]` | Add one or more OAuth scopes to an account |
| `remove` | `--scope` (req, comma-separated), `[--account]` | Remove one or more OAuth scopes from an account |
| `list` | `[--account]` | List all scopes for an account |

Adding or removing a scope sets `needs_reauth = true` on that account.

---

### Group: `api`

#### `api call`

`--url` is **required** on every invocation. The full endpoint URL is supplied directly by the caller.

| Flag | Required | Type | Description |
|------|----------|------|-------------|
| `--url` | **Yes** | string | Full URL of the target Zoho API endpoint (e.g. `https://desk.zoho.com/api/v1/tickets`) |
| `--method` | Yes | `GET\|POST\|PUT\|PATCH\|DELETE` | HTTP method |
| `--body` | No | string | Inline JSON request body |
| `--body-file` | No | string | Path to a JSON file to use as request body |
| `--header` | No | `key:value` | Additional request header (repeatable) |
| `--query` | No | `key=value` | Query string parameter (repeatable) |
| `--account` | No | string | Account override for this call |

`--body` and `--body-file` are mutually exclusive.

**Examples:**

```sh
zapi-cli api call --url "https://cliq.zoho.com/api/v2/channels" --method GET
zapi-cli api call --url "https://desk.zoho.com/api/v1/tickets" --method GET
zapi-cli api call --url "https://crm.zoho.com/crm/v5/Leads" --method POST --body '{"data":[{"Last_Name":"Doe"}]}'
```

#### `api registry` *(P2)*

Each registry entry has a single `url` field (full endpoint URL), consistent with `api call --url`.

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `list` | — | List all registered API entries |
| `add` | `--id` (req), `--url` (req), `--method` (req), `--purpose` (req) | Upsert an endpoint entry (add or overwrite by id) |
| `show` | `--id` (req) | Show a single entry by id |
| `remove` | `--id` (req) | Delete an entry by id |

Storage: `<configDir>/zapi-cli/registry.json`

```json
{
  "apis": [
    {
      "id": "cliq-list-channels",
      "url": "https://cliq.zoho.com/api/v2/channels",
      "method": "GET",
      "purpose": "Returns all Cliq channels the authenticated user can access"
    },
    {
      "id": "desk-list-tickets",
      "url": "https://desk.zoho.com/api/v1/tickets",
      "method": "GET",
      "purpose": "Returns all Desk tickets for the authenticated user's org"
    }
  ]
}
```

---

### Group: `ws` *(Future)*

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `connect` | `--url` (req), `--name` (req), `[--account]` | Open a named WebSocket connection |
| `send` | `--name` (req), `--message` (req) | Send a message |
| `listen` | `--name` (req) | Stream incoming messages to stdout as JSON lines |
| `close` | `--name` (req) | Close the connection |

---

### Group: `pex` *(Future)*

> Pex is Zoho Cliq's proprietary real-time protocol. Events are buffered to `<configDir>/zapi-cli/pex-buffer/<account>.jsonl`. `drain` atomically reads and truncates this file.

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `connect` | `[--account]` | Open a Pex/WMS WebSocket for the named account |
| `send` | `--message` (req), `[--account]` | Send a raw message over the Pex socket |
| `drain` | `[--account]` | Return all buffered Pex events since last drain, then clear the buffer; also appends pex entries to the active trace session |
| `clear` | `[--account]` | Discard buffered events without returning them |
| `listen` | `[--account]` | Stream incoming Pex events to stdout as newline-delimited JSON (blocking, Ctrl+C to stop) |
| `close` | `[--account]` | Close the Pex WebSocket for the named account |

---

### Group: `trace`

> Session-scoped API call trace. Each session has a UUID (primary key, immutable) and a human-readable name (non-unique label). The trace file is written **live on every API call** — there is no deferred export step. `api call` and `pex drain` automatically append entries when a session is active. A default export directory can be configured once; it is used whenever `--export-path` is omitted at session start.

#### `trace session`

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `session start` | `--name` (req), `[--export-path <path>]` | Create and activate a named session; assigns a UUID; resolves export path; begins live writing. Error if neither `--export-path` nor a default path is configured. |
| `session list` | — | List all sessions: uuid, name, start_time, entry_count, status, export_path |
| `session export` | `(--id <uuid>` or `--name <name>)` one req, `[--truncate-body <chars>]`, `[--type api\|pex]` | Re-read the live trace file from disk and output JSON array to stdout (non-destructive; file is already up to date) |
| `session close` | `(--id <uuid>` or `--name <name>)` one req, `[--wait-ms <ms>]` (default: 5000) | Set status to "closing"; wait `wait-ms` for in-flight calls to complete; then mark "closed" |
| `session reopen` | `(--id <uuid>` or `--name <name>)` one req | Reactivate a closed session; subsequent calls append to the existing trace file, continuing the seq counter |
| `session remove` | `(--id <uuid>` or `--name <name>)` one req | Remove session from the index; trace file at `export_path` is **preserved** |

> **Name disambiguation:** If `--name` is used and multiple sessions share that name, the command fails with `SESSION_AMBIGUOUS` listing the matching UUIDs. Use `--id` to be unambiguous.

#### `trace config`

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `config set` | `--default-export-path <path>` (req) | Set the default export directory used when `--export-path` is omitted at session start |
| `config show` | — | Show current trace configuration (`default_export_path`) |

---

### Group: `util`

| Subcommand | Description |
|-----------|-------------|
| `time-ms` | Current UTC time as a Unix millisecond timestamp (`{"ts": 1710000000000}`) |
| `uuid` | Generate a random UUID v4 |

---

## 5. Data Models

### `AccountEntry`

```csharp
public sealed record AccountEntry
{
    public required string Name { get; init; }
    public required string Dc { get; init; }               // datacenter short name: us, eu, in, au, cn, jp, sa, uk, ca
    public string? Email { get; init; }                    // fetched from Zoho user-info at account add; used for ZohoCorp domain block
    public string? Zuid { get; init; }                     // fetched from Zoho user-info at account add; Zoho User ID (ZUIDSTRING)
    public List<string> Scopes { get; init; } = [];
    public bool IsDefault { get; init; }
    public bool NeedsReauth { get; init; }
}
```

> `Dc` maps to the Zoho Accounts URL for token endpoint resolution (e.g. `us` → `https://accounts.zoho.com`). It is **not** used to derive an API base URL — the caller supplies `--url` directly on every `api call`.

### `AccountsRoot` (accounts.json root)

```csharp
public sealed record AccountsRoot
{
    public List<AccountEntry> Accounts { get; init; } = [];
}
```

### `accounts.json` Example

```json
{
  "accounts": [
    {
      "name": "work",
      "dc": "us",
      "email": "user@example.com",
      "zuidstring": "1234567890",
      "scopes": ["ZohoCliq.Channels.READ", "ZohoDesk.Tickets.WRITE"],
      "is_default": true,
      "needs_reauth": false
    },
    {
      "name": "personal",
      "dc": "eu",
      "email": "user@personal.com",
      "zuidstring": "9876543210",
      "scopes": [],
      "is_default": false,
      "needs_reauth": false
    }
  ]
}
```

JSON property names use `snake_case` (configured via `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`).

### Trace Models

**`TraceSessionEntry`** — one entry per session in `sessions.json`:

```csharp
public sealed record TraceSessionEntry
{
    public required string UniqueId { get; init; }        // UUID v4 — primary key, assigned at session start; immutable
    public required string Name { get; init; }            // human-readable label — non-unique; two sessions may share a name
    public required DateTimeOffset StartTime { get; init; }
    public required string ExportPath { get; init; }      // resolved absolute path to the live trace file; set once at start
    public int EntryCount { get; init; }                  // incremented under Mutex on every append; seq = new EntryCount
    public required string Status { get; init; }          // active | closing | closed
}
```

**`TraceConfig`** — persisted in `trace-config.json`:

```csharp
public sealed record TraceConfig
{
    public string? DefaultExportPath { get; init; }       // default export directory; null if not configured
}
```

**`ApiTraceEntry`** — written by every `api call` when a session is active:

```csharp
public sealed record ApiTraceEntry
{
    public int Seq { get; init; }
    public string Type => "api";
    public required string Session { get; init; }        // session name (human-readable label)
    public required string SessionId { get; init; }      // session UUID — primary correlation key
    public required DateTimeOffset Timestamp { get; init; }
    public int DurationMs { get; init; }
    public required string Account { get; init; }
    public required string Method { get; init; }
    public required string BaseUrl { get; init; }        // scheme+host+port only; e.g. "https://desk.zoho.com"
    public required string Url { get; init; }            // full assembled URL including path and query
    public Dictionary<string, string> RequestHeaders { get; init; } = [];   // security headers excluded (see §10)
    public string? RequestBody { get; init; }
    public int ResponseStatus { get; init; }
    public Dictionary<string, string> ResponseHeaders { get; init; } = [];  // set-cookie, www-authenticate excluded
    public string? ResponseBody { get; init; }
    public string? Error { get; init; }                  // non-null on transport failure only (not HTTP 4xx/5xx)
}
```

**`PexTraceEntry`** — written for each event when `pex drain` is called with a session active:

```csharp
public sealed record PexTraceEntry
{
    public int Seq { get; init; }
    public string Type => "pex";
    public required string Session { get; init; }        // session name
    public required string SessionId { get; init; }      // session UUID
    public required DateTimeOffset Timestamp { get; init; }
    public required string Account { get; init; }
    public int? RelatedApiSeq { get; init; }    // seq of most recent api entry before this drain; null if none
    public required string HandlerClass { get; init; }
    public required string CallbackMethod { get; init; }
    public required string Payload { get; init; }
}
```

> Security headers are **never** written to trace entries. See §10 for the full exclusion lists.

---

## 6. Authentication

### Interface

```csharp
public interface IAuthProvider
{
    Task<string> GetTokenAsync(string accountName, CancellationToken ct = default);
    Task StoreTokenAsync(string accountName, string token, CancellationToken ct = default);
    Task ClearTokenAsync(string accountName, CancellationToken ct = default);
}
```

### v1 — `OAuthProvider` (Self-Client)

- Implements `IAuthProvider`.
- User supplies `--token`, `--client-id`, `--client-secret` at `account add` time.
- `StoreTokenAsync` → writes access token + client credentials to OS keychain under key `zapi-cli:<accountName>:oauth`.
- `GetTokenAsync` → reads access token from OS keychain by the same key.
- `ClearTokenAsync` → deletes all stored credentials from OS keychain.
- Token injected into requests as: `Authorization: Zoho-oauthtoken <token>`.
- `account re-auth` uses stored `client-id` + `client-secret` to call Zoho OAuth token refresh endpoint and update the stored access token.

### `account add` Flow

#### OAuth Self-Client flow

```
zapi-cli account add --name "work" --token "xxx" --client-id "yyy" --client-secret "zzz" [--dc us]
  1. Validate --token, --client-id, --client-secret are all present (error INVALID_ARGS if any missing)
  2. Validate name is unique in accounts.json
  3. Resolve Accounts URL from --dc (default: us → https://accounts.zoho.com)
  4. Call GET https://accounts.zoho.<dc-domain>/oauth/user/info with --token as bearer token
     → abort with EMAIL_REQUIRED if no email or ZUIDSTRING returned
     → abort with AUTH_FAILURE on network/HTTP error
  5. Apply ZohoCorp domain check on the returned email; abort with ACCOUNT_DOMAIN_BLOCKED if blocked
  6. Call OAuthProvider.StoreTokenAsync: writes token + client-id + client-secret to OS keychain
     under key: zapi-cli:work:oauth
  7. Append AccountEntry (dc, email, zuidstring, scopes=[]) to accounts.json
  8. If no other account exists, set is_default = true
  9. Write accounts.json (permissions: 0600 on Unix)
  10. Output: { "status": "ok", "data": { "name": "work", "dc": "us" } }
```

### Scope Change + Re-auth Flow

> **Auto-refresh principle:** Re-authentication is triggered **automatically** whenever a token is expired or scope changes are pending. The user never needs to run `account re-auth` manually in normal operation. The command remains available for explicit/forced refresh.

```
zapi-cli scope add --scope "ZohoDesk.Tickets.READ" [--account "work"]
  1. Resolve target account (--account or default)
  2. Add scope to AccountEntry.Scopes if not already present
  3. Set AccountEntry.NeedsReauth = true
  4. Write accounts.json
  5. Output: { "status": "ok", "data": { "account": "work", "scopes": [...] } }

Next api call on account "work"  (NeedsReauth == true  OR  401 received from Zoho):
  1. Load AccountEntry for "work"
  2. Detect re-auth needed: NeedsReauth == true  OR  API returned 401
  3. Auto-refresh:
     a. Read stored client-id + client-secret from OS keychain
     b. Call Zoho OAuth token endpoint using stored credentials + current AccountEntry.Scopes
     c. Store new access token in OS keychain
     d. Set AccountEntry.NeedsReauth = false; write accounts.json
  4. Retry the API request with the new token
  5. Return the result normally
  
  If auto-refresh itself fails (invalid client credentials, revoked app, etc.):
  → write error to stderr + exit 2 with AUTH_FAILURE

zapi-cli account re-auth --name "work"  (explicit / forced)
  1. Read stored client-id + client-secret from OS keychain for account "work"
  2. Call Zoho OAuth token endpoint using stored credentials + current AccountEntry.Scopes
  3. Store updated access token in OS keychain
  4. Set AccountEntry.NeedsReauth = false; write accounts.json
  5. Output: { "status": "ok", "data": { "name": "work" } }
```

---

## 7. ZohoCorp Account Restriction

> **Rationale:** ZohoCorp accounts (`*@zohocorp.com`) are internal employee accounts tied to Zoho's corporate infrastructure. Allowing a CLI tool — particularly one used by AI agents — to authenticate with, store credentials for, or make API calls on behalf of these accounts is an unacceptable privacy and security risk. All operations involving a ZohoCorp-domain account are hard-blocked at the earliest possible entry point.

### Detection Rule

```csharp
email.Split('@')[1].Split('.')[0]
      .Equals("zohocorp", StringComparison.OrdinalIgnoreCase)
```

Covers `zohocorp.com`, `zohocorp.eu`, `zohocorp.in`, `zohocorp.com.au`, and all future datacenter TLDs automatically. The blocked-domain label list is a sealed compile-time constant in `ZapiCli.Core` — not overridable at runtime.

### Blocked Entry Points

| Entry point | Check |
|-------------|-------|
| `account add` | Email fetched from Zoho user-info API before persisting; reject if ZohoCorp domain |
| `api call` (active account) | Resolved account's stored email checked before any HTTP request is dispatched |
| `api call --account <name>` | Same check applied to the named account override |
| Any `scope` command | Account resolved first; reject if ZohoCorp domain |

### Implementation

- **At `account add`:** The supplied `--token` is used to call `GET https://accounts.zoho.<dc-domain>/oauth/user/info`. Both the `email` and `ZUIDSTRING` are extracted before the account is persisted.
- `AaaServer.profile.READ` must be included in the token's granted scopes to guarantee both fields are returned.
- **Fail closed**: If the user-info endpoint returns no email (for any auth type), `account add` must abort with `EMAIL_REQUIRED` (exit 1). The ZohoCorp check is never skipped — a missing email is treated as an unverifiable identity.
- The domain-block check also runs at the start of `ApiClient.CallAsync` against the stored account email.
- The check must NOT be bypassable via flags, environment variables, or config.

### Use Cases

| ID | Scenario | Behaviour |
|----|----------|-----------|
| UC-21 | `account add` with a `@zohocorp.com` token | Rejected immediately; keychain write never happens; exit 1 `ACCOUNT_DOMAIN_BLOCKED` |
| UC-22 | `api call` resolves to a ZohoCorp account | Rejected before any HTTP request; exit 1 `ACCOUNT_DOMAIN_BLOCKED` |
| UC-23 | `scope add/remove/list` targets a ZohoCorp account | Rejected; no mutation to `accounts.json`; exit 1 `ACCOUNT_DOMAIN_BLOCKED` |
| UC-24 | Existing `accounts.json` already contains a ZohoCorp account | Any command resolving to that account is rejected; warning emitted on startup |
| UC-25 | `account add` where user-info returns no email | Rejected before any token storage; exit 1 `EMAIL_REQUIRED` |

**Error output (stderr):**

```json
{ "error": "ZohoCorp accounts are not permitted. Use a personal or external Zoho account.", "code": "ACCOUNT_DOMAIN_BLOCKED", "exitCode": 1 }
```

---

## 8. Storage

### Paths

| Platform | Config Directory |
|----------|-----------------|
| macOS | `~/Library/Application Support/zapi-cli/` |
| Windows | `%LOCALAPPDATA%\zapi-cli\` |
| Linux | `~/.config/zapi-cli/` |

Path resolution uses `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` on macOS/Linux and `Environment.SpecialFolder.LocalApplicationData` on Windows.

### Files

| File | Location | Format |
|------|----------|--------|
| `accounts.json` | `<configDir>/accounts.json` | Plain JSON, `snake_case` |
| `registry.json` | `<configDir>/registry.json` | Plain JSON *(future)* |
| Pex event buffer | `<configDir>/pex-buffer/<account>.jsonl` | Newline-delimited JSON *(future)* |
| Trace index | `<configDir>/traces/sessions.json` | Plain JSON |
| Trace config | `<configDir>/trace-config.json` | Plain JSON |
| Trace entries | User-specified `export_path` per session (stored in `sessions.json`); outside `<configDir>` if desired | Newline-delimited JSON, append-only, written live per call |
| Secrets | OS Keychain | OS-managed, never on disk |
| Keychain fallback | `<configDir>/keystore/<accountName>.bin` | AES-256 encrypted |

### Security Rules

- `accounts.json` written with `0600` permissions on Unix (via `File.SetUnixFileMode`).
- On Windows, NTFS ACL restricts read/write to the current user only.
- Tokens are **never** written to `accounts.json`, stdout, or any log.
- Error messages never include raw token values.

### `AccountStore` Contract

```csharp
public interface IAccountStore
{
    Task<AccountsRoot> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AccountsRoot root, CancellationToken ct = default);
    Task<AccountEntry?> FindAsync(string name, CancellationToken ct = default);
    Task<AccountEntry> GetDefaultAsync(CancellationToken ct = default);  // throws if none
}
```

### `IKeychainProvider` Contract

```csharp
public interface IKeychainProvider
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

// Key format: "zapi-cli:<accountName>:oauth"
// e.g.        "zapi-cli:work:oauth"
//             "zapi-cli:personal:oauth"
```

**Platform implementations:**

| Platform | Class | Backend |
|----------|-------|---------|
| macOS | `MacOsKeychainProvider` | Security.framework (`SecKeychainAddGenericPassword`) via P/Invoke |
| Windows | `WindowsKeychainProvider` | `CredWrite` / `CredRead` via P/Invoke |
| Linux | `LinuxKeychainProvider` | `libsecret` Secret Service API via P/Invoke |
| Fallback | `EncryptedFileKeychainProvider` | AES-256-GCM, key derived from machine entropy |

Runtime detection via `RuntimeInformation.IsOSPlatform(...)`. Fallback is used when the OS keychain is unavailable.

---

## 9. API Client

### Host Allowlist

Before every outgoing HTTP request `ApiClient` validates the resolved URL host against an allowlist. Any URL whose host does not end with one of the allowed suffixes is rejected with `HOST_NOT_ALLOWED`, exit 1.

| Suffix | Covers |
|--------|--------|
| `zoho.com` | US Zoho product APIs + Accounts |
| `zoho.eu` | EU Zoho product APIs |
| `zoho.in` | IN Zoho product APIs |
| `zoho.com.au` | AU Zoho product APIs |
| `zohoapis.com` | US Zoho API domain (used by some internal endpoints) |
| `zohoapis.in` | IN Zoho API domain |

The allowlist is a sealed compile-time constant in `ZapiCli.Core` — not configurable at runtime.

### `ApiClient`

The full URL is supplied by the caller via `--url`. There is no URL derivation from the account's `Dc` field.

```csharp
public sealed class ApiClient
{
    // Validates the host against the allowlist before sending
    // Checks ZohoCorp domain block on the resolved account before sending
    // Injects: Authorization: Zoho-oauthtoken <token>
    // Injects: Content-Type: application/json  (for POST/PUT/PATCH)
    // Appends trace entry to active session (if any)

    public Task<ApiResponse> CallAsync(ApiRequest request, CancellationToken ct = default);
}

public sealed record ApiRequest
{
    public required string Url { get; init; }              // full endpoint URL supplied by --url
    public required string Method { get; init; }
    public string? Body { get; init; }
    public Dictionary<string, string> Headers { get; init; } = [];
    public Dictionary<string, string> QueryParams { get; init; } = [];
    public required string AccountName { get; init; }
}

public sealed record ApiResponse
{
    public int StatusCode { get; init; }
    public required string Body { get; init; }             // raw JSON string from server
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
```

---

## 10. Trace Sessions

> Sessions provide a continuous, automatic record of every API call made during a developer or agent analysis run. The trace file is written **live on every API call** — no explicit export or flush step is needed. This guarantees that even a crash or unexpected process exit captures everything up to that point.

### Design Principles

- **Live write-on-call.** `api call` and `pex drain` write to the trace file immediately on every invocation. The file is always current.
- **Dual identity.** Every session has a `unique_id` (UUID v4, primary key, assigned at start, immutable) and a `name` (human-readable label, non-unique — two sessions may share a name).
- **Name is non-unique.** UUID is the authoritative identifier. CLI commands accept `--id <uuid>` (unambiguous) or `--name <name>` (convenience; error `SESSION_AMBIGUOUS` if multiple sessions share that name).
- **Export path at start.** The trace file path is resolved once at `session start` (from `--export-path` or from `trace-config.json` default) and stored permanently on the session entry. All subsequent writes target that path.
- **Sequential numbering.** Every entry gets a monotonically increasing `seq` number per session, managed under a named Mutex.
- **Close with drain.** `trace session close` sets status to `"closing"` (under Mutex), waits up to `--wait-ms` (default: 5000 ms) for in-flight calls to complete, then marks the session `"closed"`. Any call that checks the session during `"closing"` or `"closed"` state silently drops its trace entry without failing the API call.
- **Reopenable.** A closed session can be reopened. Subsequent API calls append to the existing trace file, continuing the seq counter from where it left off.
- **Non-destructive reads.** `trace session export` reads the trace file and outputs JSON to stdout but never modifies or deletes it. `trace session remove` removes the index entry; the trace file is preserved by default.
- **No implicit session.** When no session is active, trace entries are silently dropped — no error, no file created.

### Session Lifecycle

```
start → active → closing → closed
                              ↑         ↓
                           reopen ←←←←←╯
```

| Status | Behaviour |
|--------|-----------|
| `active` | Session accepting new trace entries |
| `closing` | Session draining; any new entry is silently dropped; status set by `session close` |
| `closed` | Session sealed; can be reopened with `session reopen` |

### Storage Layout

```
<configDir>/
  traces/
    sessions.json              ← session index
  trace-config.json            ← default_export_path

<export_path>/                 ← user-specified or default; may be outside <configDir>
  <session-name>-<uuid-short>.json   ← JSONL; one JSON object per line; append-only; written live
```

> `<uuid-short>` = first 8 characters of the UUID. Example: `Desk-Analysis-c1a2b3d4.json`. If `--export-path` points directly to a file (path ends in `.json`), that exact path is used as-is.

### `sessions.json` Shape

```json
{
  "sessions": [
    {
      "unique_id": "c1a2b3d4-e5f6-7890-abcd-ef1234567890",
      "name": "Desk-Analysis",
      "start_time": "2026-03-20T10:00:00Z",
      "export_path": "/Users/vasanth/traces/Desk-Analysis-c1a2b3d4.json",
      "entry_count": 12,
      "status": "active"
    }
  ]
}
```

### `trace-config.json` Shape

```json
{
  "default_export_path": "/Users/vasanth/traces"
}
```

### `ApiTraceEntry` JSON shape

```json
{
  "seq": 1,
  "type": "api",
  "session": "Desk-Analysis",
  "session_id": "c1a2b3d4-e5f6-7890-abcd-ef1234567890",
  "timestamp": "2026-03-20T10:23:45.123Z",
  "duration_ms": 342,
  "account": "work",
  "method": "GET",
  "base_url": "https://desk.zoho.com",
  "url": "https://desk.zoho.com/api/v1/tickets",
  "request_headers": { "Content-Type": "application/json" },
  "request_body": null,
  "response_status": 200,
  "response_headers": { "Content-Type": "application/json" },
  "response_body": "{\"data\":[...]}",
  "error": null
}
```

### Security Header Filtering

The following headers are **never** written to the trace file. Both lists are compile-time constants in `TraceWriter` — not user-configurable for removal, but additional entries can be added in future stories.

**Request headers excluded:**

| Header | Reason |
|--------|--------|
| `Authorization` | Access token |
| `Cookie` | Session cookies that may carry auth tokens |
| `X-Auth-Token` | Common alternative auth header |
| `X-Api-Key` | API key authentication |

**Response headers excluded:**

| Header | Reason |
|--------|--------|
| `Set-Cookie` | Session token assignment |
| `WWW-Authenticate` | Auth challenge details |

### `ITraceSession` Contract

```csharp
public interface ITraceSession
{
    Task<TraceSessionEntry?> GetActiveSessionAsync(CancellationToken ct = default);
    Task<TraceSessionEntry> StartSessionAsync(string name, string exportPath, CancellationToken ct = default);
    Task CloseSessionAsync(string sessionId, int waitMs, CancellationToken ct = default);
    Task ReopenSessionAsync(string sessionId, CancellationToken ct = default);
    Task RemoveSessionAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<TraceSessionEntry>> ListSessionsAsync(CancellationToken ct = default);
    Task<int> IncrementEntryCountAsync(string sessionId, CancellationToken ct = default);
    Task<TraceSessionEntry?> FindByIdAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<TraceSessionEntry>> FindByNameAsync(string name, CancellationToken ct = default);
}
```

### Agent Workflow Integration

```sh
# One-time setup — persists across sessions:
zapi-cli trace config set --default-export-path ./traces

# Start a session (UUID auto-assigned):
zapi-cli trace session start --name "Desk-Analysis"
# → { "status": "ok", "data": { "unique_id": "c1a2b3d4-...", "name": "Desk-Analysis",
#       "export_path": "./traces/Desk-Analysis-c1a2b3d4.json", "start_time": "...", "status": "active" } }

# ... agent fires api calls — each writes to ./traces/Desk-Analysis-c1a2b3d4.json immediately ...

# Close the session (waits up to 5s for in-flight calls):
zapi-cli trace session close --id "c1a2b3d4-..."

# Inspect the trace (optional — file is already complete at export_path):
zapi-cli trace session export --id "c1a2b3d4-..." --type api

# Reopen to append more calls later:
zapi-cli trace session reopen --id "c1a2b3d4-..."
# ... more api calls ...
zapi-cli trace session close --id "c1a2b3d4-..."
```

### Export Command (Re-read Mode)

`trace session export` re-reads the trace file from disk and outputs to stdout as a JSON array. The trace file itself is always up to date (written live per call), so this command is only needed for filtered viewing.

| Flag | Description |
|------|-------------|
| `--truncate-body <chars>` | Truncate `request_body` and `response_body` per entry in output only (full content preserved in file) |
| `--type api\|pex` | Show only entries of the given type |

### Concurrency

- Named Mutex: `"Global\zapi-cli-trace-{sessionUniqueId}"` — scoped to the session UUID, not the name.
- `IncrementEntryCountAsync` reads, increments, and writes `sessions.json` under the Mutex, then returns the new `entry_count` as the seq value.
- Lock acquisition timeout: 2000 ms. On timeout, the trace entry is silently dropped — the API call continues normally.

---

## 11. Output Contract

All output goes through a central `IOutputWriter` interface so tests can capture it without console side effects.

### stdout — general rule

**No wrapper envelope.** Every command prints its result as plain JSON directly to stdout:

| Command category | stdout |
|---|---|
| **`api call`** | Raw Zoho API response body, byte-for-byte as received |
| **`account add/remove/re-auth`** | Raw Zoho API response body from the Zoho endpoint called (user-info, revoke, token-refresh) |
| **`account list/show/set-default`** | Plain JSON — just the data object/array, no wrapper |
| **`scope add/remove/list`** | Plain JSON — just the data object/array, no wrapper |
| **`trace` commands** | Plain JSON result of the trace operation |
| **`util` commands** | Plain JSON result (e.g. `{"ts": 1710000000000}`) |
| **`api registry` commands** | Plain JSON result |

Examples:

```
# api call (HTTP 200) — raw passthrough:
{"channels":[{"id":"ch_001","name":"general"}]}

# account add — raw Zoho user-info response:
{"ZPUID":"1234567890","Email":"user@example.com","Display_Name":"User Name"}

# account list — plain JSON array:
[
  {"name":"work","dc":"us","email":"user@example.com","is_default":true,"needs_reauth":false,"scope_count":2},
  {"name":"personal","dc":"eu","email":"user@personal.com","is_default":false,"needs_reauth":false,"scope_count":0}
]

# scope list — plain JSON array:
["ZohoCliq.Channels.READ","ZohoDesk.Tickets.WRITE"]

# util time-ms:
{"ts":1742256000000}
```

Token / client-secret values are **never** included in any output. `account show` displays `"token": "***"`.

### stdout — HTTP errors (`api call` and Zoho-calling account commands)

On HTTP 4xx / 5xx, the Zoho response body is still written to stdout as-is. The HTTP status is additionally reported on stderr as `API_ERROR`. Exit code is `1`.

```
stdout: {"code":"CHANNEL_NOT_FOUND","message":"Channel not found"}
stderr: {"error":"API returned 404 Not Found","code":"API_ERROR","httpStatus":404,"exitCode":1}
```

### stderr — Error Envelope

All pre-call and internal failures are written as a JSON error envelope to **stderr**. This covers:
- Errors that occur **before** any HTTP request is sent (host not allowed, ZohoCorp block, auth failure, invalid args)
- Errors retrieving or refreshing credentials
- CLI-level failures (file I/O, missing account, keychain errors)

```json
{ "error": "<human-readable message>", "code": "<ERROR_CODE>", "exitCode": <0|1|2> }
```

### Error Codes

| Code | Exit | Meaning |
|------|------|---------|
| `ACCOUNT_NOT_FOUND` | 1 | Named account does not exist |
| `ACCOUNT_ALREADY_EXISTS` | 1 | `account add` with duplicate name |
| `NO_DEFAULT_ACCOUNT` | 1 | No active account set and `--account` not provided |
| `AUTH_FAILURE` | 2 | Auto-refresh failed (invalid client credentials, revoked app, or keychain error) |
| `NEEDS_REAUTH` | 2 | Auto-refresh attempted but failed; account cannot be used until re-authenticated |
| `API_ERROR` | 1 | HTTP 4xx/5xx from Zoho API; response body still written to stdout |
| `INVALID_ARGS` | 1 | Missing or conflicting flags (e.g. missing `--client-id` at `account add`) |
| `IO_ERROR` | 1 | File system failure (accounts.json read/write) |
| `KEYCHAIN_ERROR` | 2 | OS keychain operation failed |
| `ACCOUNT_DOMAIN_BLOCKED` | 1 | Account email is a ZohoCorp domain |
| `EMAIL_REQUIRED` | 1 | User-info API returned no email; ZohoCorp check cannot be completed — `account add` aborted |
| `HOST_NOT_ALLOWED` | 1 | Outgoing URL host not in the allowlist |

### Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | General / recoverable error |
| `2` | Auth failure / needs-reauth |

---

## 12. Error Handling

- All exceptions are caught at the top-level command executor and converted to the JSON error envelope written to stderr.
- `--no-input` flag: any code path that would prompt must throw with code `INVALID_ARGS` instead.
- HTTP errors from `ApiClient` (`api call`): response body is always written to stdout as-is; a separate `API_ERROR` envelope is written to stderr with the HTTP status code; exit code 1.
- HTTP 401 from Zoho during `api call`: automatically attempt token refresh using stored client credentials; if successful, retry the request once and write the retried response body to stdout. If refresh also fails, emit `AUTH_FAILURE` to stderr, exit 2.
- `NeedsReauth == true` on `api call`: automatically trigger token refresh before executing the request (same flow as 401). If refresh fails, emit `AUTH_FAILURE` to stderr, exit 2.
- Any of `--token`, `--client-id`, `--client-secret` missing at `account add`: fail immediately with `INVALID_ARGS`.
- User-info fetch at `account add`: if the Zoho user-info API returns no email or ZUIDSTRING, `account add` must fail with `EMAIL_REQUIRED` — the ZohoCorp check is never skipped. Network/HTTP failure fetching user-info fails with `AUTH_FAILURE`.
- Cancellation (`Ctrl+C`): graceful cancellation via `CancellationTokenSource`; exit code `1`.

---

## 13. Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Spectre.Console.Cli` | latest stable | CLI command/subcommand/flag framework |
| `System.Text.Json` | (stdlib, .NET 10) | JSON serialization |
| `Microsoft.Extensions.DependencyInjection` | latest stable | DI container |
| `Microsoft.Extensions.Logging` | latest stable | Structured logging |
| `Microsoft.Extensions.Logging.Console` | latest stable | Console log sink |
| `xunit` | latest stable | Unit testing |
| `xunit.runner.visualstudio` | latest stable | Test runner integration |
| `Microsoft.NET.Test.Sdk` | latest stable | Test SDK |

No external keychain NuGet — platform keychain access is implemented via direct P/Invoke to keep the binary self-contained.

---

## 14. Non-Goals (v1)

- No browser-based auth or redirect flows — Self-Client OAuth only (user supplies token + client credentials).
- No WebSocket or Pex support (P3 — future).
- No API Registry (P2 — next phase).
- No scope management (P2 — next phase).
- No trace sessions (P2 — next phase).
- No TUI / interactive shell mode.
- No plugin system or extensibility hooks.
- No Native AOT publishing.
- No multi-account simultaneous API calls (one active account per invocation).
- No sync/caching layer — all API calls are live.
- No product-alias shortcuts for `--url` — full URL required on every `api call`.
