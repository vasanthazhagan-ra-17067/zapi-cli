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
- v1: Account management (PAT auth), scope management, REST API invocation (`--base-url` required), trace sessions, utility commands
- Future: OAuth2 re-auth, WebSocket connections, Pex real-time chat, API Registry

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
│   │       ├── ApiCommands.cs                ← api call (--base-url required)
│   │       ├── ApiRegistryCommands.cs        ← api registry list/add/show/remove
│   │       ├── PexCommands.cs                ← pex connect/send/drain/clear/listen/close
│   │       ├── TraceCommands.cs              ← trace session start/list/export/close/remove
│   │       └── UtilCommands.cs               ← util time-ms, util uuid
│   │
│   ├── ZapiCli.Core/                         ← Domain logic (no CLI concerns)
│   │   ├── ZapiCli.Core.csproj
│   │   ├── Auth/
│   │   │   ├── IAuthProvider.cs
│   │   │   └── PatAuthProvider.cs
│   │   ├── Accounts/
│   │   │   ├── AccountStore.cs               ← JSON config read/write
│   │   │   └── AccountConfig.cs              ← AccountEntry model + AccountsRoot DTO
│   │   ├── Api/
│   │   │   ├── ApiClient.cs                  ← HttpClient wrapper; injects auth; host allowlist; writes trace entries
│   │   │   └── ApiRegistry.cs                ← registry.json read/write (entries carry baseUrl field)
│   │   ├── Pex/
│   │   │   └── PexBuffer.cs                  ← per-account JSONL event buffer (future)
│   │   └── Trace/
│   │       ├── TraceSession.cs               ← session index read/write (sessions.json)
│   │       ├── TraceWriter.cs                ← append-only JSONL writer for trace entries
│   │       ├── TraceEntry.cs                 ← record types: ApiTraceEntry (carries baseUrl), PexTraceEntry
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
        ├── PatAuthProviderTests.cs
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
| `add` | `--name` (req), `--auth-type pat\|oauth` (req), `--token` (req when `--auth-type pat`), `--domain` (default: `zoho.com`) | Add a new account |
| `list` | — | List all configured accounts (token masked) |
| `remove` | `--name` (req) | Remove account + clear keychain secret |
| `show` | `--name` (req) | Show account details (token masked as `***`) |
| `set-default` | `--name` (req) | Set active/default account |
| `re-auth` | `--name` (req) | Re-authenticate after scope change *(OAuth — v2)* |

#### `account add` flag rules

| `--auth-type` | `--token` | Behaviour |
|---------------|-----------|-----------|
| `pat` | Required | Token stored in OS keychain via `PatAuthProvider` |
| `oauth` | Not accepted (error if supplied) | OAuth PKCE flow triggered automatically using compile-time credentials |

> **Note:** OAuth client credentials (`client_id`, `client_secret`) are compile-time constants in `OAuthProvider.cs`. Replace `PLACEHOLDER_CLIENT_ID` and `PLACEHOLDER_CLIENT_SECRET` before distributing the binary.

---

### Group: `scope`

All scope subcommands target an account. `--account` defaults to the active account if omitted.

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `add` | `--scope` (req), `[--account]` | Add an OAuth scope to an account |
| `remove` | `--scope` (req), `[--account]` | Remove an OAuth scope from an account |
| `list` | `[--account]` | List all scopes for an account |

Adding or removing a scope sets `needs_reauth = true` on that account.

---

### Group: `api`

#### `api call`

`--base-url` is **required** on every invocation. The final request URL is assembled as `<base-url><path>`.

| Flag | Required | Type | Description |
|------|----------|------|-------------|
| `--base-url` | **Yes** | string | Root URL of the target Zoho product API (e.g. `https://desk.zoho.com/api/v1`) |
| `--method` | Yes | `GET\|POST\|PUT\|PATCH\|DELETE` | HTTP method |
| `--path` | Yes | string | Resource path relative to `--base-url` (e.g. `/tickets`) |
| `--body` | No | string | Inline JSON request body |
| `--body-file` | No | string | Path to a JSON file to use as request body |
| `--header` | No | `key:value` | Additional request header (repeatable) |
| `--query` | No | `key=value` | Query string parameter (repeatable) |
| `--account` | No | string | Account override for this call |

