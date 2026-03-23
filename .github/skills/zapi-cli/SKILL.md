---
name: zapi-cli
description: 'Enables AI agents to interact with any Zoho REST API using the zapi-cli binary. Covers account management, authenticated API calls, API registry, trace sessions, and structured output parsing. Designed for use cases such as API analysis, multi-request exploration, and automated documentation generation.'
---

# zapi-cli — HTTP API Analysis Skill

## Purpose

This skill enables an agent to **execute, validate, document, and understand** a set of HTTP API endpoints using the `zapi-cli` binary. Beyond simple validation, the agent must explore how each API behaves under different inputs and conditions to build a clear picture of the feature's mechanics. It produces two output artefacts:

| Artefact | Path | Consumer |
|----------|------|----------|
| **API Catalog** | `docs/api-analysis/api-catalog.md` | Network-layer implementation agent |
| **Failure Report** | `docs/api-analysis/failures.md` | Developer review — lists every endpoint that did not behave as expected |

---

## Tool Location

The binaries are bundled in the `tools/` subfolder alongside this SKILL.md. Select the binary for the current platform before invoking any command.

| Platform | Binary path (relative to SKILL.md) |
|---|---|
| macOS — Apple Silicon (arm64) | `tools/osx-arm64/zapi-cli` |
| macOS — Intel (x64) | `tools/osx-x64/zapi-cli` |
| Linux x64 | `tools/linux-x64/zapi-cli` |
| Linux arm64 | `tools/linux-arm64/zapi-cli` |
| Windows x64 | `tools/win-x64/zapi-cli.exe` |

```bash
# Detect platform and resolve the CLI binary path
ARCH=$(uname -m)
OS=$(uname -s)
SKILL_DIR="<absolute path to this skill folder>"

if [[ "$OS" == "Darwin" && "$ARCH" == "arm64" ]]; then
  CLI="$SKILL_DIR/tools/osx-arm64/zapi-cli"
elif [[ "$OS" == "Darwin" ]]; then
  CLI="$SKILL_DIR/tools/osx-x64/zapi-cli"
elif [[ "$OS" == "Linux" && "$ARCH" == "aarch64" ]]; then
  CLI="$SKILL_DIR/tools/linux-arm64/zapi-cli"
else
  CLI="$SKILL_DIR/tools/linux-x64/zapi-cli"
fi

chmod +x "$CLI"
```

> **Agent note:** Always `chmod +x` the binary after selecting it — the executable bit may be lost depending on how files were transferred.

---

## CLI — Available Operations

All requests are made via the `zapi-cli` binary. The following commands are available:

| Command | When to use |
|---------|-------------|
| `account list` | **Always call first** to verify which accounts are present and healthy for the session. |
| `account add` | Register a new Zoho account using an OAuth Self-Client grant code (non-interactive setup). |
| `account login` | Authenticate a new account via browser-based OAuth login (interactive setup). |
| `account show --name <NAME>` | Check a specific account's `needs_reauth` status before making calls. |
| `account set-default --name <NAME>` | Set the account to be used when `--account` is not specified on individual commands. |
| `account remove --name <NAME>` | Remove an account and revoke its OAuth token from the Zoho servers. |
| `account re-auth --name <NAME>` | Re-authenticate an account whose refresh token has expired. Call this when exit code is `2` or `needs_reauth` is `true`. |
| `api call --url <URL> -X <METHOD>` | Fire an HTTP request against a Zoho endpoint. The OAuth token is injected automatically. |
| `api registry add / list / show / update / remove` | Manage the local registry of named API endpoints for reuse across sessions. |
| `trace session start` | Begin recording all `api call` requests/responses to a structured JSON file. |
| `trace session list` | List all known trace sessions with their current status and entry counts. |
| `trace session export` | Read back the recorded trace entries, with optional type filtering and body truncation. |
| `trace session close` | Seal a trace session after draining in-flight writes. |
| `trace session reopen` | Re-activate a closed trace session to append new entries. |
| `trace session remove` | Remove a session from the sessions index (trace file is preserved). |
| `trace config set --default-export-path <PATH>` | Persist the default export path for trace sessions. |
| `trace config show` | Display the current trace configuration. |
| `scope add --scope <SCOPE>` | Add an OAuth scope to an account and flag it for silent re-auth on the next `api call`. |
| `scope list` | List all OAuth scopes configured for an account. |
| `util time-ms` | Get current Unix milliseconds — useful for time-range query parameters. |
| `util uuid` | Generate a UUID v4 — useful for idempotency keys. |
| `util time-now` | Get current India Standard Time (IST) as a formatted timestamp. |

