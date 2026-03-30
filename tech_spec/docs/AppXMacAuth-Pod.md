# AppXMacAuth Pod — Technical Documentation

> **Internal Name:** `IAM_ZMacAuth`
> **Version:** `1.0.4`
> **Platform:** macOS 10.10+
> **Language:** Objective-C
> **Purpose:** Zoho's internal macOS OAuth 2.0 authentication SDK — handles the complete lifecycle of login, token management, multi-account support, and logout for any Zoho macOS app.

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [File Structure](#file-structure)
4. [Environments & Build Types](#environments--build-types)
5. [Initialization](#initialization)
6. [Login Flow — Step by Step](#login-flow--step-by-step)
   - [Step 1 — SDK Initialization](#step-1--sdk-initialization)
   - [Step 2 — RSA Key Pair Generation](#step-2--rsa-key-pair-generation)
   - [Step 3 — Build the Login URL](#step-3--build-the-login-url)
   - [Step 4 — Display the Login WebView](#step-4--display-the-login-webview)
   - [Step 5 — Handle the OAuth Redirect](#step-5--handle-the-oauth-redirect)
   - [Step 6 — Token Exchange](#step-6--token-exchange-post-oauthv2token)
   - [Step 7 — Fetch User Info](#step-7--fetch-user-info-get-oauthuserinfo)
   - [Step 8 — Fetch Profile Photo](#step-8--fetch-profile-photo)
   - [Step 9 — Store Everything in Keychain](#step-9--store-everything-in-keychain)
7. [Token Refresh Flow](#token-refresh-flow)
8. [Logout Flow](#logout-flow)
9. [All API Endpoints](#all-api-endpoints)
10. [Keychain Storage Schema](#keychain-storage-schema)
11. [Data Models](#data-models)
12. [Multi-Account Support](#multi-account-support)
13. [Error Codes](#error-codes)
14. [Security Notes](#security-notes)
15. [Public API Reference](#public-api-reference)

---

## Overview

`AppXMacAuth` is Zoho's macOS OAuth 2.0 authentication pod used by the Zoho Mail (Trident) macOS app and other Zoho macOS apps. It wraps Zoho IAM's OAuth 2.0 flow end-to-end:

- Presents a `WKWebView` (or `ASWebAuthenticationSession` on macOS 10.15+) pointing to Zoho Accounts login page
- Exchanges the returned authorization code for access and refresh tokens via Zoho's token endpoint
- Fetches the logged-in user's profile info and photo
- Persists all credentials securely in the macOS Keychain
- Handles token expiry and silent refresh via the refresh token
- Supports multiple signed-in Zoho accounts simultaneously
- Posts `NSDistributedNotification` so app extensions stay in sync with keychain changes

---

## Architecture

```
ZMacAuth  (Public Facade — all static methods)
    │
    └──► ZMacAuthZIAMUtil  (Singleton — owns all state, orchestrates everything)
              │
              ├──► ZMacAuthLoginViewController    (Login WebView UI)
              │         └──► WKWebView / ASWebAuthenticationSession
              │
              ├──► ZMacAuthNetworkManager         (NSURLSession wrapper for all API calls)
              │
              ├──► ZMacAuthKeychainUtil (category) (All keychain read/write logic)
              │         └──► ZMacAuthKeyChainWrapper  (Low-level Security.framework wrapper)
              │
              ├──► ZMacAuthTokenFetch (category)  (Async token refresh with queued callback stacking)
              │
              ├──► ZMacAuthKeyPairUtil             (RSA key-pair generation and decryption)
              │
              └──► ZMacAuthHelpers (category)     (User-Agent, URL transforms, mode init, keychain helpers)
```

---

## File Structure

```
IAM_ZMacAuth/
├── ZMacAuth.h / .m                      — Public facade (entry point for apps)
├── ZMacAuthConstants.h                  — Error code constants
├── ZMacAuthEnums.h                      — ZMacAuthBuildType and notification enums
├── ZMacAuthRequestBlocks.h              — All callback/handler typedef definitions
├── ZMacAuthUser.h / .m                  — ZMacAuthUser model (ZUID, profile, scopes, accountsUrl)
├── ZMacAuthUser+Internal.h              — Internal initializer for ZMacAuthUser
├── ZMacAuthProfileData.h / .m           — Profile model (email, name, displayName, photo)
├── ZMacAuthProfileData+Internal.h       — Internal initializer for ZMacAuthProfileData
├── ZMacAuthLoginViewController.h / .m   — WKWebView-based login UI controller
├── ZMacAuthWebkitViewController.h / .m  — Generic WKWebView presenter (for websession / close account)
├── ZMacAuthNetworkManager.h / .m        — NSURLSession POST/GET wrapper
├── ZMacAuthKeyPairUtil.h / .m           — RSA key-pair generator
├── ZMacAuthNSData+Base64.h / .m         — Base64 category on NSData
├── ZMacAuth_NSData+AES.h / .m           — AES-128 encryption category on NSData
├── ZMacAuthUtilConstants.h              — All URL constants, Keychain keys, shared secret
├── NSView+ZMacAuthView.h / .m           — NSView layout constraint helpers
└── Utils/
    ├── ZMacAuthZIAMUtil.h / .m          — Core singleton (all business logic)
    ├── ZMacAuthTokenFetch.h / .m        — Token refresh logic (category on ZMacAuthZIAMUtil)
    ├── ZMacAuthKeychainUtil.h / .m      — Keychain CRUD (category on ZMacAuthZIAMUtil)
    ├── ZMacAuthKeyChainWrapper.h / .m   — Security.framework keychain abstraction
    ├── ZMacAuthHelpers.h / .m           — Utilities and setup (category on ZMacAuthZIAMUtil)
    ├── ZMacAuthErrorHandler.h / .m      — Error construction helpers
    └── ZMacAuthZIAMUtil.h               — Full interface declaration for the singleton
```

---

## Environments & Build Types

The app passes a `ZMacAuthBuildType` enum at initialization. URLs are set accordingly:

| Enum Value | Description | Accounts Base URL | Contacts URL |
|---|---|---|---|
| `Local_ZMacAuth_Development` (0) | Zoho internal dev — requires ZohoCorp Wi-Fi | `https://accounts.localzoho.com` | `https://contacts.localzoho.com/file/download` |
| `LocalDev_ZMacAuth_Development` (1) | Zoho internal dev server | `https://accounts-dev.localzoho.com` | `https://contacts.localzoho.com/file/download` |
| `Live_ZMacAuth` (2) | Production (IDC) | `https://accounts.zoho.com` | `https://contacts.zoho.com/file/download` |
| `CSEZ_ZMacAuth_Dev` (3) | CSEZ development | `https://accounts.csez.zohocorpin.com` | `https://contacts.csez.zohocorpin.com` |
| `CSEZ_ZMacAuth_MDM` (4) | CSEZ MDM | `https://accounts.csez.zohocorpin.com` | `https://contacts.csez.zohocorpin.com` |
| `PRE_ZMacAuth_Dev` (5) | Pre-production | `https://preaccounts.zoho.com` | `https://precontacts.zoho.com/file/download` |
| `PRE_ZMacAuth_MDM` (6) | Pre-production MDM | `https://preaccounts.zoho.com` | `https://precontacts.zoho.com/file/download` |
| `iAccounts_ZMacAuth_Dev` (7) | iAccounts dev | `https://iaccounts.zoho.com` | — |
| `iAccounts_ZMacAuth_MDM` (8) | iAccounts MDM | `https://iaccounts.zoho.com` | — |
| China mode _(runtime flag)_ | Set via `pointToChinaSetup` | `https://accounts.zoho.com.cn` | `https://contacts.zoho.com.cn/file/download` |

> **Note:** The `accounts-server` field returned in the OAuth redirect callback at runtime overrides the base URL on a per-user basis to correctly handle DCL (Data Center Location) routing.

---

## Initialization

Call once during app launch in `AppDelegate`:

```objc
// Basic
[ZMacAuth initWithClientID:@"YOUR_CLIENT_ID"
                     Scope:@[@"aaaserver.profile.READ",
                              @"zohocontacts.userphoto.READ",
                              @"ZohoMail.messages.ALL"]
                 URLScheme:@"yourapp://"
                 BuildType:Live_ZMacAuth];

// With shared app targets (for app extensions)
[ZMacAuth initWithClientID:@"YOUR_CLIENT_ID"
                     Scope:@[@"aaaserver.profile.READ", @"zohocontacts.userphoto.READ"]
                 URLScheme:@"yourapp://"
           SharedAppTarget:@[@"com.zoho.yourapp.extension"]
                   Service:@"com.zoho.yourapp.keychain"
                 BuildType:Live_ZMacAuth];

// If the app has an App Extension, call after init:
[ZMacAuth setHavingAppExtensionWithAppGroup:@"group.com.zoho.yourapp"];
```

**What init does internally:**
- Stores `ClientID`, `UrlScheme`, `BuildType`, and `Scopes` in the `ZMacAuthZIAMUtil` singleton
- Automatically appends `aaaserver.profile.READ` and `zohocontacts.userphoto.READ` to scopes if not provided
- Scopes are concatenated with `,` (e.g. `aaaserver.profile.READ,zohocontacts.userphoto.READ,ZohoMail.messages.ALL`)
- Sets `BaseUrl` and `ContactsUrl` based on `BuildType`
- Reads the app bundle identifier (or `ZMacAuthKIT_MAIN_APP_BUNDLE_ID` from `Info.plist` for extensions) as `AppName`

---

## Login Flow — Step by Step

### Step 1 — SDK Initialization

See [Initialization](#initialization) above. The app should call `initWithClientID:...` at launch.

---

### Step 2 — RSA Key Pair Generation

When `ZMacAuthLoginViewController` loads its view:

1. A fresh RSA key pair is generated by `ZMacAuthKeyPairUtil`
2. Keys are stored in the macOS Keychain under:
   - Public Key: `com.zoho.publicKey`
   - Private Key: `com.zoho.privateKey`
   - Server Public Key: `com.zoho.serverPublicKey`
3. The public key is exported as a base64 string → stored as `oauthpub` (will be sent to server as `ss_id`)

> A new key pair is generated fresh for every login session. The public key is what allows the IAM server to securely transmit the `client_secret` back to the app without it being visible in the URL in plaintext.

---

### Step 3 — Build the Login URL

**Base URL:** `{BaseUrl}/oauth/v2/mobile/auth`

In addition to the base URL, a `rook_cook` fingerprint token is built:

```
rook_cook = AES-128-ECB-encrypt(
    "{deviceModel}__i__{appName}__i__{currentTimeMillis}__i__{hostname}",
    key = "1234567890123456"
) → base64 → URL-encoded
```

**Full URL query parameters:**

| Parameter | Value | Notes |
|---|---|---|
| `client_id` | App's Client ID | Registered with Zoho IAM |
| `scope` | Comma-separated scopes string | e.g. `aaaserver.profile.READ,ZohoMail.messages.ALL` |
| `redirect_uri` | App's URL scheme | e.g. `yourapp://` |
| `state` | `"Test"` | Hardcoded |
| `response_type` | `"code"` | Standard OAuth 2.0 authorization code flow |
| `access_type` | `"offline"` | Requests a `refresh_token` |
| `newmobilepage` | `"true"` | Uses the modern IAM login page |
| `ss_id` | Base64 RSA public key (URL-encoded) | Used by IAM to encrypt `client_secret` in the redirect |
| `rook_cook` | AES-encrypted device fingerprint (URL-encoded) | Anti-tamper / device correlation signal |

If `UrlParams` is set (via `presentInitialViewControllerInContentView:withCustomParams:`), those are appended verbatim as `&{urlParams}`.

**Example login URL:**
```
https://accounts.zoho.com/oauth/v2/mobile/auth
  ?client_id=1000.XXXX
  &scope=aaaserver.profile.READ%2CZohoMail.messages.ALL
  &redirect_uri=zohomail%3A%2F%2F
  &state=Test
  &response_type=code
  &access_type=offline
  &newmobilepage=true
  &ss_id=MIIBIjAN...%3D%3D
  &rook_cook=abc123...%3D
```

---

### Step 4 — Display the Login WebView

Two render paths exist:

#### Path A — `WKWebView` (default)
- `WKWebViewConfiguration` with `nonPersistentDataStore` (ephemeral/private session — no cookies persist cross-login)
- Embedded inside the app window's content view
- `WKNavigationDelegate` on `ZMacAuthLoginViewController` intercepts URL navigations to detect when the redirect URI scheme is hit

#### Path B — `ASWebAuthenticationSession` (macOS 10.15+)
- Activated by setting `[ZMacAuthZIAMUtil sharedUtil].shoulduseASWebAuthenticationSession = YES`
- Uses the system browser session sheet
- Callback is received directly in the completion handler (no URL scheme interception needed)
- `prefersEphemeralWebBrowserSession` is set based on a flag

---

### Step 5 — Handle the OAuth Redirect

After the user logs in on the IAM page, the server redirects to the app's URL scheme. The redirect query parameters are:

| Parameter | Meaning |
|---|---|
| `code` | Authorization code (short-lived, one-time use) |
| `gt_hash` | Token hash — sent as `rt_hash` in the token exchange |
| `gt_sec` | Client secret — **RSA-encrypted** with the public key that was sent as `ss_id` |
| `accounts-server` | User's actual accounts server (may differ from `BaseUrl` due to DCL routing) |
| `location` | DCL location prefix (e.g., `"us"`, `"eu"`, `"in"`) |
| `error` | Error string if login failed (login is aborted) |

The app decrypts `gt_sec` using the locally stored RSA private key:
```objc
gt_sec = [keygen decryptUsingPrivateKeyWithData:[NSData dataFromBase64String:encrypted_gt_sec]];
```

This decrypted `gt_sec` is the real `client_secret` used for all subsequent token API calls.

---

### Step 6 — Token Exchange (POST `/oauth/v2/token`)

```
POST {accounts-server}/oauth/v2/token
Content-Type: application/x-www-form-urlencoded
User-Agent: ZMacAuth_1.0.4_{AppName}/{version} (OS X {osVersion}; Apple {deviceModel}; ZC_OSX Extension)
```

**Request body parameters:**

| Parameter | Value |
|---|---|
| `grant_type` | `"authorization_code"` |
| `client_id` | App's client ID |
| `client_secret` | Decrypted `gt_sec` (URL percent-encoded) |
| `redirect_uri` | App's URL scheme (e.g. `yourapp://`) |
| `rt_hash` | `gt_hash` value from the OAuth redirect |
| `code` | Authorization code from the OAuth redirect |

**Response fields used:**

| Field | Purpose |
|---|---|
| `access_token` | Short-lived bearer token — used in `Authorization: Zoho-oauthtoken {token}` header |
| `refresh_token` | Long-lived token — stored in keychain, used to silently get new access tokens |
| `expires_in` | Access token lifetime in **milliseconds** |
| `dc_locations` | JSON object — data center location routing info for URL transforms |

> If `dc_locations` is absent from the response, login is **aborted** immediately with a DCL error.

---

### Step 7 — Fetch User Info (GET `/oauth/user/info`)

```
GET {accounts-server}/oauth/user/info
Authorization: Zoho-oauthtoken {access_token}
User-Agent: ZMacAuth_1.0.4_{AppName}/{version} (OS X {osVersion}; Apple {deviceModel}; ZC_OSX Extension)
```

**Response fields used:**

| JSON Key | Stored as |
|---|---|
| `ZUID` | Zoho User ID — numeric long → converted to String |
| `Display_Name` | User's display name |
| `Email` | User's email address |

The `ContactsUrl` may be transformed here based on the `dc_locations` data to ensure the profile photo is fetched from the correct regional data center.

---

### Step 8 — Fetch Profile Photo

```
GET {contacts_url}/file/download
Authorization: Zoho-oauthtoken {access_token}
User-Agent: ZMacAuth_1.0.4_{AppName}/{version} (OS X {osVersion}; Apple {deviceModel}; ZC_OSX Extension)
```

- Returns raw image `NSData`
- Stored in keychain as part of the user details entry
- **Failure is silently ignored** — if photo fetch fails, login still proceeds (photo is stored as `NSNull`)
- Can be disabled entirely by calling `[ZMacAuth donotFetchProfilePhotoDuringSignin]` before presenting the login view

---

### Step 9 — Store Everything in Keychain

Two separate keychain entries are written on each successful login:

**Keychain Entry 1 — Master Dictionary**
- **Key:** `{AppName}_zmacauth`
- **Format:** `NSKeyedArchiver` archived `NSDictionary`, stored as `NSData`
- Serialized with `NSKeyedArchiver`, deserialized with `NSKeyedUnarchiver`

```
{
  "current_user": "{ZUID}",
  "zuids": ["{ZUID}", ...],
  "{AppName}_{ZUID}_zmacauth": {
    "client_secret": "{decrypted gt_sec}",
    "refresh_token": "{refresh_token}",
    "access_token_dictionary": {
      "{scopes_string}": ["{access_token}", "{expiry_timestamp_millis}"]
    },
    "accounts_url": "https://accounts.zoho.com",
    "dcl_location": "us",
    "dcl_meta": NSData (JSON-serialized dc_locations)
  }
}
```

**Keychain Entry 2 — User Details**
- **Key:** `{AppName}_{ZUID}_USERDETAIL`
- **Format:** `NSKeyedArchiver` archived `NSArray`, stored as `NSData`

```
[DisplayName, Email, profileImageData_or_NSNull]
 index[0]     index[1]   index[2]
```

After any write to the master keychain dict, a `NSDistributedNotification` is posted:
- **Notification name:** `com.zoho.zmacauth.keychainupdated`
- Allows app extensions and background targets to invalidate their cached local copy

---

## Token Refresh Flow

Called any time the app needs a valid access token:

```objc
[ZMacAuth getOAuth2Token:^(NSString *accessToken, NSError *error) {
    if (accessToken) {
        // Use token in Authorization header: "Zoho-oauthtoken {accessToken}"
    }
}];
```

**Internal flow:**

1. Read `refresh_token` from keychain for current ZUID
2. Read the `access_token_dictionary` from keychain
3. Check: `currentTimeMillis + 60,000 < stored_expiry_timestamp`
   - **If YES** → return cached `access_token` immediately (no network call)
   - **If NO** → proceed to server refresh

4. **POST `{accounts_url}/oauth/v2/token`:**

| Parameter | Value |
|---|---|
| `grant_type` | `"refresh_token"` |
| `client_id` | App's client ID |
| `client_secret` | Stored client secret (URL percent-encoded) |
| `scope` | Scopes string |
| `refresh_token` | Stored refresh token |

5. On success: store new `[access_token, new_expiry_timestamp]` array in keychain under the scopes key
6. Return `access_token` to all waiting callers

**Queue stacking (concurrency safety):**

All concurrent token refresh requests are queued in a `stackBlocksDictionary` on a serial dispatch queue (`com.zoho.ssokit.tokenfetch`). Only **one** network call is made per ZUID at any time. All callers that arrived while the request was in-flight get the result of that single call.

**WMS variant (`getOAuth2TokenForWMS:`):**

Same flow, but:
- Uses a 7-minute (`420,000 ms`) lead time check instead of 1 minute
- Also returns `expiresMillis` (the remaining validity window in milliseconds) for WebSocket lifecycle management

---

## Logout Flow

```objc
[ZMacAuth revokeAccessToken:^(NSError *error) {
    // Clear your app's signed-in state here
}];
```

**Internal flow:**

1. **POST `{accounts_url}/oauth/v2/token/revoke`**

| Parameter | Value |
|---|---|
| `token` | Stored `refresh_token` for the ZUID |

2. On success: clear all keychain data for the ZUID:
   - Remove `{AppName}_{ZUID}_zmacauth` entry from the master dict
   - Remove `{AppName}_{ZUID}_USERDETAIL` entry
   - Update `current_user` and `zuids` list in master dict
   - If no more users remain, remove the entire `{AppName}_zmacauth` key
3. Post `com.zoho.zmacauth.keychainupdated` distributed notification

---

## All API Endpoints

| Endpoint | Method | Purpose |
|---|---|---|
| `{base}/oauth/v2/mobile/auth` | `GET` (WebView navigation) | Login page presented in WebView/ASWebAuth |
| `{accounts}/oauth/v2/token` | `POST` | Token exchange — auth code → access + refresh tokens |
| `{accounts}/oauth/v2/token` | `POST` | Token refresh — refresh token → new access token |
| `{accounts}/oauth/v2/token/revoke` | `POST` | Logout — revoke refresh token |
| `{accounts}/oauth/user/info` | `GET` | Fetch ZUID, Email, Display_Name |
| `{contacts}/file/download` | `GET` | Fetch user profile photo (raw image data) |
| `{accounts}/oauth/v2/token/internal/getextrascopes` | `POST` | Scope Enhancement flow |
| `{accounts}/oauth/v2/token/addscope` | `POST` | Add scope |
| `{accounts}/oauth/v2/token/internal/authtooauth` | `POST` | Convert Auth Token → OAuth Token |
| `{accounts}/oauth/v2/mobile/unconfirmed` | `GET` (WebView) | Email confirmation for unconfirmed accounts |
| `{accounts}/ssokit/addemail` | — | Add secondary email ID |
| `{accounts}/ssokit/closeaccount` | — | Close/delete Zoho account |
| `{accounts}/oauth/inactivetoken/handshakeId` | — | Generate handshake ID for inactive token activation |
| `{accounts}/oauth/v2/internal/inactive/token` | — | Activate an inactive refresh token |
| `{accounts}/oauth/mobile/verify` | — | Device verification |
| `{accounts}/oauth/mobile/verify/prompt` | `GET` (WebView) | Device verification UI page |
| `{accounts}/account/v1/websession` | — | Get web session token |
| `{accounts}/api/v1/ssokit/token` | — | SSO Kit token endpoint |
| `{accounts}/oauth/v2/mobile/internal/getremoteloginkey` | — | Client portal remote login key |
| `{accounts}/accounts/register?servicename=aaaserver` | `GET` (WebView) | Sign-up page |

---

## Keychain Storage Schema

### Master Dictionary

**Keychain Key:** `{AppName}_zmacauth`

```
Root NSDictionary (NSKeyedArchiver NSData)
│
├── "current_user"  →  NSString ZUID           // Active user
├── "zuids"         →  NSMutableArray<NSString> // All signed-in ZUIDs
│
└── "{AppName}_{ZUID}_zmacauth"  →  NSDictionary  // Per-user auth data
        ├── "client_secret"           → NSString   // RSA-decrypted client secret
        ├── "refresh_token"           → NSString   // Long-lived refresh token
        ├── "access_token_dictionary" → NSDictionary
        │       └── "{scopes_string}" → NSArray
        │               [0] access_token     NSString
        │               [1] expiry_millis    NSString (long as string)
        ├── "accounts_url"            → NSString   // e.g. "https://accounts.zoho.com"
        ├── "dcl_location"            → NSString   // e.g. "us", "eu", "in"
        └── "dcl_meta"                → NSData     // JSON-serialized dc_locations dict
```

### User Details

**Keychain Key:** `{AppName}_{ZUID}_USERDETAIL`

```
NSArray (NSKeyedArchiver NSData)
    [0]  NSString   Display_Name
    [1]  NSString   Email
    [2]  NSData     Profile photo image data  (or NSNull if no photo)
```

### Keychain Access

- **Service:** `AppName` (or custom `KeychainService` if set via `initWithClientID:SharedAppTarget:Service:BuildType:`)
- **Access Group:** `ExtensionAppGroup` (if set — enables sharing with app extensions)
- All underlying reads/writes go through `ZMacAuthKeyChainWrapper` which uses the `Security.framework` (`SecItemAdd`, `SecItemCopyMatching`, `SecItemUpdate`, `SecItemDelete`)

---

## Data Models

### `ZMacAuthUser`

Represents a single signed-in Zoho account.

| Property | Type | Description |
|---|---|---|
| `userZUID` | `NSString` | Zoho User ID |
| `profile` | `ZMacAuthProfileData` | User's basic profile info |
| `accessibleScopes` | `NSArray<NSString *>` | Array of OAuth scope strings |
| `accountsUrl` | `NSString` | User's accounts server URL |
| `location` | `NSString` | DCL location string |

Conforms to `NSCoding` (can be archived/unarchived).

### `ZMacAuthProfileData`

Represents the profile information of a `ZMacAuthUser`.

| Property | Type | Description |
|---|---|---|
| `email` | `NSString` | User's email address |
| `name` | `NSString` | User's full name |
| `displayName` | `NSString` | User's display name |
| `hasImage` | `BOOL` | Whether a profile photo is available |
| `profileImageData` | `NSData` | Raw image bytes of the profile photo |

Conforms to `NSCoding`.

---

## Multi-Account Support

The SDK is designed to support multiple simultaneous signed-in Zoho accounts:

```objc
// Get all signed-in users
NSMutableArray<ZMacAuthUser *> *users = [ZMacAuth getAllUsers];

// Get all ZUIDs
NSMutableArray<NSString *> *zuids = [ZMacAuth getAllUsersZUIDs];

// Get a specific user by ZUID
ZMacAuthUser *user = [ZMacAuth getZMacAuthUserHavingZUID:@"123456789"];

// Switch active account
[ZMacAuth setCurrentUserHavingZUID:@"987654321"];

// Get current active user
ZMacAuthUser *current = [ZMacAuth getCurrentUser];
NSString *currentZUID = [ZMacAuth currentZUID];

// Get token for a specific account (not just current)
[ZMacAuth getOAuth2TokenForZUID:@"123456789" token:^(NSString *token, NSError *error) {
    // ...
}];
```

Each ZUID maintains completely isolated keychain data:
- Separate `client_secret`, `refresh_token`, `access_token_dictionary`
- Separate `accounts_url` (for DCL routing)
- Separate `dcl_location` and `dcl_meta`
- Separate user profile details (`USERDETAIL` keychain entry)

---

## Error Codes

All errors use domain `com.zoho.zmacauth`.

| Constant | Code | Meaning |
|---|---|---|
| `k_ZMacAuthNoAccessToken` | 302 | No access token in keychain for this ZUID |
| `k_ZMacAuthTokenFetchError` | 201 | Token fetch failed (server error) |
| `k_ZMacAuthTokenFetchNil` | 202 | Token response was nil |
| `k_ZMacAuthTokenFetchNothingReceived` | 204 | Token fetch — nothing received |
| `k_ZMacAuthTokenFetchGeneralError` | 205 | Token fetch — general error |
| `k_ZMacAuthRefreshTokenFetchError` | 901 | Refresh token fetch server error |
| `k_ZMacAuthRefreshTokenFetchNil` | 902 | Refresh token response was nil |
| `k_ZMacAuthRefreshTokenFetchNothingReceived` | 904 | Refresh token — nothing received |
| `k_ZMacAuthOAuthServerError` | 905 | OAuth server error during redirect |
| `k_ZMacAuthScopeNotFound` | 906 | No access token for the given scopes |
| `k_ZMacAuthScopeEnhancementFetchError` | 1001 | Scope enhancement server error |
| `k_ZMacAuthScopeEnhancementFetchNil` | 1002 | Scope enhancement response was nil |
| `k_ZMacAuthScopeEnhancementFetchNothingReceived` | 1003 | Scope enhancement — nothing received |
| `k_ZMacAuthScopeEnhancementServerError` | 1004 | Scope enhancement server error during redirect |
| `k_ZMacAuthScopeEnhancementDismissedError` | 1005 | Scope enhancement page dismissed |
| `k_ZMacAuthScopeEnhancementAlreadyDone` | 1006 | Scope already enhanced |
| `k_ZMacAuthAuthToOAuthFetchError` | 2001 | AuthToken → OAuth token fetch error |
| `k_ZMacAuthAuthToOAuthFetchNil` | 2002 | AuthToken → OAuth response was nil |
| `k_ZMacAuthAuthToOAuthNothingReceived` | 2003 | AuthToken → OAuth — nothing received |
| `k_ZMacAuthAuthToOAuthServerError` | 2004 | AuthToken → OAuth server error |
| `k_ZMacAuthAddSecondaryEmailServerError` | 2501 | Add secondary email server error |
| `k_ZMacAuthAddSecondaryEmailFetchNil` | 2502 | Add secondary email response was nil |
| `k_ZMacAuthAddSecondaryEmailErrorNothingReceived` | 2503 | Add secondary email — nothing received |
| `k_SSOUserInfoFetchError` | 601 | User info fetch failed |
| `k_SSOUserInfoFetchNil` | 602 | User info response was nil |
| `k_SSOUserInfoFetchNothingReceived` | 604 | User info — nothing received |
| `k_SSOUserPhotoFetchError` | 701 | Profile photo fetch failed |
| `k_SSOUserPhotoFetchNothingReceived` | 704 | Profile photo — nothing received |
| `k_ZMacAuthRevokeTokenError` | 801 | Revoke token failed |
| `k_ZMacAuthRevokeTokenResultNil` | 802 | Revoke response was nil |
| `k_ZMacAuthRevokeTokenNothingReceived` | 804 | Revoke — nothing received |
| `k_ZMacAuthGenericError` | 901 | Generic network call failure |

**Special error scenarios:**
- `invalid_mobile_code` server error during token refresh → auto-logout for that ZUID (keychain cleared)
- `unconfirmed_user` server error during token refresh → WebView presented for email confirmation

---

## Security Notes

| Aspect | Implementation | Notes |
|---|---|---|
| Client Secret Transmission | RSA-encrypted in OAuth redirect URL | Public key generated fresh per login, sent as `ss_id`. Server encrypts `client_secret`; only the app's private key can decrypt it. |
| Client Secret Storage | macOS Keychain | Never stored in `NSUserDefaults` or on disk in plaintext |
| Access / Refresh Tokens | macOS Keychain | Archived with `NSKeyedArchiver`; only accessible by the app (and extensions in the same App Group) |
| Profile Photo | macOS Keychain | Stored alongside user details as raw `NSData` |
| Device Fingerprint (`rook_cook`) | AES-128 with hardcoded key `"1234567890123456"` | ⚠️ **Weak point** — the key is in the binary. Used only as an anti-tamper / device correlation signal, not as a cryptographic secret. The IAM server uses it for anomaly detection, not for access control. |
| Token Refresh Race Conditions | Serial dispatch queue | Prevents multiple concurrent refreshes for the same ZUID; all callers queue and receive the single result |
| WebView Session Isolation | `WKWebsiteDataStore.nonPersistentDataStore` | Each login WebView uses an ephemeral session — no cookies or credentials persist after the view is dismissed |
| Distributed Notification | `com.zoho.zmacauth.keychainupdated` | Posted after every keychain write so app extensions can invalidate their local cache |

---

## Public API Reference

### Initialization

```objc
+ (void)initWithClientID:(NSString *)clientID
                   Scope:(NSArray *)scopearray
               URLScheme:(NSString *)URLScheme
               BuildType:(ZMacAuthBuildType)buildType;

+ (void)initWithClientID:(NSString *)clientID
                   Scope:(NSArray *)scopearray
               URLScheme:(NSString *)URLScheme
         SharedAppTarget:(NSArray *)sharedTargetPaths
                 Service:(NSString *)service
               BuildType:(ZMacAuthBuildType)buildType;

+ (void)setHavingAppExtensionWithAppGroup:(NSString *)appGroup;
```

### Login / Sign-up

```objc
// Present login page in a content view
+ (void)presentInitialViewControllerInContentView:(NSView *)contentView
                                      signinBlock:(ZMacAuthKitSigninHandler)signinBlock;

// Present login page with custom URL params
+ (void)presentInitialViewControllerInContentView:(NSView *)contentView
                                 withCustomParams:(NSString *)urlParams
                                   signinHandler:(ZMacAuthKitSigninHandler)signinBlock;

// Present sign-up page
+ (void)presentSignUpViewControllerInContentView:(NSView *)contentView
                                     signinBlock:(ZMacAuthKitSigninHandler)signinBlock;

// Present custom sign-up URL
+ (void)presentSignUpViewControllerInContentView:(NSView *)contentView
                                       havingURL:(NSString *)signupUrl
                                   signinHandler:(ZMacAuthKitSigninHandler)signinBlock;
```

### Token Retrieval

```objc
// Get access token for current user (auto-refreshes if expired)
+ (void)getOAuth2Token:(ZMacAuthKitAccessTokenHandler)tokenBlock;

// Get access token for a specific ZUID
+ (void)getOAuth2TokenForZUID:(NSString *)zuid
                        token:(ZMacAuthKitAccessTokenHandler)tokenBlock;

// Get access token with expiry — for WebSocket management
+ (void)getOAuth2TokenForWMS:(ZMacAuthKitWMSAccessTokenHandler)tokenBlock;
+ (void)getOAuth2TokenForWmsHavingZUID:(NSString *)zuid
                                 token:(ZMacAuthKitWMSAccessTokenHandler)tokenBlock;

// Convert Auth Token to OAuth Token
+ (void)getOAuth2TokenUsingAuthToken:(NSString *)authToken
                               forApp:(NSString *)appName
                    havingAccountsURL:(NSString *)accountsBaseURL
                   authToOAuthHandler:(ZMacAuthKitAuthToOAuthHandler)authToOAuthBlock;
```

### User Management

```objc
// Get the current active user object
+ (ZMacAuthUser *)getCurrentUser;

// Get ZUID of the current active user
+ (NSString *)currentZUID;

// Set the active user by ZUID (multi-account switch)
+ (void)setCurrentUserHavingZUID:(NSString *)userZuid;

// Get all signed-in users
+ (NSMutableArray *)getAllUsers;
+ (NSMutableArray *)getAllUsersZUIDs;

// Get a specific user by ZUID
+ (ZMacAuthUser *)getZMacAuthUserHavingZUID:(NSString *)zuidText;

// Check if any user is signed in
+ (BOOL)isUserSignedIn;
```

### Logout

```objc
// Revoke token and clear keychain for current user
+ (void)revokeAccessToken:(ZMacAuthKitRevokeAccessTokenHandler)revoke;

// Revoke token for a specific ZUID
+ (void)revokeAccessTokenForZUID:(NSString *)zuid
                           block:(ZMacAuthKitRevokeAccessTokenHandler)revoke;

// Clear keychain on first app launch (before any user is signed in)
+ (void)clearSSODetailsForFirstLaunch;
```

### DCL / URL Transforms

```objc
// Transform a URL to the correct regional data center URL for current user
+ (NSString *)getTransformedURLStringForURL:(NSString *)url;
+ (NSString *)getTransformedURLStringForURL:(NSString *)url
                                       zuid:(NSString *)zuidText;

// Get DCL info dictionary
+ (NSDictionary *)getDCLInfoForCurrentUser;
+ (NSDictionary *)getDCLInfoForZuid:(NSString *)zuidText;
```

### Scope Enhancement

```objc
+ (void)enhanceScopesInContentView:(NSView *)contentView
                     enhanceHandler:(ZMacAuthKitScopeEnhancementHandler)enhanceHandler;
+ (void)enhanceScopesInContentView:(NSView *)contentView
                     enhanceHandler:(ZMacAuthKitScopeEnhancementHandler)enhanceHandler
                               zuid:(NSString *)zuidText;
```

### Other

```objc
// Handle URL scheme callback (call from AppDelegate openURL:)
+ (BOOL)handleURL:(NSURL *)url
sourceApplication:(NSString *)sourceApplication
       annotation:(id)annotation;

// China region support
+ (void)pointToChinaSetup;
+ (void)cancelPointToChinaSetup;

// Disable profile photo fetch during sign-in (performance optimization)
+ (void)donotFetchProfilePhotoDuringSignin;

// Disable sending scopes param in token refresh
+ (void)donotSendScopesParam;

// Invalid OAuth logout check (call when server returns invalid_oauthtoken)
+ (void)checkAndLogoutUserDuringInvalidOAuth:(ZMacAuthKitInvalidOAuthLogoutHandler)logoutHandler;
+ (void)checkAndLogoutUserDuringInvalidOAuth:(ZMacAuthKitInvalidOAuthLogoutHandler)logoutHandler
                                        zuid:(NSString *)zuidText;

// Progress indicator callbacks
+ (void)startPreloginProgress:(void (^)(void))callbackBlock;
+ (void)endPreloginProgress:(void (^)(void))callbackBlock;
+ (void)startProgress:(void (^)(void))callbackBlock;
+ (void)endProgress:(void (^)(void))callbackBlock;
```

---

## Callback / Handler Type Definitions

```objc
// Sign-in result
typedef void (^ZMacAuthKitSigninHandler)(NSString *accessToken, NSError *error);

// Access token retrieval
typedef void (^ZMacAuthKitAccessTokenHandler)(NSString *accessToken, NSError *error);

// Access token with expiry (WMS)
typedef void (^ZMacAuthKitWMSAccessTokenHandler)(NSString *accessToken, long expiresMillis, NSError *error);

// Logout / revoke result
typedef void (^ZMacAuthKitRevokeAccessTokenHandler)(NSError *error);

// Scope enhancement result
typedef void (^ZMacAuthKitScopeEnhancementHandler)(NSString *accessToken, NSError *error);

// Auth Token → OAuth Token result
typedef void (^ZMacAuthKitAuthToOAuthHandler)(NSString *accessToken, NSError *error);

// Invalid OAuth logout check
typedef void (^ZMacAuthKitInvalidOAuthLogoutHandler)(BOOL shouldLogoutUser);

// Add secondary email result
typedef void (^ZMacAuthAddEmailHandler)(NSError *error);
```

---

*Documentation generated from source code analysis of `AppXMacAuth` pod version `1.0.4`.*