`--body` and `--body-file` are mutually exclusive.

**Examples:**

```sh
zapi-cli api call --base-url "https://cliq.zoho.com/api/v2" --method GET --path "/channels"
zapi-cli api call --base-url "https://desk.zoho.com/api/v1" --method GET --path "/tickets"
zapi-cli api call --base-url "https://crm.zoho.com/crm/v5" --method POST --path "/Leads" --body '{"data":[{"Last_Name":"Doe"}]}'
```

#### `api registry` *(Future)*

Each registry entry carries a `baseUrl` field since entries can span multiple Zoho products.

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `list` | — | List all registered API entries |
| `add` | `--id` (req), `--base-url` (req), `--method` (req), `--url-template` (req), `--purpose` (req) | Upsert an endpoint entry (add or overwrite by id) |
| `show` | `--id` (req) | Show a single entry by id |
| `remove` | `--id` (req) | Delete an entry by id |

Storage: `<configDir>/zapi-cli/registry.json`

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

> Session-scoped API call trace. One analysis run = one named session. `api call` and `pex drain` automatically append entries when a session is active — no extra flag required.

| Subcommand | Flags | Description |
|-----------|-------|-------------|
| `session start` | `--name` (req) | Create and activate a named trace session |
| `session list` | — | List all sessions: name, start time, entry count, status |
| `session export` | `--name` (req), `[--truncate-body <bytes>]`, `[--type api\|pex]`, `[--product <name>]` | Dump full session trace to stdout as JSON array (non-destructive) |
| `session close` | `--name` (req) | Mark session inactive; file preserved for export |
| `session remove` | `--name` (req) | Delete session entry and all trace files |

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
    public required string Domain { get; init; }           // e.g. "zoho.com" — used for OAuth token endpoints only
    public string? Email { get; init; }                    // captured from Zoho user-info API at account add
    public List<string> Scopes { get; init; } = [];
    public required string TokenType { get; init; }        // "pat" | "oauth"
    public bool IsDefault { get; init; }
    public bool NeedsReauth { get; init; }
}
```

> `Domain` is used for OAuth token endpoint resolution only. It is **not** used to derive an API base URL — the caller supplies `--base-url` directly on every `api call`.

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

JSON property names use `snake_case` (configured via `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`).

### Trace Entry Models

**`ApiTraceEntry`** — written by every `api call` when a session is active:

```csharp
public sealed record ApiTraceEntry
{
    public int Seq { get; init; }
    public string Type => "api";
    public required string Session { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public int DurationMs { get; init; }
    public required string Account { get; init; }
    public required string Method { get; init; }
    public required string BaseUrl { get; init; }          // recorded for cross-product traceability
    public required string Url { get; init; }              // full assembled URL
    public Dictionary<string, string> RequestHeaders { get; init; } = [];   // Authorization excluded
    public string? RequestBody { get; init; }
    public int ResponseStatus { get; init; }
    public Dictionary<string, string> ResponseHeaders { get; init; } = [];
    public string? ResponseBody { get; init; }
    public string? Error { get; init; }                    // non-null on transport failure only
}
```

**`PexTraceEntry`** — written for each event when `pex drain` is called with a session active:

```csharp
public sealed record PexTraceEntry
{
    public int Seq { get; init; }
    public string Type => "pex";
    public required string Session { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Account { get; init; }
    public int? RelatedApiSeq { get; init; }    // seq of most recent api entry before this drain; null if none
    public required string HandlerClass { get; init; }
    public required string CallbackMethod { get; init; }
    public required string Payload { get; init; }
}
```

> `RequestHeaders` in `ApiTraceEntry` always excludes the `Authorization` header — tokens are **never** written to the trace.

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

### v1 — `PatAuthProvider`

- Implements `IAuthProvider`.
- `StoreTokenAsync` → writes to OS keychain under key `zapi-cli:<accountName>:pat`.
- `GetTokenAsync` → reads from OS keychain by the same key.
- `ClearTokenAsync` → deletes from OS keychain.
- Token injected into requests as: `Authorization: Zoho-oauthtoken <token>`.

### v2 (Future) — `OAuthProvider`

```csharp
// TODO: Replace placeholders before distributing the binary
private const string ClientId = "PLACEHOLDER_CLIENT_ID";
private const string ClientSecret = "PLACEHOLDER_CLIENT_SECRET";
```

- OAuth client credentials are **compile-time constants** — not user input, not runtime config.
- PKCE flow runs entirely in terminal (no browser).
- Access + refresh tokens stored in OS keychain under `zapi-cli:<accountName>:oauth`.

### `account add` Flow

#### PAT path (`--auth-type pat`)

```
zapi-cli account add --name "work" --auth-type pat --token "xxx" [--domain "zoho.com"]
  1. Validate --token is present (error INVALID_ARGS if missing)
  2. Validate name is unique in accounts.json
  3. Call GET https://accounts.{domain}/oauth/user/info with the PAT as bearer token; abort with EMAIL_REQUIRED if no email returned; apply ZohoCorp domain check
  4. Call PatAuthProvider.StoreTokenAsync("work", "xxx")
  5. Append AccountEntry (token_type = "pat") to accounts.json
  6. If no other account exists, set is_default = true
  7. Write accounts.json (permissions: 0600 on Unix)
  8. Output: { "status": "ok", "data": { "name": "work", "domain": "zoho.com", "auth_type": "pat" } }
```

#### OAuth path (`--auth-type oauth`)

```
zapi-cli account add --name "work" --auth-type oauth [--domain "zoho.com"]
  1. Reject if --token is supplied (error INVALID_ARGS)
  2. Validate name is unique in accounts.json
  3. Initiate OAuth PKCE flow using hardcoded ClientId / ClientSecret; AaaServer.profile.READ mandated in scope request
  4. A token is received upon successful OAuth PKCE flow completion
  5. Call GET https://accounts.{domain}/oauth/user/info with the received token; abort with EMAIL_REQUIRED if no email returned
  6. Apply ZohoCorp domain check on the returned email; abort with ACCOUNT_DOMAIN_BLOCKED if blocked
  7. Store access + refresh tokens via OAuthProvider
  8. Append AccountEntry (token_type = "oauth") to accounts.json
  9. Write accounts.json
  10. Output: { "status": "ok", "data": { "name": "work", "domain": "zoho.com", "auth_type": "oauth" } }
```

### Scope Change + Re-auth Flow

```
zapi-cli scope add --scope "ZohoDesk.Tickets.READ" [--account "work"]
  1. Resolve target account (--account or default)
  2. Add scope to AccountEntry.Scopes if not already present
  3. Set AccountEntry.NeedsReauth = true
  4. Write accounts.json
  5. Output: { "status": "ok", "data": { "account": "work", "scopes": [...] } }

Next api call on account "work":
  1. Load AccountEntry for "work"
  2. If NeedsReauth == true → write error to stderr + exit 2

zapi-cli account re-auth --name "work"   [v2 OAuth only]
  1. Exchange new token via Zoho OAuth API using OAuthProvider
  2. Store updated tokens in OS keychain
  3. Set AccountEntry.NeedsReauth = false
  4. Write accounts.json
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

- **PAT path**: After the token is supplied, `GET https://accounts.{domain}/oauth/user/info` is called with `Authorization: Zoho-oauthtoken <token>` to retrieve the account email before the account is persisted.
- **OAuth path**: Upon successful PKCE flow completion, an OAuth access token is received. That token is immediately used to call `GET https://accounts.{domain}/oauth/user/info`. `AaaServer.profile.READ` is mandated in the OAuth scope request to guarantee the email claim is returned.
- **Fail closed**: If the user-info endpoint returns no email (for any auth type), `account add` must abort with `EMAIL_REQUIRED` (exit 1). The ZohoCorp check is never skipped — a missing email is treated as an unverifiable identity.
- The domain-block check also runs at the start of `ApiClient.CallAsync` against the stored account email.
- The check must NOT be bypassable via flags, environment variables, or config.

### Use Cases

| ID | Scenario | Behaviour |
|----|----------|-----------|
| UC-21 | `account add` with a `@zohocorp.com` PAT | Rejected immediately; keychain write never happens; exit 1 `ACCOUNT_DOMAIN_BLOCKED` |
| UC-22 | `api call` resolves to a ZohoCorp account | Rejected before any HTTP request; exit 1 `ACCOUNT_DOMAIN_BLOCKED` |
| UC-23 | `scope add/remove/list` targets a ZohoCorp account | Rejected; no mutation to `accounts.json`; exit 1 `ACCOUNT_DOMAIN_BLOCKED` |
| UC-24 | Existing `accounts.json` already contains a ZohoCorp account | Any command resolving to that account is rejected; warning emitted on startup |
| UC-25 | `account add` (OAuth path) where user-info returns no email | Rejected before any token storage; exit 1 `EMAIL_REQUIRED` |

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
| Trace entries | `<configDir>/traces/<session-name>/trace.jsonl` | Newline-delimited JSON (append-only) |
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

// Key format: "zapi-cli:<accountName>:<tokenType>"
// e.g.        "zapi-cli:work:pat"
//             "zapi-cli:work:oauth"
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

The base URL is fully supplied by the caller via `--base-url`. There is no URL derivation from the account's `Domain` field.

```csharp
public sealed class ApiClient
{
    // Assembles final URL as: <baseUrl><path>
    // Validates the host against the allowlist before sending
    // Injects: Authorization: Zoho-oauthtoken <token>
    // Injects: Content-Type: application/json  (for POST/PUT/PATCH)
    // Appends trace entry to active session (if any)

    public Task<ApiResponse> CallAsync(ApiRequest request, CancellationToken ct = default);
}

public sealed record ApiRequest
{
    public required string BaseUrl { get; init; }          // supplied by --base-url
    public required string Method { get; init; }
    public required string Path { get; init; }
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

### URL Assembly

```
--base-url "https://desk.zoho.com/api/v1"
--path     "/tickets"
→ Final URL: "https://desk.zoho.com/api/v1/tickets"

--base-url "https://cliq.zoho.com/api/v2"
--path     "/channels"
→ Final URL: "https://cliq.zoho.com/api/v2/channels"
```

---

## 10. Trace Sessions

> During an API analysis session the agent fires API calls and (optionally) drains Pex events. All calls are automatically recorded in a named session trace. After the session the agent exports the trace for developer reference.

### Design Principles

- **Always-on when a session is active.** `api call` and `pex drain` automatically append entries — no `--trace` flag to forget.
- **Named sessions.** One analysis run = one session identified by a developer-readable name.
- **Non-destructive export.** `trace session export` writes to stdout but does not delete the file; only `trace session remove` deletes it.
- **Sequential numbering.** Every entry gets a monotonically increasing `seq` number.
- **Fallback when no session is active.** Trace entries are silently dropped — no implicit session created.

### Storage Layout

```
<configDir>/zapi-cli/traces/
  sessions.json                 ← index: name, startTime, entryCount, status (active | closed)
  <session-name>/
    trace.jsonl                 ← one JSON object per line (append-only)
```

### `ApiTraceEntry` JSON shape

```json
{
  "seq": 1,
  "type": "api",
  "session": "Desk-Tickets-2026-03-17",
  "timestamp": "2026-03-17T10:23:45.123Z",
  "duration_ms": 342,
  "account": "work",
  "method": "GET",
  "base_url": "https://desk.zoho.com/api/v1",
  "url": "https://desk.zoho.com/api/v1/tickets",
  "request_headers": { "Content-Type": "application/json" },
  "request_body": null,
  "response_status": 200,
  "response_headers": { "Content-Type": "application/json" },
  "response_body": "{\"data\":[...]}",
  "error": null
}
```

### Agent Workflow Integration

```
Session start:
  zapi-cli trace session start --name "<FeatureName>-<YYYY-MM-DD>"

... agent fires api calls — entries auto-appended ...

Session complete:
  zapi-cli trace session export --name "<FeatureName>-<YYYY-MM-DD>"
    → agent captures stdout → saves to api-trace.json

