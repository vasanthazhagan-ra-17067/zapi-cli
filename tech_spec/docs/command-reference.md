# zapi — Command Reference (Post-Refactor)

> **Canonical specification for the redesigned `zapi` CLI surface.**
> This document is the authoritative source for all command names, flags, and param names
> after the CLI naming refactor (stories 26–36). HELP.md is regenerated from this spec.

---

## Binary

| Platform | Path |
|---|---|
| macOS Apple Silicon | `build/osx-arm64/zapi` |
| macOS Intel | `build/osx-x64/zapi` |
| Linux x64 | `build/linux-x64/zapi` |
| Linux ARM64 | `build/linux-arm64/zapi` |
| Windows x64 | `build/win-x64/zapi.exe` |
| Windows ARM64 | `build/win-arm64/zapi.exe` |

`config.SetApplicationName("zapi")` — all `--help` output uses `zapi`.

---

## Deprecation Contract

All removed/renamed commands and flags must remain wired as **deprecated aliases**.
When a deprecated alias is invoked, the CLI:

1. Prints to **stderr** (before any stdout): `Deprecation: '<old>' is deprecated; use '<new>' instead.`
2. Executes the command normally — exit code and stdout are identical to the canonical form.
3. Does **not** appear in `--help` output.

---

## Global Flags

| Flag | Short | Description |
|---|---|---|
| `--account <ACCOUNT>` | `-a` | Override default account for this invocation |
| `--no-input` | | Disable all interactive prompts |
| `--help` | `-h` | Print help for the current command |
| `--version` | | Print CLI version and exit |

---

## `account` group

### `account login`

```
zapi account login [--name <NAME>] [--scope <SCOPES>]
```

| Flag | Required | Description |
|---|---|---|
| `--name <NAME>` | No | Account alias; derived from email if omitted |
| `--scope <SCOPES>` | No* | Comma-separated OAuth scopes; additive to scope-file |

*At least one scope must be resolved from `--scope` or the configured scope-file.

**Example:**
```bash
zapi account login --scope "ZohoCliq.Channels.READ,ZohoCliq.Messages.CREATE"
zapi account login --name work
```

---

### `account list`

```
zapi account list
```

No flags. Returns all accounts (tokens masked).

**Example:**
```bash
zapi account list
```

---

### `account show`

```
zapi account show (--name | --email | --zuid) <VALUE>
```

| Flag | Description |
|---|---|
| `--name <NAME>` | Account alias |
| `--email <EMAIL>` | Identify by email |
| `--zuid <ZUID>` | Identify by Zoho User ID *(was: `--zuidstring`)* |

Exactly one identifier required. Deprecated alias: `--zuidstring` (emits deprecation warning).

**Example:**
```bash
zapi account show --name myaccount
zapi account show --email user@example.com
zapi account show --zuid 123456789
```

---

### `account set-default`

```
zapi account set-default (--name | --email | --zuid) <VALUE>
```

Exactly one identifier required. `--zuidstring` is a deprecated alias for `--zuid`.

**Example:**
```bash
zapi account set-default --name myaccount
```

---

### `account use` *(new — positional shorthand for `set-default`)*

```
zapi account use <NAME>
```

| Arg | Description |
|---|---|
| `<NAME>` | Positional account alias (no flag needed) |

Sets the default account. Equivalent to `account set-default --name <NAME>`.

**Example:**
```bash
zapi account use work
```

---

### `account remove`

```
zapi account remove (--name | --email | --zuid) <VALUE>
```

Exactly one identifier required. `--zuidstring` is a deprecated alias for `--zuid`.

**Example:**
```bash
zapi account remove --name myaccount
```

---

### `account refresh` *(was: `account re-auth`)*

```
zapi account refresh (--name | --email | --zuid) <VALUE>
```

Re-authenticates using stored refresh token. Deprecated alias: `re-auth` (command name).

**Example:**
```bash
zapi account refresh --name myaccount
```

---

### `account rename`

```
zapi account rename (--name | --email | --zuid) <VALUE> --to <NEW_NAME>
```

| Flag | Required | Description |
|---|---|---|
| `--name / --email / --zuid` | Yes (one) | Identify account to rename |
| `--to <NEW_NAME>` | Yes | New alias *(was: `--new-name`)* |

Deprecated alias: `--new-name` (flag name).

**Example:**
```bash
zapi account rename --name work --to work-eu
zapi account rename --email user@example.com --to personal
```

---

### `account scope add` *(was: top-level `scope add`)*

```
zapi account scope add --scope <SCOPES> [-a <ACCOUNT>] [--port <PORT>]
```

| Flag | Required | Description |
|---|---|---|
| `--scope <SCOPES>` | Yes | Comma-separated OAuth scope(s) to add |
| `--account / -a <ACCOUNT>` | No | Target account (uses default if omitted) |
| `--port <PORT>` | No | Callback port for incremental auth (default: 8085) |

