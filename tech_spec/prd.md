# PRD: zapi-cli

## 1. Product overview

### 1.1 Document title and version

- PRD: zapi-cli — Zoho API CLI for AI Agents
- Version: 1.0.0
- Date: 2026-03-17

### 1.2 Product summary

zapi-cli is a standalone, multi-platform CLI binary that enables AI agents to interact programmatically with any Zoho product's REST APIs. It manages multiple Zoho accounts, handles Personal Access Token (PAT) authentication through a pluggable interface, and provides a general-purpose HTTP API invoker where the caller supplies the full base URL at invocation time.

The tool is designed first and foremost as a GitHub Copilot CLI Skill and Claude Agent Skill substrate — a scriptable, deterministic layer that AI agents invoke to execute Zoho API calls without browser sessions, GUIs, or interactive prompts. Human developers interact with it only to configure accounts and inspect trace output.

All output is structured JSON to stdout and all errors are structured JSON to stderr, making every invocation trivially parseable by an agent or downstream pipeline. Security constraints — including a hard block on ZohoCorp corporate accounts and a compile-time host allowlist to prevent SSRF — are unconditional and cannot be disabled at runtime.

## 2. Goals

### 2.1 Business goals

- Provide a single, self-contained binary that AI agents can invoke to perform authenticated Zoho API calls across any Zoho product.
- Eliminate the need for product-specific Zoho CLI tools — one binary covers Cliq, Desk, CRM, People, Creator, and any other Zoho product via the caller-supplied `--base-url` pattern.
- Deliver a working v1 in a single development day.

### 2.2 User goals

- **AI agents**: Execute Zoho REST API calls non-interactively, parse structured JSON responses, manage account context across multi-step workflows, and export complete trace logs for debugging.
- **Human developers**: Configure Zoho accounts and their PAT tokens once, then leave the CLI to agents. Inspect trace sessions when reviewing agent runs.

### 2.3 Non-goals

- OAuth 2.0 authentication flow (deferred to v2).
- WebSocket connections (`ws` group) — deferred to v2.
- Pex / Zoho Cliq real-time protocol (`pex` group) — deferred to v2.
- API Registry shorthand (`api registry` group) — deferred to v2.
- Binary distribution, packaging, or publishing (address in a separate work item).
- Support for ZohoCorp corporate accounts (`*@zohocorp.com` and datacenter variants) — hard-blocked unconditionally.
- GUI or web interface.
- Any Zoho product-specific logic beyond authentication header injection.

## 3. User personas

### 3.1 Key user types

- AI agent (primary consumer)
- Human developer (secondary, light use for account setup and trace inspection)

### 3.2 Basic persona details

- **AI Agent**: A GitHub Copilot CLI Skill or Claude Agent Skill that invokes zapi-cli as a subprocess. It calls the binary with structured flags, reads stdout as JSON, and acts on the result. It never provides interactive input — it always runs with `--no-input`.
- **Developer**: A software engineer configuring Zoho accounts on their machine and occasionally running `api call` or `trace session export` directly in a terminal. Interacts with the tool infrequently.

### 3.3 Role-based access

- **Account owner**: The human who runs `account add` to register a Zoho account and its PAT token. This person is responsible for the credential stored in the OS keychain.
- **Agent operator**: The AI agent that uses a pre-configured account to call APIs. It never adds or removes accounts — it only calls APIs and manages trace sessions.

## 4. Functional requirements

- **Account management — PAT** (Priority: P0)

  - Add a named Zoho account with a PAT token via `account add --name <n> --auth-type pat --token <t>`.
  - Store the PAT exclusively in the OS keychain (macOS Keychain, Windows Credential Manager, Linux Secret Service); fall back to AES-256-GCM encrypted file when the OS keychain is unavailable.
  - Never write token values to `accounts.json`, stdout, or any trace file.
  - List, show (with token masked as `***`), remove, and set a default account.
  - Support a per-command `--account` override flag on all commands that resolve an account.
  - Capture the Zoho account email during `account add` by calling `GET /oauth/user/info` with the supplied token (PAT path) or the received OAuth access token (OAuth path). If no email is returned, `account add` fails with `EMAIL_REQUIRED` — fail closed.

