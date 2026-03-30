# Templates

This folder contains sample configuration files for zapi-cli.

## Scope Files

OAuth scope files are used by the `account login` command to request specific permissions from Zoho.

### Usage

Set the scope file path once:
```bash
zapi-cli config set scope-file /path/to/scopes.txt
```

Then use it with account login:
```bash
zapi-cli account login
```

### Sample Files

1. **scopes-simple.txt** — All scopes on a single comma-separated line. Most concise format.
2. **scopes-multiline.txt** — One scope per line. Most readable format.
3. **scopes-with-comments.txt** — Mixed with comments for organization and documentation. Supports both comma-separated and per-line formats.

### Format Rules

- Scopes must **not** have quotes
- One scope per line OR comma-separated on a single line
- Lines starting with `#` are treated as comments and ignored
- Empty lines are skipped
- Whitespace around scopes and commas is trimmed automatically

### Important Notes

- `AaaServer.profile.READ` is always included automatically — you do not need to add it to your scope file
- The scope file path is resolved to an absolute path and stored in `cli-settings.json`
- The file does not need to exist at configuration time — it is read when `account login` runs