---

## Output Contract

Every command outputs **strict JSON** to stdout. Errors go to **stderr**. The agent must never parse free text, only JSON.

### Success (stdout)
```json
{ "status": "ok", "data": { ... } }
```

### Error (stderr)
```json
{ "error": "<message>", "code": "<ERROR_CODE>", "exitCode": <n> }
```

### Exit codes

| Exit code | Meaning |
|---|---|
| `0` | Success |
| `1` | General error |
| `2` | Authentication expired — call `account re-auth` before retrying |

> **Agent note:** Always check the exit code first. Exit code `2` means the account needs re-authentication — invoke `$CLI account re-auth --name <ACCOUNT>` before retrying the failed command.

---

## Global Flags

These flags are accepted by all subcommands:

| Flag | Type | Description |
|---|---|---|
| `-a, --account <ACCOUNT>` | string | Override the default account for this invocation only. |
| `--json` | boolean | Force JSON output mode (useful if a subcommand has non-JSON output). |
| `--no-input` | boolean | Disable all interactive prompts; fail instead of prompting. |
| `-h, --help` | boolean | Print help for the current command. |

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

## Accounts

Verify accounts before starting any session:

```bash
$CLI account list
```

Check health of the specific account to use:

```bash
STATUS=$($CLI account show --name <ACCOUNT_NAME>)
NEEDS_REAUTH=$(echo "$STATUS" | jq -r '.data.needs_reauth')
if [ "$NEEDS_REAUTH" = "true" ]; then
  $CLI account re-auth --name <ACCOUNT_NAME>
fi
```

**Account selection rules:**
- Default to using the primary account unless the API being analysed requires multi-user interaction.
- For APIs that require two participants (e.g. messaging, channel membership), use a primary account as the initiator and a secondary account as the receiver.
- When an API requires an admin or owner role, try the primary account first; if it fails with a permissions error, escalate to another account and record the account used in the catalog.

---

## Test Data Isolation Policy

**The agent must never read from, write to, or mutate any pre-existing data on the server during an analysis session.**

Every piece of data the agent interacts with must be created fresh at the start of the session specifically for that session's tests. This applies universally:

| Data type | Rule |
|-----------|------|
| Chats / group chats | Create a new chat at session start. Never send messages or perform actions in any existing chat. |
| Channels | Create a new channel for the session. Never post to or modify any existing channel. |
| Messages | Post only to the freshly created chat or channel. |
| Users / memberships | Add or remove only within freshly created entities. |
| Any other mutable entity (threads, reactions, bookmarks, etc.) | Create a new parent entity first; perform all mutations within it. |

---

## Agent Behaviour

### Step 1 — Initialise

1. Detect the platform and set `$CLI` to the correct binary path. Run `chmod +x "$CLI"`.
2. Call `$CLI account list` to confirm which accounts are available and healthy for this session.
3. For each account to be used, check `needs_reauth` and call `account re-auth` if needed.
4. Ensure the output directories exist: `docs/api-analysis/`. Create them if they do not exist. If the output files already exist, append to them rather than overwriting.
5. Start a trace session so all API calls during analysis are automatically recorded:
   ```bash
   SESSION=$($CLI trace session start --name "api-analysis-$(date +%s)" --export-path /tmp/traces/)
   SESSION_ID=$(echo "$SESSION" | jq -r '.data.unique_id')
   ```