- **ZohoCorp domain hard-block** (Priority: P0 / Security)

  - Reject at `account add` any account whose email resolves to the `zohocorp` second-level domain label (covers `zohocorp.com`, `zohocorp.eu`, `zohocorp.in`, `zohocorp.com.au`, and any future TLDs).
  - Email is sourced by calling `GET /oauth/user/info` with the token in both auth paths: PAT uses the supplied token; OAuth uses the access token received upon PKCE flow completion.
  - `AaaServer.profile.READ` is mandated in the OAuth scope request to guarantee the email claim is returned from the user-info endpoint.
  - **Fail closed**: if user-info returns no email (absent or empty), `account add` fails immediately with `EMAIL_REQUIRED` (exit 1) — the domain check is never skipped regardless of auth type.
  - Also block at `api call` and any `scope` command if the resolved account has a ZohoCorp email.
  - The detection rule and blocked-domain label are compile-time constants — not overridable by flag, environment variable, or config.
  - Return `ACCOUNT_DOMAIN_BLOCKED` (exit 1) on stderr as a JSON error envelope.

- **Host allowlist / SSRF prevention** (Priority: P0 / Security)

  - Before every outgoing HTTP request, verify the target host ends with an allowed Zoho suffix (`.zoho.com`, `.zoho.eu`, `.zoho.in`, `.zoho.com.au`, `.zohoapis.com`, `.zohoapis.in`).
  - Reject with `HOST_NOT_ALLOWED` (exit 1) before any network activity or token injection occurs.
  - The allowlist is a compile-time constant sealed array — not extensible at runtime.

- **Scope management** (Priority: P1)

  - Add, remove, and list OAuth scope strings on a per-account basis via the `scope` group.
  - Setting `needs_reauth = true` on the account after any scope mutation.
  - Block `api call` with `NEEDS_REAUTH` (exit 2) when the resolved account has `needs_reauth = true`, guiding the user to run `account re-auth`.

- **API invocation** (Priority: P0)

  - `api call` requires `--base-url`, `--method`, and `--path`; all other flags are optional.
  - Assemble the final request URL as `<base-url><path>`.
  - Inject `Authorization: Zoho-oauthtoken <token>` from the resolved account's keychain secret.
  - Support inline body (`--body`), file body (`--body-file`, mutually exclusive with `--body`), repeatable `--header key:value`, and repeatable `--query key=value`.
  - Return the HTTP response as a structured JSON object to stdout.
  - Support all HTTP methods: GET, POST, PUT, PATCH, DELETE.

- **Trace sessions** (Priority: P0)

  - `trace session start --name <name>`: create and activate a named trace session.
  - When a session is active, every `api call` automatically appends an `ApiTraceEntry` (JSON) to the session's `.jsonl` file — no per-call flag required.
  - When no session is active, trace entries are silently dropped — no implicit session is created.
  - `trace session list`: list all sessions with name, start time, entry count, and status.
  - `trace session export --name <name>`: emit the full session as a JSON array to stdout; non-destructive (the `.jsonl` file is not modified).
  - Support `--truncate-body <bytes>` and `--type api|pex` filters on export.
  - `trace session close --name <name>`: mark session inactive; preserve `.jsonl` for later export.
  - `trace session remove --name <name>`: delete session and all associated trace files.
  - `Authorization` header must never appear in any `request_headers` field of a trace entry.

- **Utility commands** (Priority: P1)

  - `util time-ms`: output current UTC time as Unix millisecond timestamp (`{"ts": <number>}`).
  - `util uuid`: generate and output a random UUID v4.

