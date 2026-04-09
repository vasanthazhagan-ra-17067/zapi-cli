# PRD: zapi-cli — Zoho API CLI

## 1. Product overview

### 1.1 Document title and version

- PRD: zapi-cli — Zoho API CLI
- Version: 1.0
- Date: 2026-03-18

### 1.2 Product summary

**zapi-cli** is a standalone, multi-platform CLI binary for interacting with any Zoho product's REST APIs. It manages multiple Zoho accounts, handles OAuth authentication via a pluggable `IAuthProvider` interface, and exposes a general-purpose HTTP API invoker where the caller supplies the full base URL at invocation time.

The primary consumer is AI agents — specifically GitHub Copilot CLI Skills and Claude Agent Skills — that require a deterministic, scriptable interface to Zoho APIs without a browser or GUI. No existing tool combines Zoho auth management with general-purpose, module-agnostic API invocation in a single cross-platform binary.

zapi-cli ships as a self-contained single binary (`dotnet publish -r <rid> /p:PublishSingleFile=true --self-contained true`) targeting Windows, macOS, and Linux.

---

## 2. Goals

### 2.1 Business goals

- Provide a single, reusable CLI artifact that AI agents can invoke to interact with any Zoho product API.
- Eliminate the need for product-specific CLI tools or browser-based auth flows in agentic workflows.
- Ship as a GitHub Copilot CLI Skill and Claude Agent Skill package.

### 2.2 User goals

- Add and switch between multiple Zoho accounts without re-entering credentials each time.
- Invoke any Zoho REST API endpoint (any product, any region) with a single command.
- Get raw, machine-parseable JSON responses and errors suitable for AI agent consumption.

### 2.3 Non-goals (v1)

- No GUI or interactive TUI — all output is machine-readable JSON.
- No OAuth scope management UI (Phase 2).
- No trace session recording (Phase 2; see §11).
- No API registry (Phase 2).
- No WebSocket or Pex/real-time connection handling (Phase 3).
- No ZohoCorp (`@zohocorp.com`) account support — hard blocked by design.
- No support for non-Zoho API hosts — enforced via compile-time allowlist.

---

## 3. User personas

### 3.1 Key user types

- AI agent (primary) — automated caller via GitHub Copilot CLI Skill or Claude Agent Skill.
- Developer (secondary) — configures accounts, debugs API calls, writes agent scripts.

### 3.2 Basic persona details

- **AI Agent**: A non-interactive automated process that invokes `zapi-cli` subcommands programmatically to gather data from Zoho products (e.g., listing Cliq channels, fetching Desk tickets). Requires deterministic exit codes and JSON-only output.
- **Developer**: Sets up and maintains accounts, performs manual test calls during development, and troubleshoots authentication failures.

### 3.3 Role-based access

- **Any user / agent**: Full access to all `account`, `api`, and `util` commands. No privilege separation — the tool is local-only and single-user per machine.

---

## 4. Functional requirements

### Phase 1 (v1 — ship first)

**Account management** (Priority: P0)

- Add a new OAuth Self-Client account with `--name`, `--token`, `--client-id`, `--client-secret`, and optional `--dc` (default: `us`).
- On `account login`, call the Zoho user-info endpoint to fetch and store `email` and `zuid`; reject if either is absent.
- List all configured accounts with tokens masked.
- Show details for a specific account with secrets masked as `***`.
- Set a default/active account (`set-default`, alias: `use`).
- Remove an account: revoke the token via the Zoho OAuth revoke endpoint, clear keychain secrets, and delete the entry.
- Re-authenticate an account (`account refresh`): refresh the stored access token using saved `client-id` + `client-secret`.
- Auto-refresh token transparently on every `api request` when `needs_reauth == true` or a `401` is received — no manual intervention required.

**General-purpose API invocation** (Priority: P0)

- `api request` requires `--url` (full endpoint URL) and `--method` on every invocation; no URL derivation from the account `dc` field.
- Support `GET`, `POST`, `PUT`, `PATCH`, `DELETE` methods.
- Support inline JSON body via `--body` and file-based body via `--body-file` (mutually exclusive).
- Support repeatable `--header key:value` and `--query key=value` flags.
- Support per-call account override via `--account`.
- Inject `Authorization: Zoho-oauthtoken <token>` automatically.
- Return the raw Zoho API response body on `stdout`; report HTTP errors on `stderr`.

**Host allowlist** (Priority: P0)