  zapi-cli trace session close --name "<FeatureName>-<YYYY-MM-DD>"
```

### Export Options

| Flag | Description |
|------|-------------|
| `--truncate-body <bytes>` | Truncate `request_body` and `response_body` per entry at export time (full fidelity preserved in `.jsonl`) |
| `--type api\|pex` | Export only entries of the given type |
| `--product <name>` | Export only entries where `base_url` matches a registered product alias (requires API Registry) |

---

## 11. Output Contract

All output goes through a central `IOutputWriter` interface so tests can capture it without console side effects.

### stdout — Success

```json
{ "status": "ok", "data": <raw API response or command result> }
```

For `account list`:

```json
{
  "status": "ok",
  "data": [
    { "name": "work", "domain": "zoho.com", "token_type": "pat", "is_default": true, "needs_reauth": false, "scope_count": 2 },
    { "name": "personal", "domain": "zoho.eu", "token_type": "pat", "is_default": false, "needs_reauth": false, "scope_count": 0 }
  ]
}
```

Token value is **never** included in any output. `account show` displays `"token": "***"`.

### stderr — Error Envelope

```json
{ "error": "<human-readable message>", "code": "<ERROR_CODE>", "exitCode": <0|1|2> }
```

### Error Codes

| Code | Exit | Meaning |
|------|------|---------|
| `ACCOUNT_NOT_FOUND` | 1 | Named account does not exist |
| `ACCOUNT_ALREADY_EXISTS` | 1 | `account add` with duplicate name |
| `NO_DEFAULT_ACCOUNT` | 1 | No active account set and `--account` not provided |
| `AUTH_FAILURE` | 2 | Keychain read failed or token rejected by API |
| `NEEDS_REAUTH` | 2 | Account has pending scope changes |
| `API_ERROR` | 1 | Non-2xx response from Zoho API |
| `INVALID_ARGS` | 1 | Missing or conflicting flags (e.g. `--token` with `--auth-type oauth`) |
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
- HTTP errors from `ApiClient`: non-2xx responses are converted to `API_ERROR` with the response body included in `"detail"`.
- `--token` supplied when `--auth-type oauth`: fail immediately with `INVALID_ARGS`.
- `--token` missing when `--auth-type pat`: fail immediately with `INVALID_ARGS`.
- User-info fetch at `account add` (both PAT and OAuth): if the Zoho user-info API returns no email, `account add` must fail with `EMAIL_REQUIRED` — the ZohoCorp check is never skipped. Network/HTTP failure fetching user-info fails with `AUTH_FAILURE`.
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

- No OAuth2 implementation — PAT only (`--auth-type pat`).
- No browser-based auth or redirect flows.
- No WebSocket or Pex support (future Epics 4 & 6).
- No API Registry (future Epic 7).
- No TUI / interactive shell mode.
- No plugin system or extensibility hooks.
- No Native AOT publishing.
- No multi-account simultaneous API calls (one active account per invocation).
- No sync/caching layer — all API calls are live.
- `trace` and `util` commands are implemented in v1; `pex`, `ws`, and `api registry` are deferred.
- No product-alias shortcuts for `--base-url` — full URL required on every `api call` in v1.