- **Output contract** (Priority: P0)

  - All successful output goes to stdout as valid JSON.
  - All errors go to stderr as a JSON envelope: `{ "error": "<message>", "code": "<CODE>", "exitCode": <n> }`.
  - Exit codes: `0` = success, `1` = general error / security block, `2` = auth failure / needs-reauth.

- **Non-interactive mode** (Priority: P0)

  - Global `--no-input` flag: when set, the CLI never prompts; any operation that would require interactive input fails with a structured error.
  - Default for agent use; developers may omit it.

- **Keychain storage** (Priority: P0)

  - Platform implementations via direct P/Invoke (no NuGet keychain package): `MacOsKeychainProvider` (Security.framework), `WindowsKeychainProvider` (advapi32.dll CredWrite/CredRead), `LinuxKeychainProvider` (libsecret).
  - `EncryptedFileKeychainProvider` (AES-256-GCM) as automatic fallback when the OS keychain is unavailable (e.g., headless Linux CI).
  - Key format: `zapi-cli:<accountName>:<tokenType>`.

## 5. User experience

### 5.1 Entry points & first-time user flow

- A developer installs the binary (distribution deferred) and runs `zapi-cli account add --name work --auth-type pat --token <PAT>` once.
- They set it as default with `zapi-cli account set-default --name work`.
- From that point forward, agents invoke `zapi-cli api call` without any further setup.

### 5.2 Core experience

- **Account setup**: Developer runs one `account add` command. The CLI fetches the Zoho user-info email, validates it is non-ZohoCorp, stores the PAT in the OS keychain, and writes account metadata to `accounts.json`. Output confirms success as JSON.
- **Agent API call**: Agent invokes `zapi-cli api call --base-url <url> --method GET --path /resource`. The CLI resolves the active account, validates the host against the allowlist, fetches the PAT from the keychain, injects the `Authorization` header, makes the request, and returns the response JSON to stdout. The agent parses stdout.
- **Trace-driven agent session**: Agent starts `trace session start --name session-001`, fires multiple `api call` commands, then exports with `trace session export --name session-001` to get a full call log. No per-call flags needed — tracing is automatic when a session is active.
- **Error handling**: Every failure produces a JSON error on stderr and a non-zero exit code. Agents can detect failures by checking the exit code or parsing stderr.

### 5.3 Advanced features & edge cases

- Agent supplying a non-Zoho `--base-url` is rejected pre-request with `HOST_NOT_ALLOWED` (no token is ever sent to a non-Zoho host).
- `account add` with a `@zohocorp.com` email returns `ACCOUNT_DOMAIN_BLOCKED` immediately after the user-info fetch, before any keychain write.
- Legacy `accounts.json` containing a ZohoCorp email emits a warning to stderr on startup; any command resolving to that account is rejected.
- `--body` and `--body-file` being supplied together is a hard validation error before any HTTP activity.
- Calling `api call` when `needs_reauth = true` is a hard block (exit 2) with a guided error message pointing to `account re-auth`.
- `trace session export` is always non-destructive; it can be called multiple times safely.
- OS keychain unavailable (headless CI) automatic fallback to `EncryptedFileKeychainProvider` — no user intervention required.

### 5.4 UI/UX highlights

- All output is machine-readable JSON — no ANSI color codes, no tables, no human-readable progress spinners on stdout.
- `--no-input` is the recommended flag for all agent invocations to guarantee zero interactive prompts.
- Masked token display (`***`) in `account show` and `account list` prevents accidental token exposure in logs.
- Session names are human-readable (developer-chosen), not UUIDs — making trace output easy to reference in downstream analysis.

## 6. Narrative