- Validate every outgoing request URL host against a compile-time allowlist of allowed Zoho domain suffixes: `zoho.com`, `zoho.eu`, `zoho.in`, `zoho.com.au`, `zohoapis.com`, `zohoapis.in`.
- Reject any request to an unlisted host with `HOST_NOT_ALLOWED` (exit 1) before any network I/O.
- The allowlist is not configurable at runtime.

**ZohoCorp account restriction** (Priority: P0)

- Hard-block all operations on accounts whose email matches `@zohocorp.<any-tld>` (covers all datacenter variants).
- Block at the earliest entry point: `account login`, `api request`, and any `account scope` command.
- The block is not bypassable via flags, environment variables, or config files.
- If `account add` returns no email from the user-info API, abort with `EMAIL_REQUIRED` (exit 1).

**Utility commands** (Priority: P1)

- `util timestamp`: output current UTC time as a Unix millisecond timestamp as `{"ts": <value>}`.
- `util uuid`: generate and output a random UUID v4.

**Output contract** (Priority: P0)

- All successful output to `stdout` is plain JSON — no wrappers or envelopes.
  - `api request`, `account login/remove/refresh`: raw Zoho API response body.
  - Local commands (`account list/show/set-default`, `util`, `api endpoints`): plain JSON object or array.
- All pre-call errors go to `stderr` as a JSON envelope: `{ "error": "...", "code": "...", "exitCode": N }`.
- Exit codes: `0` = success, `1` = general / HTTP error, `2` = auth failure.
- `--json` flag defaults to `true`; `--no-input` flag prevents interactive prompts (always fail instead).

**Credential storage** (Priority: P0)

- Tokens, `client-id`, and `client-secret` stored in the OS-native keychain (macOS Security.framework, Windows Credential Manager, Linux libsecret).
- Fallback to AES-256-GCM encrypted file keychain when OS keychain is unavailable.
- No secrets ever written to `accounts.json`, `stdout`, logs, or error messages.
- `accounts.json` written with `0600` permissions on Unix; NTFS ACL-restricted to current user on Windows.

---

## 5. User experience

### 5.1 Entry points & first-time user flow

- User installs the single binary and runs `zapi account login` to configure their first account.
- The tool fetches the user-info from Zoho to validate the token and store `email`/`zuid` automatically.
- After adding an account, `zapi api request --url <url> --method GET` works immediately.

### 5.2 Core experience

- **Account setup**: `zapi-cli account add --name "work" --token "xxx" --client-id "yyy" --client-secret "zzz"` — validates token against Zoho, stores secrets in keychain, persists metadata to `accounts.json`.
- **API invocation**: `zapi api request --url "https://cliq.zoho.com/api/v2/channels" --method GET` — resolves active account, injects auth, returns raw Zoho JSON to stdout.
- **Token refresh**: On `401` or `needs_reauth == true`, the token is refreshed silently and the call is retried — the agent receives the result without any interruption.
- **Account switching**: `zapi-cli account set-default --name "personal"` or `--account <name>` on any individual call.

### 5.3 Advanced features & edge cases

- `--body` and `--body-file` are mutually exclusive; using both fails fast with `INVALID_ARGS`.
- If no default account is set and no `--account` is provided, the command fails with a clear error.
- Calls to non-Zoho hosts are rejected before any network I/O.
- ZohoCorp accounts already present in `accounts.json` (e.g., migrated from another tool) are rejected on use with a startup warning.
- `account remove` revokes the server-side token via the Zoho OAuth revoke endpoint before clearing local storage.

### 5.4 UI/UX highlights

- All output is JSON — no prose, colors, or interactive prompts in non-TTY contexts.
- Error codes are machine-readable constants (`AUTH_FAILURE`, `HOST_NOT_ALLOWED`, `ACCOUNT_DOMAIN_BLOCKED`, etc.).
- `--no-input` flag ensures the tool never blocks waiting for keyboard input in agent contexts.

---

## 6. Narrative

An AI agent working in a GitHub Copilot session needs to fetch open support tickets from Zoho Desk while also pulling related Cliq channel messages. The agent invokes `zapi api request` twice — once for each product — using the pre-configured `work` account. No browser window opens, no token is typed, and no product-specific SDK is needed. The agent receives clean JSON it can reason over immediately. When the token silently expires mid-session, zapi refreshes it automatically and the agent never notices. The developer who set up the account months earlier never has to touch it again.

---

## 7. Success metrics

### 7.1 User-centric metrics

- Time from binary install to first successful `api request` < 3 minutes.
- Zero interactive prompts needed during agent-driven workflows (`--no-input` safe by default).
- Token refresh succeeds transparently on `401` with no agent-visible failure.

