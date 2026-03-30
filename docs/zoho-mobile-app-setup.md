# Setting Up a Zoho Mobile Application for `account login`

This guide walks you through creating a **Mobile Application** client in the Zoho Developer Console and configuring it for use with `zapi-cli account login`.

---

## Why Mobile Application type?

The `account login` command uses Zoho's `/oauth/v2/mobile/auth` endpoint, which is only available to clients registered as **Mobile Application** (or **Desktop Application**) type. Other types (Self-Client, Server-based) use the standard `/oauth/v2/auth` endpoint and do not support the mobile-specific callback parameters (`gt_sec`, `gt_hash`, `accounts-server`).

---

## Prerequisites

- A Zoho account in the datacenter where your organization is registered (IN, US, EU, AU, etc.)
- Access to the [Zoho API Console](https://api-console.zoho.com) for your datacenter:

| Datacenter | Console URL |
|------------|-------------|
| India (IN) | https://api-console.zoho.in |
| US         | https://api-console.zoho.com |
| Europe (EU)| https://api-console.zoho.eu |
| Australia (AU) | https://api-console.zoho.com.au |
| Japan (JP) | https://api-console.zoho.jp |
| Canada (CA) | https://api-console.zohocloud.ca |
| Saudi Arabia (SA) | https://api-console.zoho.sa |
| UK         | https://api-console.zoho.uk |
| China (CN) | https://api-console.zoho.com.cn |

---

## Step 1 — Create a Mobile Application

1. Open the API Console for your datacenter (e.g., https://api-console.zoho.in for IN).
2. Click **GET STARTED** or **Add Client**.
3. Select **Mobile Application** as the client type.

   > ⚠️ Do **not** select Self-Client, Server-based Applications, or Single Page App. Only Mobile Application (and Desktop Application) work with `account login`.

4. Fill in the application details:

   | Field | Value |
   |-------|-------|
   | **Client Name** | Any descriptive name, e.g. `zapi-cli dev` |
   | **Homepage URL** | Any valid URL, e.g. `http://localhost` |
   | **Authorized Redirect URIs** | `http://localhost:8085/callback` |

   > The callback port is **fixed at 8085**. Register `http://localhost:8085/callback` as the redirect URI. No other port is supported.

5. Click **CREATE**.

---

## Step 2 — Copy Your Credentials

After creating the client, the console shows:

- **Client ID** — looks like `1000.XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX`
- **Client Secret** — a long alphanumeric string

Copy both values. The client secret is shown only once on creation; if you lose it, regenerate it from the client's **Settings** tab.

---

## Step 3 — Configure Your Environment

### Configure env-file (required)

Create a `.env` file and tell zapi-cli where to find it. You only need to do this once — the path is saved in the platform config directory and loaded automatically on every subsequent invocation.

```bash
# 1. Create the .env file
touch ~/.zapi-cli.env
```

Edit the file and fill in your credentials:

```dotenv
ZOHO_CLIENT_ID=1000.XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
ZOHO_CLIENT_SECRET=xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

```bash
# 2. Persist the path — run once, never needs repeating
zapi-cli config set env-file /absolute/path/to/.zapi-cli.env

# 3. Verify
zapi-cli config show
```

After this, every `zapi-cli` invocation loads the file automatically regardless of the working directory. See [`config set env-file`](HELP.md#config-set-env-file) in the command reference for full details.

---

## Step 3b — Configure Your Scope File (optional)

You can define the OAuth scopes you need in a plain-text file (one scope per line or comma-separated) and tell zapi-cli where to find it. This avoids repeating `--scope` on every login.

```bash
# Create a scope file
cat > ~/.zapi-cli-scopes.txt << 'EOF'
ZohoMail.messages.READ
ZohoMail.folders.READ
ZohoCliq.Channels.READ
EOF

# Persist the path — run once
zapi-cli config set scope-file ~/.zapi-cli-scopes.txt
```

> **Note:** `AaaServer.profile.READ` is always included automatically — you do not need to add it to your scope file.

Alternatively, pass `--scope` directly when running `account login`.

---

## Step 4 — Verify the Redirect URI

Before running `account login`, confirm that `http://localhost:8085/callback` is listed under **Authorized Redirect URIs** for your client in the API Console. Without this, Zoho returns an `Invalid Redirect URI` error and the login is aborted before any callback is received.

Exactly one redirect URI is required: `http://localhost:8085/callback`. The port is fixed and cannot be changed.

To add or edit redirect URIs:

1. Open the API Console and click on your Mobile Application.
2. Go to the **Settings** tab.
3. Under **Authorized Redirect URIs**, add `http://localhost:8085/callback`.
4. Click **Update**.

---

## Step 5 — Run `account login`

With credentials configured (Step 3) and the redirect URI registered, run:

```bash
# If scope-file is configured (Step 3b), no flags are required:
zapi-cli account login

# To specify scopes directly:
zapi-cli account login --scope "ZohoMail.messages.READ,ZohoCliq.Channels.READ"

# Optionally, name the account:
zapi-cli account login --name "myaccount"
```

For the full command reference including all flags and error codes, see [`account login`](HELP.md#account-login) in HELP.md.

The browser will open automatically. Sign in within **120 seconds**. The datacenter is auto-detected from your login response. On success:

```json
{"status":"ok","data":{"name":"myaccount","dc":"in"}}
```

---

## Scopes

Request only the scopes your use case needs. Common scopes:

| Scope | Access |
|-------|--------|
| `AaaServer.profile.READ` | Read user profile / identity |
| `ZohoMail.messages.READ` | Read mail messages |
| `ZohoMail.folders.READ` | Read mail folder list |
| `ZohoCliq.Channels.READ` | Read Cliq channels |
| `ZohoCliq.Messages.CREATE` | Post messages to Cliq |
| `ZohoPeople.employee.READ` | Read People (HR) employee data |

For a full list, see the [Zoho OAuth Scopes reference](https://www.zoho.com/accounts/protocol/oauth/scopes.html).

---

## Troubleshooting

| Error | Cause | Fix |
|-------|-------|-----|
| `Invalid Client` / `Invalid Client ID` | Wrong datacenter. The client was created on `zoho.in` but `ZOHO_CLIENT_ID` belongs to a different DC. | Ensure your `ZOHO_CLIENT_ID` matches the datacenter you are logged into. |
| `Invalid Redirect URI` | `http://localhost:8085/callback` is not in the client's Authorized Redirect URIs. | Add exactly `http://localhost:8085/callback` in the API Console → client Settings tab. |
| `oauth_app_blocked` | The application is blocked, suspended, or in draft state in the API Console. | Open the API Console and check the application status. Contact Zoho support if blocked. |
| `LOGIN_TIMEOUT` | The 120-second window expired before you completed the browser login. | Run the command again and sign in promptly. |
| `Mobile OAuth callback was missing required parameters` | The client is not of type Mobile Application (e.g., it's Self-Client). | Create a new client with type **Mobile Application**. |
| `Zoho did not return an encrypted client secret (gt_sec)` | Client type doesn't use RSA key exchange. | Set `ZOHO_CLIENT_SECRET` in your `.env` file. This is normal for some Mobile Application configurations. |
| `ENV_FILE_NOT_CONFIGURED` | `ZOHO_CLIENT_ID` is not set when running `account login`. | Run `zapi-cli config set env-file /path/.env` and ensure `ZOHO_CLIENT_ID` is set in the file. |
| `SCOPE_FILE_NOT_CONFIGURED` | No scopes provided and no scope-file is configured. | Pass `--scope` or run `zapi-cli config set scope-file /path/scopes.txt`. |

---

## Notes

- The `.env` file is git-ignored. Never commit credentials to version control.
- The client secret is sensitive — treat it like a password.
- The `account login` command is intended for interactive developer workstations.
- See [HELP.md](HELP.md) for the full command reference including [`account login`](HELP.md#account-login), [`config set env-file`](HELP.md#config-set-env-file), [`config set scope-file`](HELP.md#config-set-scope-file), and [`config show`](HELP.md#config-show).