A GitHub Copilot agent is tasked with mapping the Zoho Desk ticket lifecycle. It starts a named trace session, fires a sequence of `api call` commands across multiple Desk endpoints using the pre-configured `work` account, and receives structured JSON responses it can reason about. After the run, it exports the full trace to provide the developer with a complete, ordered record of every HTTP exchange — including requests, responses, timing, and the target base URL — without a single `Authorization` token value ever touching a log file. The developer reviews the trace, iterates the agent prompt, and reruns. The entire loop is deterministic: identical inputs always produce identical behavior, and every security boundary is enforced by the binary itself.

## 7. Success metrics

### 7.1 User-centric metrics

- An agent can complete a multi-step Zoho API workflow (account resolve → api call → trace export) without any interactive prompt or error.
- `trace session export` produces a valid, parseable JSON array after a multi-call session.
- `account add` with a ZohoCorp email is rejected 100% of the time before any keychain write.

### 7.2 Business metrics

- Binary builds and runs successfully on all four RIDs: `win-x64`, `osx-x64`, `osx-arm64`, `linux-x64`.
- All v1 commands are implemented and reachable via the CLI by end of day 1.

### 7.3 Technical metrics

- All stdout is valid, parseable JSON for every command path (success and error).
- No PAT or OAuth token value appears in `accounts.json`, stdout, stderr, or any trace `.jsonl` file.
- Host allowlist check fires pre-request, before any TCP connection is opened or any token is read from the keychain.
- Unit tests pass for `AccountStore`, `PatAuthProvider`, and `ApiClient` (including allowlist and domain-block paths).

## 8. Technical considerations

### 8.1 Integration points

- **Zoho user-info API**: Called at `account add` to fetch the account email for ZohoCorp domain validation.
- **OS keychains**: macOS Security.framework, Windows Credential Manager (advapi32.dll), Linux libsecret — all via P/Invoke in `ZapiCli.Keychain`.
- **Zoho product REST APIs**: Any endpoint under the allowed host suffixes. No product-specific API integration — the tool is deliberately product-agnostic.
- **GitHub Copilot CLI Skills / Claude Agent Skills**: Invocation contract is: structured flags in, JSON stdout out, JSON stderr on error, exit code signals success/failure.

### 8.2 Data storage & privacy