6. **Identify prerequisite APIs using the resources catalog first.** Before making any prerequisite call (e.g. creating a chat before testing a message API), consult `resources/catalog.md` to find the relevant spec file by description, then read that YAML file for the exact URL, HTTP method, and required parameters to use. If the catalog does not contain a suitable entry, that API has not been catalogued yet.
7. **Create all test entities needed for this session before executing any API under analysis.** At minimum, create a dedicated group chat (and channel, if channel APIs are being tested). Use `$CLI api call` to make the prerequisite creation calls, using the YAML spec from `resources/` to get the exact request shape.

### Step 2 — Understand the APIs to Analyse

Accept the list of APIs from the user. Each API entry must have at minimum:
- HTTP method (GET, POST, PUT, DELETE, PATCH)
- URL template (may contain `{placeholders}`)
- A short description of the expected behaviour
- Any required request body or query parameters

If the user provides an incomplete definition, infer reasonable defaults (e.g. `Content-Type: application/json`) and flag any assumptions in the catalog entry for that API.

### Step 3 — Execute Each API with Variation Exploration

For each API in the list, the goal is not only to confirm the API works but to **understand the full behaviour of the feature it serves**.

#### 3a — Resolve and Execute

1. **Resolve the URL** — substitute all `{placeholders}` with real values. All entity IDs must refer to the freshly created test entities from Step 1 — never reference pre-existing server data. If the URL is given as a path only (e.g. `/api/v2/chats`), prepend the appropriate Zoho base domain (e.g. `https://cliq.zoho.com`) unless the user has specified otherwise. If the request body shape is unclear, look up the matching YAML file in `resources/` via `resources/catalog.md`.

2. **Fire the request** using `api call`:
   ```bash
   OUTPUT=$($CLI --no-input api call \
     --url "<fully resolved URL>" \
     -X <METHOD> \
     --account <ACCOUNT_NAME> \
     --body '{"key": "value"}' \    # omit for GET
     --header "Content-Type:application/json" \    # add only when sending a body
     2>/tmp/zapi_err.json)
   EXIT_CODE=$?
   ```

3. **Evaluate the response** against the success criteria below.

4. **Retry on transient failure** — if the exit code is `1` with error code `API_ERROR` and the HTTP status was `5xx`, or if exit code is `2`, handle accordingly:
   ```bash
   if [ $EXIT_CODE -eq 2 ]; then
     $CLI account re-auth --name "$ACCOUNT"
     # retry the original command once
   fi
   ```

#### 3b — Variation Exploration (Feature Understanding)

After the baseline call succeeds, identify all axes of variation for the API and exercise each one. The aim is to build a complete behavioural model of the feature, not just verify a happy path.

**Axes to explore:**

| Axis | What to test |
|------|--------------|
| **Query / body parameters that accept enumerated values** | Call the API once per distinct value and observe how the response shape or content changes. |
| **Optional parameters** | Call with the parameter omitted, then with it included. Document the difference. |
| **Pagination parameters** (`limit`, `nextToken`, `from`/`to`) | Test with a small limit to confirm pagination works; retrieve the next page using the token or cursor from the first response. |
| **Filtering / search parameters** | Test with a matching value, a non-matching value, and (if applicable) an empty string. |
| **Permission-scoped behaviour** | If the API's response or access is expected to differ by user role, call it from different accounts and compare. |
| **Empty / edge states** | Call the API against an entity that has no data yet (e.g. a brand-new chat with no messages) and observe the empty-state response. |

**Rules for variation exploration:**
- Every variation call must also use the freshly created test entities — do not reuse production data.
- If a variation call produces a *different* response shape, record it as a separate entry under the same API section in the catalog.
- If a variation fails unexpectedly (e.g. a valid enum value returns 400), record it in the failure report in addition to the catalog.
- Stop exploring variations once the behaviour is fully understood — do not exhaustively brute-force every permutation.

### Step 4 — Classify the Result

#### Success Criteria

An API call is **successful** when ALL of the following hold:

- CLI exits with code `0`.
- The `data` field in stdout is valid JSON.
- There are no `error`, `status: "failure"`, or equivalent error-indicator keys in the top-level response object inside `data`.

#### Failure Criteria

