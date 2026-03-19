# Zoho OAuth — Incremental Authorization & Scope Enhancement

**Source:** [Zoho Accounts OAuth Documentation](https://www.zoho.com/accounts/protocol/oauth/incremental-authorization.html)

---

## Overview

**Incremental authorization** is an OAuth implementation strategy that allows your app to request permissions from the user only when needed, rather than all upfront. It improves user experience by not overwhelming users with a large consent screen at sign-in.

### Advantages

- Users only grant permissions for features they actually use.
- Your app does not need to repeatedly ask for permissions already granted.

### Example

Consider an app integrating with Zoho Mail:

1. On sign-in, the app requests only basic profile info and the permission to view the inbox.
2. Later, when the user wants to send an email from within your app, the app can request the additional `send email` permission.
3. The user is only prompted to grant the new permission — not the ones already approved.

---

## How It Works

The flow consists of two sequential steps:

```
Step 1: POST /oauth/v2/token/scopeenhance
        → Returns a scope enhancement token

Step 2: GET  /oauth/v2/token/addextrascope
        → User consents → refresh token appended with new scopes
```

> **Note:** The `accounts-server-url` is datacenter-specific. See [Multiple Data Centers](Authentication.md#multiple-data-centers) for the full list of datacenter-specific accounts URLs.

---

## Step 1 — Get Scope Enhancement Token

Obtain a short-lived scope enhancement token by exchanging your existing refresh token.

### Endpoint

```
POST {accounts-server-url}/oauth/v2/token/scopeenhance
```

### Request Parameters

| Parameter       | Required | Description |
|----------------|----------|-------------|
| `client_id`     | ✅ Yes   | The unique ID of your application from the API console. |
| `client_secret` | ✅ Yes   | The unique secret for your app from the API console. |
| `grant_type`    | ✅ Yes   | Must be set to `update_scopes_token`. |
| `refresh_token` | ✅ Yes   | The refresh token to which additional scopes are to be appended. |

### Request Example

```http
POST https://accounts.zoho.com/oauth/v2/token/scopeenhance
Content-Type: application/x-www-form-urlencoded

client_id=1000.XXXXX&
client_secret=XXXXX&
grant_type=update_scopes_token&
refresh_token=1000.XXXXX
```

### Response Parameters

| Parameter     | Description |
|---------------|-------------|
| `access_token` | The scope enhancement token. Use this in Step 2 as `enhance_token`. |
| `token_type`   | Indicates the type of token generated (e.g., `Bearer`). |
| `expires_in`   | Time in seconds before the scope enhancement token expires. |

### Response Example

```json
{
  "access_token": "1000.XXXXX",
  "token_type": "Bearer",
  "expires_in": 600
}
```

### Error Codes

| Error                  | Cause |
|------------------------|-------|
| `invalid_client`        | The `accounts-server-url` is invalid (wrong datacenter). |
|                        | `client_id` is missing or invalid. |
|                        | `grant_type` is missing or not `update_scopes_token`. |
| `invalid_client_secret` | `client_secret` is missing or invalid. |
| `invalid_code`          | `refresh_token` is missing, invalid, or already used. |
|                        | `grant_type` value is incorrect (must be `update_scopes_token`). |
| `400 Bad Request`       | HTTP method is incorrect — must be `POST`. |

---

## Step 2 — Enhance Scope (User Consent Request)

Use the scope enhancement token to redirect the user to a consent screen for the additional scopes.

### Endpoint

```
GET {accounts-server-url}/oauth/v2/token/addextrascope
```

### Query Parameters

| Parameter      | Required | Description |
|----------------|----------|-------------|
| `client_id`    | ✅ Yes   | The unique ID of your application from the API console. |
| `response_type`| ✅ Yes   | Must be set to `update_scopes`. |
| `redirect_uri` | ✅ Yes   | URI to redirect the browser after grant/rejection. Must match the URI registered in the API console. Must start with `http://` or `https://`. <br>Example: `https://www.zylker.com/oauthredirect` |
| `scope`        | ✅ Yes   | The additional scopes to request from the user. These will appear on the consent screen. <br>Example: `ZohoCRM.settings.READ` |
| `enhance_token`| ✅ Yes   | The scope enhancement token received from Step 1. |
| `logout`       | ✅ Yes   | Set to `true` to terminate the user's session after they grant or reject permission. |

### Request Example

```
GET https://accounts.zoho.com/oauth/v2/token/addextrascope
  ?client_id=1000.XXXXX
  &response_type=update_scopes
  &redirect_uri=https://www.zylker.com/oauthredirect
  &scope=ZohoCRM.settings.READ
  &enhance_token=1000.XXXXX
  &logout=true
```

### Response

After authenticating the user, Zoho displays a consent screen showing the permissions your app is requesting.

- If the user **approves**, the refresh token (from Step 1) and its associated access tokens are appended with the additional scopes, and a success response is returned to `redirect_uri`.
- If the user **rejects**, a failure response is returned.

### Response Parameters (Redirect)

| Scenario | Redirect URL |
|----------|-------------|
| **Success** | `{redirect_uri}?status=success&scope_enhanced=true` |
| **Failure** | `{redirect_uri}?error=access_denied` |

### Response Examples

**Success:**
```
https://www.zylker.com/oauthredirect?status=success&scope_enhanced=true
```

**Failure:**
```
https://www.zylker.com/oauthredirect?error=access_denied
```

---

## Full Flow Summary

```
App                         Zoho Accounts Server              User
 |                                  |                           |
 |-- POST /scopeenhance ----------->|                           |
 |   (client_id, client_secret,     |                           |
 |    grant_type, refresh_token)    |                           |
 |<-- {access_token (enhance_token)}|                           |
 |                                  |                           |
 |-- Redirect User to /addextrascope with enhance_token ------->|
 |                                  |<-- User Views Consent ----|
 |                                  |<-- User Grants/Rejects----|
 |<-- Redirect to redirect_uri with status/error ---------------|
```

---

## Related Links

- [Introduction to OAuth 2.0](https://www.zoho.com/accounts/protocol/oauth.html)
- [OAuth Glossary](https://www.zoho.com/accounts/protocol/oauth-terminology.html)
- [OAuth Scope](https://www.zoho.com/accounts/protocol/oauth/scope.html)
- [Register Your App](https://www.zoho.com/accounts/protocol/oauth-setup.html)
- [Multi-DC Support](https://www.zoho.com/accounts/protocol/oauth/multi-dc.html)
- [Revoke OAuth Token](https://www.zoho.com/accounts/protocol/oauth/revoke-refresh-token.html)
- [OAuth Token Limits](https://www.zoho.com/accounts/protocol/oauth/token-limits.html)