- `accounts.json` lives at `<configDir>/zapi-cli/accounts.json` with `0600` (Unix) / ACL-restricted (Windows) permissions.
- PAT and OAuth tokens are never stored in `accounts.json` or any plain-text file — only in the OS keychain or the AES-256-GCM encrypted fallback file.
- Trace `.jsonl` files never contain `Authorization` header values.
- `account show` and `account list` always mask token values as `***`.
- Platform config directories: `~/Library/Application Support/zapi-cli/` (macOS), `%LOCALAPPDATA%\zapi-cli\` (Windows), `~/.config/zapi-cli/` (Linux).

### 8.3 Scalability & performance

- .NET 10 self-contained single-file binary; no runtime dependency on the target machine.
- Binary artifact size: ~50–80 MB (self-contained .NET); acceptable for the agent-invocation use case.
- Cold-start latency on first invocation is the primary performance concern for agent workflows with many sequential calls. ReadyToRun publish flag is a v1+ mitigation option.
- Trace `.jsonl` files are append-only; export reads the full file on demand — suitable for the expected session sizes in agent-driven exploration tasks.

### 8.4 Potential challenges

- **macOS code-signing**: Unsigned developer builds may trigger OS keychain access prompts, which disrupts agent-mode use. Addressed by developer signing the binary or accepting the one-time prompt.
- **Linux headless environments**: `libsecret` requires a running GNOME Keyring or KWallet daemon. The encrypted file fallback mitigates this transparently, but the security properties differ from a full OS keychain.
- **Zoho user-info API availability**: `account add` depends on a network round-trip to fetch the email for ZohoCorp domain blocking. If the API is unavailable, `account add` must fail safely rather than skip the check.
- **P/Invoke platform testing**: All three keychain implementations require CI coverage on macOS, Windows, and Linux runners.

## 9. Milestones & sequencing

### 9.1 Project estimate

- Small: 1 day (solo engineer)

### 9.2 Team size & composition

- 1 engineer: full-stack implementation (C# / .NET 10, CLI framework, keychain P/Invoke, domain/security logic, tests)

### 9.3 Suggested phases

- **Phase 1 — Core domain layer** (morning, ~3 hours)

  - Scaffold solution: `ZapiCli`, `ZapiCli.Core`, `ZapiCli.Keychain` projects with `dotnet new` and project references.
  - Implement `IKeychainProvider` + all four platform implementations (`MacOsKeychainProvider`, `WindowsKeychainProvider`, `LinuxKeychainProvider`, `EncryptedFileKeychainProvider`).
  - Implement `IAuthProvider` + `PatAuthProvider`.
  - Implement `AccountStore` (read/write `accounts.json`, per-account CRUD, `needs_reauth` lifecycle).
  - Implement ZohoCorp domain-block utility (compile-time constant, no runtime override).

- **Phase 2 — API client + security** (late morning, ~2 hours)

  - Implement `ApiClient`: host allowlist check (pre-request), auth header injection, `HttpClient` wrapper, response serialization.
  - Wire `TraceWriter` into `ApiClient`: append `ApiTraceEntry` to active session `.jsonl` when a session is open; silently drop when none is active.
  - Implement `TraceSession`, `TraceExporter` (non-destructive export to JSON array).

- **Phase 3 — Command layer** (afternoon, ~3 hours)

  - Wire Spectre.Console.Cli in `Program.cs`.
  - Implement all `account` subcommands (add, list, remove, show, set-default).
  - Implement all `scope` subcommands (add, remove, list).
  - Implement `api call`.
  - Implement all `trace session` subcommands (start, list, export, close, remove).
  - Implement `util time-ms` and `util uuid`.
  - Enforce `--no-input` global flag; enforce JSON-only stdout and JSON-envelope stderr across all commands.

- **Phase 4 — Tests + smoke** (evening, ~2 hours)

  - Unit tests: `AccountStoreTests`, `PatAuthProviderTests`, `ApiClientTests` (allowlist, domain-block, token-masking, trace-append paths).
  - Smoke test: full agent workflow on local machine (account add → api call against a live Zoho API → trace export).
  - Verify self-contained publish via `dotnet publish -r osx-arm64 /p:PublishSingleFile=true --self-contained true`.

## 10. User stories

### 10.1. Add a PAT account

- **ID**: ZAPI-001
- **Description**: As an AI agent or developer, I want to add a named Zoho account using a Personal Access Token so that subsequent API calls are authenticated without any interactive login flow.
- **Acceptance criteria**:
  - `zapi-cli account add --name work --auth-type pat --token <PAT>` succeeds and outputs `{"status":"ok","data":{"name":"work"}}` to stdout.
  - The PAT is stored in the OS keychain (or encrypted file fallback) under the key `zapi-cli:work:pat`.
  - The PAT value is never written to `accounts.json`.
  - The Zoho user-info API (`GET /oauth/user/info`) is called to fetch the account email; if the email matches the ZohoCorp SLD, the command fails with `ACCOUNT_DOMAIN_BLOCKED` (exit 1) before any keychain write.
  - If the user-info API returns no email (for any auth type), the command fails with `EMAIL_REQUIRED` (exit 1) before any keychain write.
  - For the OAuth path, `AaaServer.profile.READ` is mandated in the scope request to guarantee the email is returned; the received OAuth access token is used to call the user-info endpoint.
  - Supplying `--token` together with `--auth-type oauth` returns a validation error (exit 1).
  - `--domain` defaults to `zoho.com` if not provided.

### 10.2. List accounts

- **ID**: ZAPI-002
- **Description**: As a developer, I want to list all configured accounts so that I can confirm what accounts are available and which is set as default.
- **Acceptance criteria**:
  - `zapi-cli account list` outputs a JSON array of account entries to stdout.
  - Token values are masked as `"***"` in the output.
  - The `is_default` field correctly reflects the active account.
  - Exits 0 on success; exits 1 if `accounts.json` is unreadable.

### 10.3. Remove an account

- **ID**: ZAPI-003
- **Description**: As a developer, I want to remove a named account and its stored credentials so that no trace of that account remains on the machine.
- **Acceptance criteria**:
  - `zapi-cli account remove --name work` deletes the account entry from `accounts.json` and calls `DeleteAsync` on the keychain provider for the account's token key.
  - Output confirms removal as JSON to stdout.
  - If the account is not found, exits 1 with a JSON error indicating the account does not exist.

### 10.4. Show account details

- **ID**: ZAPI-004
- **Description**: As a developer, I want to inspect a single account's metadata so that I can verify its configuration without exposing the token.
- **Acceptance criteria**:
  - `zapi-cli account show --name work` outputs the full `AccountEntry` JSON with `"token": "***"`.
  - No token value appears anywhere in the output.
  - Exits 1 with a JSON error if the account is not found.

### 10.5. Set the default account

- **ID**: ZAPI-005
- **Description**: As a developer, I want to designate one account as the default so that I don't have to pass `--account` on every command.
- **Acceptance criteria**:
  - `zapi-cli account set-default --name work` sets `is_default: true` on the named account and `is_default: false` on all others in `accounts.json`.
  - Output confirms the change as JSON.
  - Exits 1 if the named account does not exist.

### 10.6. Per-command account override

- **ID**: ZAPI-006
- **Description**: As an AI agent, I want to override the active account for a single command so that I can operate on multiple accounts in one workflow without changing the default.
- **Acceptance criteria**:
  - Any command that resolves an account accepts `--account <name>` to override the default for that invocation only.
  - `accounts.json` is not mutated by the override.
  - If the named account does not exist, the command exits 1 with a JSON error.

### 10.7. Add a scope to an account

- **ID**: ZAPI-007
- **Description**: As an AI agent or developer, I want to record OAuth scope strings on an account so that the scope list is available for future OAuth re-auth flows.
- **Acceptance criteria**:
  - `zapi-cli scope add --scope ZohoDesk.Tickets.READ [--account work]` appends the scope to the account's `scopes` array in `accounts.json` and sets `needs_reauth: true`.
  - Duplicate scope entries are not added.
  - Output confirms the updated scope list as JSON.

### 10.8. Remove a scope from an account

- **ID**: ZAPI-008
- **Description**: As an AI agent or developer, I want to remove a scope from an account so that the scope list stays accurate.
- **Acceptance criteria**:
  - `zapi-cli scope remove --scope ZohoDesk.Tickets.READ [--account work]` removes the scope and sets `needs_reauth: true`.
  - If the scope is not present, exits 1 with a JSON error.

### 10.9. List scopes for an account

- **ID**: ZAPI-009
- **Description**: As an AI agent, I want to list the scopes associated with an account so that I know what permissions are configured.
- **Acceptance criteria**:
  - `zapi-cli scope list [--account work]` outputs the account's `scopes` array as JSON.
  - Exits 0 even if the scopes array is empty.

### 10.10. Block API call when re-auth is required

- **ID**: ZAPI-010
- **Description**: As the system, I want to block `api call` when the target account has pending scope changes so that the agent is notified to re-authenticate before making calls.
- **Acceptance criteria**:
  - `api call` targeting an account with `needs_reauth: true` exits 2 with `NEEDS_REAUTH` and a message directing the user to run `account re-auth --name <account>`.
  - No HTTP request is made; no token is read from the keychain.

### 10.11. Make a GET API call

- **ID**: ZAPI-011
- **Description**: As an AI agent, I want to make an authenticated GET request to a Zoho product API so that I can retrieve data for analysis.
- **Acceptance criteria**:
  - `zapi-cli api call --base-url https://cliq.zoho.com/api/v2 --method GET --path /channels` constructs the URL as `https://cliq.zoho.com/api/v2/channels`, injects `Authorization: Zoho-oauthtoken <PAT>`, and returns the HTTP response as JSON to stdout.
  - Exits 0 on HTTP 2xx; exits 1 on HTTP 4xx/5xx, with the error response included in the stderr JSON envelope.
  - If an active trace session exists, an `ApiTraceEntry` is appended to the session `.jsonl` (without the `Authorization` value).