### 7.2 Business metrics

- Adopted as the standard Zoho API invocation mechanism for internal GitHub Copilot and Claude Agent Skills.
- Covers all major Zoho datacenter regions (US, EU, IN, AU, CN, JP, SA, UK, CA) at launch.

### 7.3 Technical metrics

- Binary size: self-contained single-file publish under 100 MB per platform.
- `api request` round-trip overhead (excluding network): < 50 ms.
- Zero secrets written to disk in plaintext, confirmed by test suite.
- 100% of `HOST_NOT_ALLOWED` and `ACCOUNT_DOMAIN_BLOCKED` cases caught before network I/O.

---

## 8. Technical considerations

### 8.1 Integration points

- Zoho OAuth 2.0 token endpoint (per datacenter) for token storage and refresh.
- Zoho user-info endpoint (`GET /oauth/user/info`) used at `account login` time to validate identity and fetch `email` + `zuid`.
- Zoho OAuth revoke endpoint called on `account remove`.
- OS keychain APIs: macOS Security.framework, Windows Credential Manager, Linux libsecret — via P/Invoke.

### 8.2 Data storage & privacy

- `accounts.json` stored at `~/Library/Application Support/zapi/` (macOS), `%LOCALAPPDATA%\zapi\` (Windows), `~/.config/zapi/` (Linux) with `0600` / ACL permissions.
- Secrets (access token, `client-id`, `client-secret`) stored exclusively in the OS keychain under key `zapi:<accountName>:oauth`.
- No token values are ever present in stdout, stderr, log files, or error messages.

### 8.3 Scalability & performance

- Stateless HTTP client per invocation — no persistent daemon or background process.
- Multiple accounts supported; one is active by default with per-command override.
- Designed for AI agent use at high invocation frequency without shared state issues.

### 8.4 Potential challenges

- OS keychain availability varies (headless CI, containers, WSL); fallback AES-256-GCM encrypted file keychain mitigates this.
- Zoho user-info endpoint requires `AaaServer.profile.READ` scope to return both `email` and `zuid`; token validation at `account login` must account for this.
- Atomic `accounts.json` write must prevent data corruption on concurrent invocations.

---

## 9. Milestones & sequencing

### 9.1 Project estimate

- Phase 1 (v1): Medium — 2–4 weeks

### 9.2 Team size & composition

- Small team: 1–2 engineers

### 9.3 Suggested phases

- **Phase 1** (v1 — this PRD): Account management, `api request`, utility commands, host allowlist, ZohoCorp block (2–4 weeks)
  - Account CRUD + keychain integration
  - `api request` with auto token-refresh
  - Host allowlist enforcement
  - ZohoCorp domain block
  - Utility commands (`timestamp`, `uuid`)
  - Output contract + exit codes
  - Unit tests for `AccountStore`, `OAuthProvider`, `ApiClient`

- **Phase 2** (future): Scope management (`account scope add/list`), trace sessions (`trace start/list/export/close/reopen/remove` + `trace config set/show`), API endpoints (`api endpoints list/add/show/remove`)

- **Phase 3** (future): Generic WebSocket connections (`ws` group), Pex/WMS real-time protocol (`pex` group)

---

## 10. User stories

### 10.1. Add an OAuth account

- **ID**: UC-001
- **Description**: As an agent or developer, I want to add a Zoho OAuth Self-Client account so that I can authenticate and make API calls.
- **Acceptance criteria**:
  - `zapi-cli account add --name "work" --token "xxx" --client-id "yyy" --client-secret "zzz"` succeeds when all required flags are present.
  - `--dc` defaults to `us` when omitted; accepts `us|eu|in|au|cn|jp|sa|uk|ca`.
  - The Zoho user-info endpoint is called to fetch `email` and `zuidstring` before persisting the account.
  - If user-info returns no email, the command fails with `EMAIL_REQUIRED` (exit 1) and nothing is written to keychain or `accounts.json`.
  - If the email matches `@zohocorp.<any-tld>`, the command fails with `ACCOUNT_DOMAIN_BLOCKED` (exit 1) and nothing is written.
  - On success, secrets are stored in the OS keychain under `zapi-cli:work:oauth` and metadata is written to `accounts.json`.
  - If this is the first account, it is automatically set as default.
  - Output: `{ "status": "ok", "data": { "name": "work", "dc": "us" } }` to stdout.

### 10.2. List configured accounts

- **ID**: UC-002
- **Description**: As an agent or developer, I want to list all configured accounts so that I can see what is available.
- **Acceptance criteria**:
  - `zapi-cli account list` outputs a JSON array of all accounts.
  - Token and secret values are masked as `***` or omitted.
  - Returns an empty array `[]` when no accounts are configured.

### 10.3. Show account details

- **ID**: UC-003
- **Description**: As a developer, I want to view the details of a specific account so that I can verify its configuration.
- **Acceptance criteria**:
  - `zapi-cli account show --name "work"` outputs a JSON object with all metadata for the named account.
  - Token and secrets are masked as `***`.
  - Fails with `ACCOUNT_NOT_FOUND` (exit 1) if the account does not exist.

### 10.4. Set default account

- **ID**: UC-004
- **Description**: As an agent or developer, I want to set a default account so that subsequent commands use it without specifying `--account` every time.
- **Acceptance criteria**:
  - `zapi-cli account set-default --name "work"` sets `is_default = true` for the named account and `false` for all others.
  - Fails with `ACCOUNT_NOT_FOUND` (exit 1) if the account does not exist.
  - Output: `{ "status": "ok", "data": { "name": "work" } }`.

### 10.5. Remove an account

- **ID**: UC-005
- **Description**: As a developer, I want to remove an account so that its credentials are revoked and deleted locally.
- **Acceptance criteria**:
  - `zapi-cli account remove --name "work"` calls the Zoho OAuth revoke endpoint before clearing local state.
  - Keychain entry `zapi-cli:work:oauth` is deleted.
  - Account entry is removed from `accounts.json`.
  - If the removed account was the default and other accounts remain, no implicit default is selected (must be set manually).
  - Fails with `ACCOUNT_NOT_FOUND` (exit 1) if the account does not exist.

### 10.6. Re-authenticate an account

- **ID**: UC-006
- **Description**: As a developer, I want to manually force a token refresh for an account so that I can recover from credential issues.
- **Acceptance criteria**:
  - `zapi-cli account re-auth --name "work"` reads stored `client-id` + `client-secret` from OS keychain.
  - Calls the Zoho OAuth token endpoint using stored credentials and current scopes.
  - Stores the new access token in the OS keychain.
  - Sets `needs_reauth = false` in `accounts.json`.
  - Fails with `AUTH_FAILURE` (exit 2) if the refresh fails (revoked app, bad credentials).
  - Output: `{ "status": "ok", "data": { "name": "work" } }`.

### 10.7. Make a GET API call

- **ID**: UC-007
- **Description**: As an agent, I want to make a GET request to any Zoho API endpoint so that I can retrieve data.
- **Acceptance criteria**:
  - `zapi-cli api call --url "https://cliq.zoho.com/api/v2/channels" --method GET` sends the request with `Authorization: Zoho-oauthtoken <token>`.
  - Raw Zoho API response body is written to stdout.
  - HTTP errors (4xx/5xx) are reported on stderr as `{ "error": "...", "code": "API_ERROR", "exitCode": 1 }` while the response body is still written to stdout.
  - Uses the active/default account unless `--account` is provided.

### 10.8. Make a POST API call with an inline body

- **ID**: UC-008
- **Description**: As an agent, I want to send a POST request with an inline JSON body to create or update a Zoho resource.
- **Acceptance criteria**:
  - `zapi-cli api call --url "..." --method POST --body '{"data":[...]}' ` sends `Content-Type: application/json` and the given body.
  - `--body` and `--body-file` cannot be used together; doing so fails with `INVALID_ARGS` (exit 1).

### 10.9. Make an API call with a body file

- **ID**: UC-009
- **Description**: As an agent, I want to supply the request body from a file so that I can send larger or pre-prepared payloads.
- **Acceptance criteria**:
  - `zapi-cli api call --url "..." --method POST --body-file ./req.json` reads the file and sends its contents as the request body.
  - Fails with `INVALID_ARGS` (exit 1) if the file does not exist.

### 10.10. Make an API call with custom headers and query parameters

- **ID**: UC-010
- **Description**: As an agent, I want to append custom headers and query parameters to an API call so that I can meet endpoint-specific requirements.
- **Acceptance criteria**:
  - `--header "X-Custom: val"` (repeatable) injects additional headers into the request.
  - `--query "status=open"` (repeatable) appends query parameters to the URL.
  - The `Authorization` header cannot be overridden via `--header`.

### 10.11. Override account per call

- **ID**: UC-011
- **Description**: As an agent, I want to override the active account for a single call so that I can make requests on behalf of a specific account without changing the global default.
- **Acceptance criteria**:
  - `zapi-cli api call --url "..." --method GET --account "personal"` uses the `personal` account for that invocation only.
  - Fails with `ACCOUNT_NOT_FOUND` (exit 1) if the named account does not exist.

### 10.12. Automatic token refresh on 401

- **ID**: UC-012
- **Description**: As an agent, I want the CLI to refresh an expired token automatically so that my workflow is not interrupted by credential expiry.
- **Acceptance criteria**:
  - When `api call` receives a `401` from Zoho, the token is refreshed using stored `client-id` + `client-secret` and the call is retried once.
  - If the refresh fails, `AUTH_FAILURE` (exit 2) is reported on stderr; no retry loop.
  - The agent receives the final result (success or failure) transparently.

### 10.13. Reject a request to a non-Zoho host

- **ID**: UC-013
- **Description**: As a security control, the CLI must prevent requests to hosts not on the Zoho domain allowlist.
- **Acceptance criteria**:
  - Any `--url` whose host does not end with `zoho.com`, `zoho.eu`, `zoho.in`, `zoho.com.au`, `zohoapis.com`, or `zohoapis.in` is rejected with `HOST_NOT_ALLOWED` (exit 1) before any network I/O.
  - The allowlist cannot be overridden at runtime by any flag or environment variable.

### 10.14. Block ZohoCorp accounts at add time

- **ID**: UC-014
- **Description**: As a security control, the CLI must refuse to add or use accounts whose email belongs to the ZohoCorp corporate domain.
- **Acceptance criteria**:
  - `account add` with a `@zohocorp.com` (or any `@zohocorp.<tld>`) token is rejected with `ACCOUNT_DOMAIN_BLOCKED` (exit 1); no keychain write occurs.
  - `api call` and any `scope` command resolving to a ZohoCorp account is rejected before any HTTP request.
  - The check is applied even to accounts already present in `accounts.json` (e.g., migrated entries).
  - The block cannot be bypassed by any flag, environment variable, or config file.

### 10.15. Get current UTC timestamp in milliseconds

- **ID**: UC-015
- **Description**: As an agent, I want to retrieve the current UTC time as a millisecond Unix timestamp so that I can generate time-correlated request parameters.
- **Acceptance criteria**:
  - `zapi-cli util time-ms` outputs `{"ts": <unix-ms>}` to stdout.
  - Exit code is `0`.

### 10.16. Generate a UUID v4

- **ID**: UC-016
- **Description**: As an agent, I want to generate a random UUID v4 so that I can use it as a correlation or idempotency key.
- **Acceptance criteria**:
  - `zapi-cli util uuid` outputs a valid UUID v4 string in standard format.
  - Exit code is `0`.

### 10.17. Non-interactive mode for agent use

- **ID**: UC-017
- **Description**: As an agent, I want the CLI to never block waiting for user input so that automated workflows never stall.
- **Acceptance criteria**:
  - When `--no-input` is set (or when running in a non-TTY context), any operation that would prompt the user instead fails immediately with a descriptive error on stderr.
  - Exit code is `1`.

---

## 11. Future phases (minimal)

### Phase 2

- **Scope management** (`scope add/remove/list`): Add and remove OAuth scopes per account; setting `needs_reauth = true` triggers auto-refresh on the next `api call`.
- **Trace sessions** (`trace session start/list/export/close/reopen/remove` + `trace config set/show`): Session-scoped live trace of all `api call` invocations, written to disk on every call. Each session has a UUID (primary key, immutable) and a human-readable name (non-unique). The trace file is written live at the user-configured export path — no deferred flush needed. `trace session close` waits for in-flight API calls to drain (configurable timeout) before sealing. Sessions can be reopened to append further calls to the existing trace file. A default export directory can be set globally via `trace config set`.
- **API registry** (`api registry list/add/show/remove`): Persistent local registry of known API endpoints (`id`, `url`, `method`, `purpose`) for agent discovery.

### Phase 3

- **WebSocket connections** (`ws connect/send/listen/close`): Generic named WebSocket sessions for Zoho real-time endpoints.
- **Pex / real-time chat** (`pex connect/send/drain/clear/listen/close`): Zoho Cliq proprietary Pex/WMS real-time protocol; events buffered to a per-account JSONL file and drained on demand.
>
> **Contents summary:** Problem statement, target user (AI agents / CLI power users), v1 scope (account management, api call, trace system, api registry, util commands), v2 scope (mobile login, incremental auth, keychain credentials), v3 deferred (team sharing, remote trace storage). Full requirements list with MoSCoW prioritization.

See: [`tech_spec/prd.md`](../../tech_spec/prd.md)