Deprecated alias: top-level `scope add` (command path).

**Example:**
```bash
zapi account scope add --scope "ZohoDesk.Tickets.READ,ZohoDesk.Reports.READ"
zapi account scope add --scope "ZohoMail.messages.READ" --account eu-staging
```

---

### `account scope list` *(was: top-level `scope list`)*

```
zapi account scope list [-a <ACCOUNT>]
```

Deprecated alias: top-level `scope list` (command path).

**Example:**
```bash
zapi account scope list
zapi account scope list --account eu-staging
```

---

## `api` group

### `api request` *(was: `api call`)*

```
zapi api request --url <URL> -X <METHOD>
    [--body <JSON>] [--body-file <FILE>]
    [--header <K:V>]... [--query <k=v>]...
    [-a <ACCOUNT>]
```

| Flag | Short | Required | Description |
|---|---|---|---|
| `--url <URL>` | | Yes | Full Zoho API endpoint URL |
| `--method <METHOD>` | `-X` | Yes | HTTP verb: `GET POST PUT PATCH DELETE` |
| `--body <JSON>` | | No | Inline JSON request body |
| `--body-file <FILE>` | | No | Read JSON body from file |
| `--header <K:V>` | | No | Repeatable: add request header |
| `--query <k=v>` | | No | Repeatable: add query parameter |
| `--account <ACCOUNT>` | `-a` | No | Override default account |

Short alias: `req`. Deprecated command alias: `call`.

**Examples:**
```bash
zapi api request --url "https://www.zohoapis.com/cliq/v2/channels" -X GET

zapi api req \
  --url "https://www.zohoapis.com/cliq/v2/channels" \
  -X GET \
  --query "limit=50" --query "page=1"

zapi api request \
  --url "https://www.zohoapis.com/cliq/v2/channels/general/message" \
  -X POST \
  --body '{"text": "Hello from zapi"}'

zapi api request \
  --url "https://www.zohoapis.com/crm/v6/Leads/1234567890" \
  -X PUT \
  --header "Content-Type:application/json" \
  --body-file ./payload.json \
  --account eu-staging
```

---

### `api endpoints list` *(was: `api registry list`)*

```
zapi api endpoints list
```

Deprecated alias branch: `api registry`.

**Example:**
```bash
zapi api endpoints list
```

---

### `api endpoints add` *(was: `api registry add`)*

```
zapi api endpoints add --id <ID> --url <URL> --method <METHOD> --purpose <TEXT>
```

| Flag | Required | Description |
|---|---|---|
| `--id <ID>` | Yes | Unique identifier for this endpoint entry |
| `--url <URL>` | Yes | Full endpoint URL (Zoho domain, validated) |
| `--method <METHOD>` | Yes | `GET POST PUT PATCH DELETE` |
| `--purpose <TEXT>` | Yes | Human-readable description |

**Example:**
```bash
zapi api endpoints add \
  --id cliq-channels \
  --url https://cliq.zoho.com/api/v2/channels \
  --method GET \
  --purpose "List all Cliq channels"
```

---

### `api endpoints update` *(was: `api registry update`)*

```
zapi api endpoints update --id <ID> [--url <URL>] [--method <METHOD>] [--purpose <TEXT>]
```

At least one of `--url`, `--method`, `--purpose` is required.

**Example:**
```bash
zapi api endpoints update --id cliq-channels --purpose "Fetch all Cliq channels"
```

---

### `api endpoints show` *(was: `api registry show`)*

```
zapi api endpoints show --id <ID>
```

**Example:**
```bash
zapi api endpoints show --id cliq-channels
```

---

### `api endpoints remove` *(was: `api registry remove`)*

```
zapi api endpoints remove --id <ID>
```

**Example:**
```bash
zapi api endpoints remove --id cliq-channels
```

---

## `trace` group

> The intermediate `session` nesting level is removed. All session commands sit directly
> under `trace`. The `trace session <cmd>` path remains as a deprecated alias.

### `trace start` *(was: `trace session start`)*

```
zapi trace start --name <NAME> [--export-path <PATH>]
```

| Flag | Required | Description |
|---|---|---|
| `--name <NAME>` | Yes | Session name (`[a-zA-Z0-9_.-]`, max 64 chars) |
| `--export-path <PATH>` | No | Output file or directory |

**Example:**
```bash
zapi trace start --name my-session --export-path /tmp/traces/
zapi trace start --name api-audit --export-path /tmp/traces/audit.json
```

---

### `trace list` *(was: `trace session list`)*

```
zapi trace list
```

**Example:**
```bash
zapi trace list
```

---

### `trace export` *(was: `trace session export`)*

```
zapi trace export (--id <UUID> | --name <NAME>) [--type api|pex] [--truncate-body <N>]
```