### 10.12. Make a POST API call with a JSON body

- **ID**: ZAPI-012
- **Description**: As an AI agent, I want to POST a JSON payload to a Zoho product API to create or update resources.
- **Acceptance criteria**:
  - `zapi-cli api call --base-url https://crm.zoho.com/crm/v5 --method POST --path /Leads --body '{"data":[{"Last_Name":"Doe"}]}'` sends the body with `Content-Type: application/json` and returns the response JSON.
  - `--body` and `--body-file` supplied together exits 1 with a validation error before any network activity.
  - `--body-file <path>` reads the file and sends its contents as the request body.

### 10.13. Supply custom headers and query parameters

- **ID**: ZAPI-013
- **Description**: As an AI agent, I want to add custom request headers and query parameters to an API call so that I can pass required metadata or pagination controls.
- **Acceptance criteria**:
  - `--header X-Custom-Header:value` is repeatable and appends the header to the outgoing request.
  - `--query page=2` is repeatable and is appended to the request URL as a query string.
  - Custom headers do not overwrite the `Authorization` header.

### 10.14. Enforce host allowlist (SSRF prevention)

- **ID**: ZAPI-014
- **Description**: As the system, I want to reject any `api call` whose target host is not in the Zoho allowlist so that the tool cannot be used to send Zoho credentials to arbitrary hosts.
- **Acceptance criteria**:
  - Any `--base-url` whose host does not end with `.zoho.com`, `.zoho.eu`, `.zoho.in`, `.zoho.com.au`, `.zohoapis.com`, or `.zohoapis.in` is rejected with `HOST_NOT_ALLOWED` (exit 1) before any TCP connection is opened or any token is read from the keychain.
  - The allowlist is hardcoded and cannot be extended without a source code change.
  - The check fires even if `--no-input` is not set.