Record an entry in the **Failure Report** when ANY of the following occur:

- CLI exits with code `1` or `2`.
- Exit code is `0` but `data` contains a top-level `status: "failure"` or `error` key.
- The response body is empty or non-JSON when JSON was expected.
- A prerequisite call needed to resolve a placeholder also fails.
- The response is missing fields that were defined as required in the API spec.

---

### Step 5 — Write Output Files

#### 5a — API Catalog (`docs/api-analysis/api-catalog.md`)

Append a section for each **successful** API using this template:

~~~markdown
---

## <Short API Name>

**Method:** `<HTTP METHOD>`  
**URL Template:** `<URL with {placeholders}>`  
**Account Used:** `<account name>`  

### Request

**Headers:**
```json
{
  "Content-Type": "application/json"
}
```

**Body:** *(omit section if GET or no body)*
```json
<prettified request body>
```

### Response

**Status:** `<HTTP status code>`

**Body:**
```json
<prettified response body — truncate arrays to 3 representative items if very large>
```

### Notes

- <Any observed behaviour worth noting for the implementation agent>
- <Assumption made, if any>
- <Fields in the response that map to domain models, if identifiable>

### Feature Behaviour Summary

> Synthesised understanding built from the baseline call and variation calls above.

- **What the API does:** <One or two sentences describing the feature this API implements.>
- **Key parameters and their effect:** <Enumerate each significant parameter and describe how changing it affects the response.>
- **Pagination / continuation:** <Describe the pagination mechanism if present, including the field name of the cursor/token.>
- **Empty / edge-state behaviour:** <What does the API return when there is no data?>
- **Permission differences:** <Does the response differ by user role or membership? If so, how?>
- **Unexpected or noteworthy findings:** <Anything discovered during variation exploration that was not obvious from the API definition.>
~~~

#### 5b — Failure Report (`docs/api-analysis/failures.md`)

Append a section for each **failed** API using this template:

~~~markdown
---

## <Short API Name>

**Method:** `<HTTP METHOD>`  
**URL Attempted:** `<fully resolved URL>`  
**Account Used:** `<account name>`  

### Request Sent

**Body:** *(omit if none)*
```json
<request body>
```

### Failure Detail

**CLI Exit Code:** `<exit code>`  
**Error Code:** `<ERROR_CODE from stderr>`  
**Error Message:** `<error field from stderr>`  
**HTTP Status:** `<code from API_ERROR data, or "N/A">`  
**Response Body:**
```json
<response body or error message>
```

**Failure Reason:** <One-sentence explanation of why this is classified as a failure>

**Suggested Fix / Investigation:** <What should be checked — wrong endpoint path, missing permission, bad payload shape, etc.>
~~~

---

### Step 6 — Close Trace and Print Summary

```bash
# Close the trace session
$CLI trace session close --id "$SESSION_ID"

# Export and summarise the trace
$CLI trace session export --id "$SESSION_ID" --type api \
  | jq '{
      total: length,
      successful: [.[] | select(.error == null)] | length,
      failed: [.[] | select(.error != null)] | length,
      calls: [.[] | {seq, method, url, status: .response_status, duration_ms, error}]
    }'
```

After closing the trace, print a summary to the chat:

```
API Analysis Complete
=====================
Total APIs analysed : <n>
Successful          : <n>  → saved to docs/api-analysis/api-catalog.md
Failed              : <n>  → recorded in docs/api-analysis/failures.md

Failed endpoints:
  - <METHOD> <url template>   (<failure reason, one line>)
  ...
```

If all APIs passed, print a success message and skip the failure list.

---

### Step 7 — Update the Resources Library

After completing the analysis, reconcile every successfully analysed API against the `resources/` library.

#### New specs — create directly (no permission required)

If an API has no matching YAML in `resources/`, create the file and add the catalog row immediately without asking the user. Use the naming convention `<verb>-<resource>[-<qualifier>].yaml`.

#### Updates, syncs, and removals — ask permission first

If an existing YAML file needs changes, or `catalog.md` rows need updating, or any file would be removed, **present a summary and wait for explicit confirmation before making any changes**:

```
The following changes to existing resources/ files are recommended:

Updates:
  - <filename>.yaml  (<what changed and why>)
  - ...

catalog.md rows to update:
  - <row description>
  - ...

Shall I apply these changes? (yes / no / select specific items)
```

Only proceed once the user explicitly confirms. If they say "yes", apply all. If they select specific items, apply only those. If they say "no", skip all updates.

> **Exception:** If the user's original request explicitly included "update resources", "sync resources", "update specs", or equivalent wording, skip the confirmation prompt and apply all changes directly.

#### For each new or approved change:

1. **Check if a YAML spec already exists** — search `resources/catalog.md` for the API by description or URL path.

2. **If no spec exists → create one**

   Create a new OpenAPI 3.1.0 YAML file in `resources/` using the naming convention `<verb>-<resource>[-<qualifier>].yaml` (e.g. `get-channels.yaml`, `send-text-message-to-chid.yaml`). Use this template:

   ```yaml
   openapi: 3.1.0
   info:
     title: <Human-readable API name>
     version: "1.0"

   servers:
     - url: <base URL, e.g. https://cliq.zoho.com>

   paths:
     <path>:
       <method>:
         summary: <One-sentence description of what the API does.>
         parameters:          # omit section if no query params
           - name: <param>
             in: query
             required: <true|false>
             schema:
               type: <string|integer|boolean>
             description: <What this parameter controls.>
         requestBody:         # omit section if GET or no body
           required: true
           content:
             application/json:
               schema:
                 type: object
                 required:
                   - <required_field>
                 properties:
                   <field>:
                     type: <string|integer|boolean|array|object>
                     description: <What this field means.>
                     example: <representative value>
                     enum:        # include only if the field has a fixed set of values
                       - <value1>
                       - <value2>
   ```

   Populate it with the exact URL, method, parameters, and body shape observed during the analysis. Use the actual field names and types from the real response/request — not guesses.

3. **If a spec already exists → check if it needs updating**

   Read the existing YAML and compare it against what was observed during analysis:
   - Are all observed request fields present? Add any that are missing.
   - Are field types and enum values accurate? Correct any that are wrong.
   - Is the summary still accurate? Update it if the understanding has changed.

   Only edit the file if there is a genuine discrepancy — do not rewrite specs that are already accurate.

4. **Update `resources/catalog.md`**

   - For a **new file**: add a row to the appropriate domain section in `catalog.md` with the filename and a concise description matching the YAML's `summary`. If no domain section fits, add a new section.
   - For an **updated file**: update the description in the existing catalog row if it changed.

#### Rules

- Only write specs for APIs that **succeeded** (exit code `0`, no error payload). Do not write specs for failed APIs.
- Base all field names, types, and examples on **observed** request/response data, not on assumptions.
- Keep specs minimal — only include fields that were actually used or observed. Do not add speculative fields.
- Never delete an existing YAML file, even if the API was not exercised in this session.

---

## Registry — Persisting Endpoints for Reuse

Register endpoints that will be called again in future sessions or by other agents:

```bash
$CLI api registry add \
  --id "<product>-<resource>-<operation>" \
  --url "<URL template>" \
  --method GET \
  --purpose "Brief description of what this endpoint returns"
```

Before adding, check if the entry already exists to avoid `REGISTRY_ENTRY_ALREADY_EXISTS`:

```bash
$CLI api registry list | jq '.[] | select(.id == "<id>")'
# If found, use update instead:
$CLI api registry update --id "<id>" --purpose "Updated description"
```

---

## Output File Conventions

- Both output files use **Markdown** with fenced code blocks for JSON.
- JSON bodies must be **prettified** (2-space indent).
- If an array in the response contains more than 5 items, truncate to 3 and append a `// ... <n> more items` comment.
- Sensitive values (tokens, passwords) must be **redacted** as `"<redacted>"` before writing.
- Do **not** overwrite existing sections in the output files. Always append new sections at the bottom.

---

## Edge Cases