| Flag | Required | Description |
|---|---|---|
| `--id <UUID>` | One of | Session UUID (from `trace list`) |
| `--name <NAME>` | One of | Session name (error if ambiguous) |
| `--type <TYPE>` | No | Filter: `api` or `pex` |
| `--truncate-body <N>` | No | Truncate body fields to N chars in output only |

**Example:**
```bash
zapi trace export --id 550e8400-e29b-41d4-a716-446655440000
zapi trace export --name my-session --type api --truncate-body 200
```

---

### `trace close` *(was: `trace session close`)*

```
zapi trace close (--id <UUID> | --name <NAME>) [--drain-timeout <MS>]
```

| Flag | Required | Description |
|---|---|---|
| `--id <UUID>` | One of | Session UUID |
| `--name <NAME>` | One of | Session name |
| `--drain-timeout <MS>` | No | Drain window before sealing, milliseconds (default: 5000) *(was: `--wait-ms`)* |

Deprecated alias: `--wait-ms` (flag name).

**Example:**
```bash
zapi trace close --id 550e8400-e29b-41d4-a716-446655440000
zapi trace close --name my-session --drain-timeout 3000
```

---

### `trace reopen` *(was: `trace session reopen`)*

```
zapi trace reopen (--id <UUID> | --name <NAME>)
```

**Example:**
```bash
zapi trace reopen --id 550e8400-e29b-41d4-a716-446655440000
```

---

### `trace remove` *(was: `trace session remove`)*

```
zapi trace remove (--id <UUID> | --name <NAME>)
```

**Example:**
```bash
zapi trace remove --id 550e8400-e29b-41d4-a716-446655440000
```

---

### `trace config set`

```
zapi trace config set --default-export-path <PATH>
```

*(No change to this command.)*

**Example:**
```bash
zapi trace config set --default-export-path /tmp/traces/
```

---

### `trace config show`

```
zapi trace config show
```

*(No change to this command.)*

**Example:**
```bash
zapi trace config show
```

---

## `config` group

### `config set env-file`

```
zapi config set env-file <PATH>
```

*(No change.)*

**Example:**
```bash
zapi config set env-file ~/.zapi/.env
```

---

### `config set scope-file`

```
zapi config set scope-file <PATH>
```

*(No change.)*

**Example:**
```bash
zapi config set scope-file ~/.zapi/scopes.txt
```

---

### `config set app-dir`

```
zapi config set app-dir <PATH>
```

*(No change.)*

**Example:**
```bash
zapi config set app-dir ~/projects/myapp/.zapi
```

---

### `config show`

```
zapi config show
```

*(No change.)*

**Example:**
```bash
zapi config show
```

---

## `util` group

### `util timestamp` *(was: `util time-ms`)*

```
zapi util timestamp
```

Outputs current UTC time as Unix milliseconds. Deprecated alias: `time-ms`.

**Example:**
```bash
zapi util timestamp
# → {"ts": 1743465000000}
```

---

### `util now` *(was: `util time-now`)*

```
zapi util now
```

Outputs current IST time as `DD/MM/YY HH:mm:ss.fff`. Deprecated alias: `time-now`.

**Example:**
```bash
zapi util now
# → {"now": "01/04/26 14:30:00.427"}
```

---

### `util uuid`

```
zapi util uuid
```

*(No change.)*

**Example:**
```bash
zapi util uuid
# → {"uuid": "550e8400-e29b-41d4-a716-446655440000"}
```

---

## Error Code Changes

| Old | New | Scope |
|---|---|---|
| `REGISTRY_ENTRY_ALREADY_EXISTS` | `ENDPOINT_ALREADY_EXISTS` | `api endpoints add` |
| `REGISTRY_ENTRY_NOT_FOUND` | `ENDPOINT_NOT_FOUND` | `api endpoints show / update / remove` |

---

## Full Rename Map

| Old | New | Category |
|---|---|---|
| `zapi-cli` (binary) | `zapi` | Binary |
| `api call` | `api request` (alias: `req`) | Command |
| `api registry *` | `api endpoints *` | Branch |
| `trace session *` | `trace *` | Nesting flattened |
| `scope add/list` (top-level) | `account scope add/list` | Branch moved |
| `account set-default` | `account use <NAME>` | Shorthand added |
| `account re-auth` | `account refresh` | Command renamed |
| `util time-ms` | `util timestamp` | Command renamed |
| `util time-now` | `util now` | Command renamed |
| `--zuidstring` | `--zuid` | Flag on 5 account commands |
| `--new-name` | `--to` (on `account rename`) | Flag renamed |
| `--wait-ms` | `--drain-timeout` (on `trace close`) | Flag renamed |
| `REGISTRY_ENTRY_ALREADY_EXISTS` | `ENDPOINT_ALREADY_EXISTS` | Error code |
| `REGISTRY_ENTRY_NOT_FOUND` | `ENDPOINT_NOT_FOUND` | Error code |