### 10.15. Block ZohoCorp account operations

- **ID**: ZAPI-015
- **Description**: As the system, I want to prevent any account whose email resolves to the ZohoCorp corporate domain from being used so that Zoho corporate infrastructure is never exposed to agent-driven API calls.
- **Acceptance criteria**:
  - `account add` fetching a ZohoCorp email from the Zoho user-info API exits 1 with `ACCOUNT_DOMAIN_BLOCKED` before writing to `accounts.json` or the keychain.
  - `api call` with an active or named ZohoCorp account exits 1 with `ACCOUNT_DOMAIN_BLOCKED` before any HTTP request.
  - `scope add/remove/list` targeting a ZohoCorp account exits 1 with `ACCOUNT_DOMAIN_BLOCKED`.
  - There is no flag, environment variable, or config value that bypasses the check.
  - A startup warning is emitted to stderr if `accounts.json` contains a ZohoCorp-domain account.

### 10.16. Start a named trace session

- **ID**: ZAPI-016
- **Description**: As an AI agent, I want to start a named trace session before a workflow run so that all API calls in that run are automatically captured.
- **Acceptance criteria**:
  - `zapi-cli trace session start --name desk-exploration-001` creates the session entry in `sessions.json` and the `traces/desk-exploration-001/` directory with an empty `trace.jsonl`.
  - Subsequent `api call` commands automatically append entries to this session's `.jsonl` without any extra flag.
  - Starting a session when one is already active replaces the active session pointer (the old session is not destroyed).
  - Output confirms session creation as JSON.

### 10.17. Export a trace session

