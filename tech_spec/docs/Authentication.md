# Zoho REST API — Self-Client Authentication

This document describes how to authenticate with the Zoho REST API using the **Self-Client** OAuth 2.0 flow. Self-Client is intended for server-side or CLI applications that act on behalf of a single user without requiring browser-based authorization redirects.

> **Note:** Even in Self-Client mode, a `redirect_uri` must be registered and included in token requests. It will be validated for presence but **will not be used** (no redirect occurs).

---

## Prerequisites

- A Zoho account with access to [Zoho Developer Console](https://accounts.zoho.com/developerconsole)
- The API scopes your application requires

---

## Overview

The Self-Client flow has four steps:

1. [Register a Self-Client and obtain credentials](#step-1-register-a-self-client)
2. [Generate a Grant Token from the Developer Console](#step-2-generate-a-grant-token)
3. [Exchange the Grant Token for Access and Refresh Tokens](#step-3-exchange-grant-token-for-access--refresh-tokens)
4. [Refresh the Access Token using the Refresh Token](#step-4-refresh-the-access-token)

---

## Step 1: Register a Self-Client

1. Go to [https://accounts.zoho.com/developerconsole](https://accounts.zoho.com/developerconsole)
2. Click **Add Client**.
3. Select **Self Client** as the client type.
4. Provide the following details:
   - **Client Name** — A name for your application.
   - **Homepage URL** — Your application's homepage (e.g. `http://localhost`).
   - **Authorized Redirect URIs** — A valid URI (e.g. `http://localhost`). This value must be present and match what you provide in token requests, but will **not** be called or redirected to.
5. Click **Create**.

Upon success, you receive a **Client ID** and **Client Secret**.

---

## Step 2: Generate a Grant Token

Unlike the standard web flow, Self-Client does not redirect you to a browser. You generate the grant token directly from the Developer Console.

1. In the Developer Console, open your Self Client application.
2. Click the **Generate Code** tab.
3. Enter the **Scope(s)** your application requires (comma-separated).  
   Example: `ZohoAPI.Resource.READ,ZohoAPI.Resource.WRITE`
4. Set the **Time Duration** (how long the grant code is valid — maximum 10 minutes).
5. Optionally enter a **Scope Description**.
6. Click **Generate**.

A one-time **grant code** (authorization code) is displayed. Copy it immediately — it expires quickly.

**Example grant code:**

```
1000.dd7e47321d48b8a7e312e3d6eb1a9bb8.b6c07ac766ec11da98bf6a261e24dca4
```

---

## Step 3: Exchange Grant Token for Access & Refresh Tokens

Use the grant code from Step 2 to obtain a long-lived refresh token and a short-lived access token.

**Endpoint:**

```
POST https://accounts.zoho.com/oauth/v2/token
```

**Parameters:**

| Parameter       | Description                                                    | Required |
| --------------- | -------------------------------------------------------------- | -------- |
| `code`          | The grant code generated in Step 2.                            | Yes      |
| `client_id`     | Client ID from Step 1.                                         | Yes      |
| `client_secret` | Client Secret from Step 1.                                     | Yes      |
| `redirect_uri`  | Must match the URI registered in Step 1. Will not be called.   | Yes      |
| `grant_type`    | `authorization_code`                                           | Yes      |
| `scope`         | Same scopes used to generate the grant code.                   | Yes      |

**Sample Request:**

```http
POST /oauth/v2/token HTTP/1.1
Host: accounts.zoho.com
Content-Type: application/x-www-form-urlencoded

code=1000.dd7e47321d48b8a7e312e3d6eb1a9bb8.b6c07ac766ec11da98bf6a261e24dca4
&grant_type=authorization_code
&scope=ZohoAPI.Resource.READ,ZohoAPI.Resource.WRITE
&client_id=1000.0SRSZSY37WMZ69405H3K3TMYI2239V
&client_secret=39c689de68c712fa5f1f06c3b1319ab98f59fa921b
&redirect_uri=http://localhost
```

**Sample Response:**

```json
{
  "access_token": "1000.70d737e7cc1d8869123f796363f55345.830d0dc7ea80c404ace4a261d1b710d4",
  "refresh_token": "1000.8ecd474019e31d52d2f94aad659c5cb7.4638677ebc14f2f2ee410b6dfb6cebdc",
  "token_type": "Bearer",
  "expires_in": 3600000
}
```

**Response Fields:**

| Field           | Description                                                              |
| --------------- | ------------------------------------------------------------------------ |
| `access_token`  | Token used to authenticate API requests. Valid for **1 hour**.           |
| `refresh_token` | Permanent token used to obtain new access tokens. Store this securely.   |
| `token_type`    | Always `Bearer`.                                                         |
| `expires_in`    | Access token lifetime in milliseconds (`3600000` = 1 hour).              |

> **Important:** Store the `refresh_token` securely. It does not expire but is limited to 20 uses before a new one must be obtained.

---

## Step 4: Refresh the Access Token

Access tokens expire after 1 hour. Use the refresh token to obtain a new one without repeating Steps 2–3.

**Endpoint:**

```
POST https://accounts.zoho.com/oauth/v2/token
```

**Parameters:**

| Parameter       | Description                                        | Required |
| --------------- | -------------------------------------------------- | -------- |
| `client_id`     | Client ID from Step 1.                             | Yes      |
| `client_secret` | Client Secret from Step 1.                         | Yes      |
| `redirect_uri`  | Must match the URI registered in Step 1.           | Yes      |
| `grant_type`    | `refresh_token`                                    | Yes      |
| `refresh_token` | The refresh token obtained in Step 3.              | Yes      |
| `scope`         | The scopes to authorize for the new access token.  | Yes      |

**Sample Request:**

```http
POST /oauth/v2/token HTTP/1.1
Host: accounts.zoho.com
Content-Type: application/x-www-form-urlencoded

refresh_token=1000.8ecd474019e31d52d2f94aad659c5cb7.4638677ebc14f2f2ee410b6dfb6cebdc
&grant_type=refresh_token
&scope=ZohoAPI.Resource.READ,ZohoAPI.Resource.WRITE
&client_id=1000.0SRSZSY37WMZ69405H3K3TMYI2239V
&client_secret=39c689de68c712fa5f1f06c3b1319ab98f59fa921b
&redirect_uri=http://localhost
```

**Sample Response:**

```json
{
  "access_token": "1000.newtoken123abc.newhash456def",
  "token_type": "Bearer",
  "expires_in": 3600000
}
```

---

## Using the Access Token

Include the access token in the `Authorization` header for every API request.

**Required Headers:**

| Header          | Value                                  |
| --------------- | -------------------------------------- |
| `Authorization` | `Zoho-oauthtoken <access_token>`       |
| `Content-Type`  | `application/json`                     |

**Sample API Request:**

```http
GET /api/v2/resource HTTP/1.1
Host: api.zoho.com
Authorization: Zoho-oauthtoken 1000.70d737e7cc1d8869123f796363f55345.830d0dc7ea80c404ace4a261d1b710d4
Content-Type: application/json
```

---

## Revoking the Refresh Token

To revoke a refresh token and invalidate all tokens associated with it:

**Endpoint:**

```
POST https://accounts.zoho.com/oauth/v2/token/revoke
```

**Parameters:**

| Parameter | Description                       | Required |
| --------- | --------------------------------- | -------- |
| `token`   | The refresh token to be revoked.  | Yes      |

**Sample Request:**

```http
POST /oauth/v2/token/revoke HTTP/1.1
Host: accounts.zoho.com
Content-Type: application/x-www-form-urlencoded

token=1000.8ecd474019e31d52d2f94aad659c5cb7.4638677ebc14f2f2ee410b6dfb6cebdc
```

---

## Multiple Data Centers

Zoho is hosted across multiple data centers. Use the domain that matches your organization.

| Data Center    | Short Name | Domain          | Accounts URL                         |
| -------------- | ---------- | --------------- | ------------------------------------ |
| United States  | `us`       | `.com`          | `https://accounts.zoho.com`          |
| Europe         | `eu`       | `.eu`           | `https://accounts.zoho.eu`           |
| India          | `in`       | `.in`           | `https://accounts.zoho.in`           |
| Australia      | `au`       | `.au`           | `https://accounts.zoho.com.au`       |
| China          | `cn`       | `.cn`           | `https://accounts.zoho.com.cn`       |
| Japan          | `jp`       | `.jp`           | `https://accounts.zoho.jp`           |
| Saudi Arabia   | `sa`       | `.sa`           | `https://accounts.zoho.sa`           |
| United Kingdom | `uk`       | `.uk`           | `https://accounts.zoho.uk`           |
| Canada         | `ca`       | `.zohocloud.ca` | `https://accounts.zohocloud.ca`      |

> **Note:** Replace `accounts.zoho.com` in this guide with the appropriate domain for your organization.