| Situation | Behaviour |
|-----------|-----------|
| API requires an entity (e.g. channel ID) that doesn't exist yet | Create it fresh as part of Step 1 test-data setup. Record the ID in a session scratch variable. |
| API returns `200` with a product-specific error payload (`status: "failure"`) | Treat as failure and record in failure report. |
| API requires elevated permissions not held by any configured account | Record in failure report with reason "Insufficient permissions — requires manual testing with admin account". |
| An API was already registered via `api registry add` in a previous session | Call `api registry show --id <ID>` to retrieve the existing definition, validate the URL matches, and skip re-registration. |
| Rate limit hit (HTTP 429 inside `API_ERROR`) | Wait 2 seconds and retry once. If still failing, record as failure with reason "Rate limited". |
| Placeholder value is ambiguous (e.g. `{channelId}` vs `{chid}`) | Infer from the product's API conventions and note the assumption in the catalog. |
| `needs_reauth` is `true` mid-session | Call `$CLI account re-auth --name <ACCOUNT>` and retry immediately. If re-auth fails with `AUTH_FAILURE`, record all remaining APIs as blocked and stop. |
| A `resources/` YAML spec exists but has wrong field names or types | Note the discrepancy during analysis, complete the session, then include it in the Step 7 update permission prompt before making any changes. |
| A new API was analysed that has no entry in `resources/catalog.md` | Create the YAML file and add the catalog row directly in Step 7 — no permission needed for new files. |

---

## Allowed Domains

The CLI only accepts URLs from these Zoho hosts. Any other host returns `HOST_NOT_ALLOWED`:

- `zoho.com`
- `zoho.eu`
- `zoho.in`
- `zoho.com.au`
- `zohoapis.com`
- `zohoapis.in`

---

## Complete Error Code Reference

| Code | Exit | Action |
|---|---|---|
| `ACCOUNT_NOT_FOUND` | 1 | Run `account list` — use a valid account name. |
| `ACCOUNT_ALREADY_EXISTS` | 1 | Choose a different account name or remove the existing account first. |
| `NO_DEFAULT_ACCOUNT` | 1 | Run `account set-default --name <NAME>` or pass `--account`. |
| `AUTH_FAILURE` | 2 | Verify credentials; re-add the account if persistent. |
| `NEEDS_REAUTH` | 2 | Run `account re-auth --name <NAME>` then retry. |
| `API_ERROR` | 1 | Inspect the `data` field in stderr for the Zoho error details. |
| `HOST_NOT_ALLOWED` | 1 | Use a URL under `zoho.com`, `zohoapis.com`, etc. |
| `INVALID_ARGS` | 1 | A required flag is missing — run the command with `--help`. |
| `IO_ERROR` | 1 | Verify file path exists and is readable (e.g., for `--body-file`). |
| `KEYCHAIN_ERROR` | 1 | Check OS keychain permissions; on Linux verify libsecret/GNOME Keyring. |
| `ACCOUNT_DOMAIN_BLOCKED` | 1 | Only customer Zoho accounts permitted; use a non-`@zohocorp.*` account. |
| `EMAIL_REQUIRED` | 1 | Ensure OAuth scopes include user profile permissions. |
| `STATE_MISMATCH` | 1 | Indicates possible CSRF attack; discard and re-run the login command. |
| `LOGIN_TIMEOUT` | 1 | Browser callback not received within 120 seconds; ensure browser opened and re-run. |
| `INTERNAL_ERROR` | 1 | Unhandled internal exception; file a bug report with the full stderr output. |
| `REGISTRY_ENTRY_NOT_FOUND` | 1 | Run `api registry list` to see valid IDs. |
| `REGISTRY_ENTRY_ALREADY_EXISTS` | 1 | Use `api registry update --id <ID>` instead of `add`. |
| `SESSION_NOT_FOUND` | 1 | Run `trace session list` to see valid session IDs. |
| `SESSION_AMBIGUOUS` | 1 | Use `--id` instead of `--name` to target a specific session. |
| `EXPORT_PATH_NOT_SET` | 1 | Run `trace config set --default-export-path <PATH>`. |