- **ID**: ZAPI-017
- **Description**: As an AI agent or developer, I want to export a trace session as a JSON array so that I can inspect or pass the full call log to analysis tools.
- **Acceptance criteria**:
  - `zapi-cli trace session export --name desk-exploration-001` emits a JSON array of `ApiTraceEntry` objects to stdout.
  - The export is non-destructive; the `.jsonl` file is not modified.
  - No `Authorization` header value appears in any entry's `request_headers`.
  - `--truncate-body <bytes>` trims `response_body` and `request_body` to the specified byte count.
  - `--type api` filters entries to API calls only (for when Pex entries are added in v2).
  - Exits 1 if the named session does not exist.

### 10.18. List trace sessions

- **ID**: ZAPI-018
- **Description**: As a developer, I want to list all trace sessions with their metadata so that I can identify which sessions contain relevant call data.
- **Acceptance criteria**:
  - `zapi-cli trace session list` outputs a JSON array with each session's name, start time, entry count, and status (`active` or `closed`).
  - Exits 0 even if no sessions exist (returns an empty array).

### 10.19. Close a trace session

- **ID**: ZAPI-019
- **Description**: As an AI agent, I want to close a trace session after a workflow run so that future API calls do not add entries to it.
- **Acceptance criteria**:
  - `zapi-cli trace session close --name desk-exploration-001` marks the session status as `closed` in `sessions.json`.
  - The `.jsonl` file is preserved for future export.
  - `api call` commands after closing no longer append entries to the closed session (they are silently dropped if no new session is started).

### 10.20. Remove a trace session

- **ID**: ZAPI-020
- **Description**: As a developer, I want to delete a trace session and all its associated data to free up disk space.
- **Acceptance criteria**:
  - `zapi-cli trace session remove --name desk-exploration-001` deletes the session entry from `sessions.json` and removes the `traces/desk-exploration-001/` directory and its contents.
  - Exits 1 with a JSON error if the named session does not exist.

### 10.21. Non-interactive mode

- **ID**: ZAPI-021
- **Description**: As an AI agent, I want to guarantee the CLI never prompts for interactive input so that automated invocations never block waiting for user input.
- **Acceptance criteria**:
  - `--no-input` is a global flag accepted by all commands.
  - When `--no-input` is set, any code path that would prompt for user input instead exits 1 with a structured JSON error.
  - The flag has no effect on commands that never prompt (i.e., it is safe to always pass it).

### 10.22. Structured JSON output contract

- **ID**: ZAPI-022
- **Description**: As an AI agent, I want every CLI invocation to produce machine-parseable JSON so that I can reliably process results without text parsing.
- **Acceptance criteria**:
  - All successful output is written to stdout as valid JSON (object or array).
  - All errors are written to stderr as `{ "error": "<message>", "code": "<CODE>", "exitCode": <n> }`.
  - Exit code 0 means success; exit code 1 means general error or security block; exit code 2 means auth failure or needs-reauth.
  - No ANSI escape codes, progress spinners, or human-formatted text appears on stdout.

### 10.23. Get current UTC time as millisecond timestamp

- **ID**: ZAPI-023
- **Description**: As an AI agent, I want to retrieve the current UTC time as a Unix millisecond timestamp so that I can generate consistent timestamps for API payloads without depending on platform shell utilities.
- **Acceptance criteria**:
  - `zapi-cli util time-ms` outputs `{"ts": <unix_ms>}` to stdout.
  - The value is a valid Unix timestamp in milliseconds for the current UTC time.
  - Exits 0.

### 10.24. Generate a UUID v4

- **ID**: ZAPI-024
- **Description**: As an AI agent, I want to generate a random UUID v4 so that I can create unique identifiers for API payloads or session names.
- **Acceptance criteria**:
  - `zapi-cli util uuid` outputs `{"uuid": "<uuid-v4-string>"}` to stdout.
  - The value is a valid RFC 4122 UUID v4.
  - Exits 0.
