---
goal: Replace env-file client credentials flow with keychain-stored app credentials and hardcoded defaults
version: "1.0"
date_created: 2026-04-01
owner: zapi-cli
status: 'Planned'
tags: [feature, auth, config, keychain, credentials]
---

# Introduction

![Status: Planned](https://img.shields.io/badge/status-planned-blue)

Replace the current `ZOHO_CLIENT_ID` / `ZOHO_CLIENT_SECRET` environment-variable flow with a
keychain-backed credentials model.

**Current flow (to be removed):**
- User creates a `.env` file with `ZOHO_CLIENT_ID=...` and `ZOHO_CLIENT_SECRET=...`
- User runs `zapi-cli config set env-file <path>` to register the file
- CLI loads env vars at startup and reads them in `AccountLoginCommand`

**New flow:**
- Binary ships with dummy build-time constants (`DefaultClientId`, `DefaultClientSecret`)
- User runs `zapi-cli config set client-id <ID>` and `zapi-cli config set client-secret <SECRET>` to store real credentials
- CLI stores each value in the OS keychain (`macOS Keychain`, `Windows Credential Manager`, `Linux Secret Service`)
- At every login attempt the CLI reads from keychain; if only dummy defaults are present it aborts with a clear error

---

## 1. Requirements & Constraints

- **REQ-001**: `ZOHO_CLIENT_ID` and `ZOHO_CLIENT_SECRET` must **no longer** be read from environment variables in `AccountLoginCommand`.
- **REQ-002**: A `src/ZapiCli.Core/Auth/AppClientDefaults.cs` constants file must hold the default (dummy) `ClientId` and `ClientSecret` values compiled into the binary.
- **REQ-003**: Two new sub-commands must be added under `config set`:
  - `zapi-cli config set client-id <CLIENT_ID>`
  - `zapi-cli config set client-secret <CLIENT_SECRET>`
- **REQ-004**: Each config command stores the value in the OS keychain using the existing `IKeychainProvider` abstraction, under its own dedicated key.
- **REQ-005**: At login time the CLI queries the keychain for both values.  
  - If both are present and non-default → use them.
  - If either is absent or still matches the dummy defaults → throw `ZapiCliException` with `APP_CREDENTIALS_NOT_CONFIGURED`.
- **REQ-006**: The error message must clearly instruct the user how to fix the issue:
  > No credentials found to perform login. Update client details using:
  >   zapi-cli config set client-id \<CLIENT_ID\>
  >   zapi-cli config set client-secret \<CLIENT_SECRET\>
- **REQ-007**: A new `IAppCredentialsProvider` interface and `AppCredentialsProvider` implementation must encapsulate credential resolution so `AccountLoginCommand` has no keychain knowledge.
- **REQ-008**: The new credential provider must be registered in `DependencyInjectionRegistrar`.
- **SEC-001**: Dummy default values must never be accepted silently — the guard in `AppCredentialsProvider` must compare against `AppClientDefaults.ClientId` and `AppClientDefaults.ClientSecret` and reject if equal.
- **SEC-002**: Credentials are stored in the OS keychain (existing `IKeychainProvider`) — never in `cli-settings.json` or any plain-text file.
- **CON-001**: `config set env-file` command remains in place (other env vars may still be needed); only its client credential role is removed.
- **CON-002**: `MobileLoginAsync` / `ExchangeAndFinalizeAsync` signatures in `AccountService` are unchanged — only the **call site** in `AccountLoginCommand` changes.
- **CON-003**: All existing `account login` tests must continue to pass; new unit tests are added for `AppCredentialsProvider`.

---

## 2. Keychain Key Design

| Value | Keychain Key |
|-------|-------------|
| Client ID | `zapi-cli:config:client-id` |
| Client Secret | `zapi-cli:config:client-secret` |

These keys are constants in `AppClientDefaults.cs` (same file, different section).

---

## 3. Implementation Steps

### Phase 1 — `AppClientDefaults.cs` — build-time constants

> **GOAL-001**: Introduce the constants file that holds dummy defaults and keychain key names.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | Create `src/ZapiCli.Core/Auth/AppClientDefaults.cs`. Define `internal static class AppClientDefaults` with three `const string` members: `ClientId` (dummy), `ClientSecret` (dummy), `KeychainClientIdKey` (`"zapi-cli:config:client-id"`), `KeychainClientSecretKey` (`"zapi-cli:config:client-secret"`). | | |

```csharp
// src/ZapiCli.Core/Auth/AppClientDefaults.cs
namespace ZapiCli.Core.Auth;

/// <summary>
/// Build-time default (dummy) OAuth client credentials.
/// Real credentials are stored in the OS keychain via <c>config set client-id/client-secret</c>.
/// </summary>
internal static class AppClientDefaults
{
    /// <summary>Dummy client ID shipped in the binary. Never used for real logins.</summary>
    public const string ClientId = "DUMMY_CLIENT_ID";

    /// <summary>Dummy client secret shipped in the binary. Never used for real logins.</summary>
    public const string ClientSecret = "DUMMY_CLIENT_SECRET";

    /// <summary>Keychain key under which the user-configured client ID is stored.</summary>
    public const string KeychainClientIdKey = "zapi-cli:config:client-id";

    /// <summary>Keychain key under which the user-configured client secret is stored.</summary>
    public const string KeychainClientSecretKey = "zapi-cli:config:client-secret";
}
```

---

### Phase 2 — `IAppCredentialsProvider` — interface

> **GOAL-002**: Define the public contract for credential resolution.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-002 | Create `src/ZapiCli.Core/Auth/IAppCredentialsProvider.cs`. Single async method `GetCredentialsAsync(CancellationToken)` returning `(string ClientId, string ClientSecret)`. Throws `ZapiCliException` with `APP_CREDENTIALS_NOT_CONFIGURED` when credentials are unavailable. | | |

```csharp
// src/ZapiCli.Core/Auth/IAppCredentialsProvider.cs
namespace ZapiCli.Core.Auth;

/// <summary>
/// Resolves the OAuth application client ID and client secret for use during login.
/// Reads from OS keychain; falls back to dummy defaults which then produce an error.
/// </summary>
public interface IAppCredentialsProvider
{
    /// <summary>
    /// Returns the effective <c>(ClientId, ClientSecret)</c> pair.
    /// </summary>
    /// <exception cref="ZapiCliException">
    /// Thrown with <see cref="ErrorCodes.APP_CREDENTIALS_NOT_CONFIGURED"/> when no real
    /// credentials have been stored (keychain is empty or matches dummy defaults).
    /// </exception>
    Task<(string ClientId, string ClientSecret)> GetCredentialsAsync(CancellationToken ct = default);
}
```

---

### Phase 3 — `AppCredentialsProvider` — implementation

> **GOAL-003**: Implement credential resolution: keychain → dummy-default guard.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-003 | Create `src/ZapiCli.Core/Auth/AppCredentialsProvider.cs`. Constructor injects `IKeychainProvider`. `GetCredentialsAsync` reads both keychain keys. If either is null/empty, falls back to `AppClientDefaults.ClientId/Secret`. Compares final values against dummy defaults; if match, throws `ZapiCliException` with `APP_CREDENTIALS_NOT_CONFIGURED` and the instructional error message. | | |

```csharp
// src/ZapiCli.Core/Auth/AppCredentialsProvider.cs
using ZapiCli.Keychain;

namespace ZapiCli.Core.Auth;

public sealed class AppCredentialsProvider : IAppCredentialsProvider
{
    private readonly IKeychainProvider _keychain;

    public AppCredentialsProvider(IKeychainProvider keychain)
        => _keychain = keychain;

    public async Task<(string ClientId, string ClientSecret)> GetCredentialsAsync(
        CancellationToken ct = default)
    {
        var clientId = await _keychain.GetAsync(AppClientDefaults.KeychainClientIdKey, ct)
                           .ConfigureAwait(false)
                       ?? AppClientDefaults.ClientId;

        var clientSecret = await _keychain.GetAsync(AppClientDefaults.KeychainClientSecretKey, ct)
                               .ConfigureAwait(false)
                           ?? AppClientDefaults.ClientSecret;

        if (clientId == AppClientDefaults.ClientId || clientSecret == AppClientDefaults.ClientSecret)
            throw new ZapiCliException(
                "No credentials found to perform login. Update client details using:\n" +
                "  zapi-cli config set client-id <CLIENT_ID>\n" +
                "  zapi-cli config set client-secret <CLIENT_SECRET>",
                ErrorCodes.APP_CREDENTIALS_NOT_CONFIGURED,
                exitCode: 1);

        return (clientId, clientSecret);
    }
}
```

---

### Phase 4 — `ErrorCodes` — new error code

> **GOAL-004**: Register the new error code constant.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-004 | Open `src/ZapiCli.Core/ErrorCodes.cs`. Add `public const string APP_CREDENTIALS_NOT_CONFIGURED = nameof(APP_CREDENTIALS_NOT_CONFIGURED);` alongside the existing `ENV_FILE_NOT_CONFIGURED` constant. | | |

```csharp
// In ErrorCodes.cs — add alongside ENV_FILE_NOT_CONFIGURED
public const string APP_CREDENTIALS_NOT_CONFIGURED = nameof(APP_CREDENTIALS_NOT_CONFIGURED);
```

---

### Phase 5 — `ConfigCommands` — new `config set client-id` and `config set client-secret` sub-commands

> **GOAL-005**: Expose the two new config sub-commands so users can store real credentials.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-005 | In `src/ZapiCli/Commands/ConfigCommands.cs`, add `ConfigSetClientIdSettings` + `ConfigSetClientIdCommand` and `ConfigSetClientSecretSettings` + `ConfigSetClientSecretCommand`. Each command: accepts a single positional `<VALUE>` argument, validates it is non-empty, calls `IKeychainProvider.SetAsync(key, value, ct)`, and returns `{ status: "ok", data: { key } }`. | | |

```csharp
// ─── config set client-id ─────────────────────────────────────────────────
public sealed class ConfigSetClientIdSettings : CommandSettings
{
    [CommandArgument(0, "<CLIENT_ID>")]
    public required string ClientId { get; init; }

    public override ValidationResult Validate()
        => string.IsNullOrWhiteSpace(ClientId)
            ? ValidationResult.Error("CLIENT_ID must not be empty.")
            : ValidationResult.Success();
}

public sealed class ConfigSetClientIdCommand : AsyncCommand<ConfigSetClientIdSettings>
{
    private readonly IKeychainProvider _keychain;
    private readonly IOutputWriter _output;

    public ConfigSetClientIdCommand(IKeychainProvider keychain, IOutputWriter output)
    {
        _keychain = keychain;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ConfigSetClientIdSettings settings)
    {
        await _keychain.SetAsync(AppClientDefaults.KeychainClientIdKey, settings.ClientId, default)
                       .ConfigureAwait(false);
        _output.WriteJson(new { status = "ok", data = new { key = AppClientDefaults.KeychainClientIdKey } });
        return 0;
    }
}

// ─── config set client-secret ─────────────────────────────────────────────
// (mirrors ConfigSetClientIdCommand; replaces KeychainClientSecretKey)
```

---

### Phase 6 — `Program.cs` — register new sub-commands

> **GOAL-006**: Wire up the two new sub-commands in the Spectre.Console command tree.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-006 | In `src/ZapiCli/Program.cs` (or wherever `config set` sub-commands are added to the `CommandApp`), register `ConfigSetClientIdCommand` and `ConfigSetClientSecretCommand` under the `config set` branch alongside the existing `env-file` and `scope-file` commands. | | |

---

### Phase 7 — `DependencyInjectionRegistrar` — register `AppCredentialsProvider`

> **GOAL-007**: Make `IAppCredentialsProvider` available for injection.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-007 | In `src/ZapiCli/DependencyInjectionRegistrar.cs`, add `services.AddSingleton<IAppCredentialsProvider, AppCredentialsProvider>();`. `IKeychainProvider` is already registered. | | |

---

### Phase 8 — `AccountLoginCommand` — remove env vars, inject `IAppCredentialsProvider`

> **GOAL-008**: Swap the env-var credential read for the new provider.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-008 | In `AccountLoginCommand`: add `IAppCredentialsProvider _appCredentials` to the constructor and stored field. Remove the `Environment.GetEnvironmentVariable("ZOHO_CLIENT_ID")` block and its guard. Remove the `Environment.GetEnvironmentVariable("ZOHO_CLIENT_SECRET")` line. Replace both with a single `var (clientId, clientSecret) = await _appCredentials.GetCredentialsAsync(default).ConfigureAwait(false);` call near the start of `ExecuteAsync`. The rest of the method is unchanged. | | |

**Before:**
```csharp
var clientId = Environment.GetEnvironmentVariable("ZOHO_CLIENT_ID");
if (string.IsNullOrWhiteSpace(clientId))
    throw new ZapiCliException(
        "ZOHO_CLIENT_ID is not set. Configure an env-file via 'zapi-cli config set env-file <path>'.",
        ErrorCodes.ENV_FILE_NOT_CONFIGURED,
        exitCode: 1);
// ... scope loading ...
var clientSecret = Environment.GetEnvironmentVariable("ZOHO_CLIENT_SECRET");
```

**After:**
```csharp
var (clientId, clientSecret) = await _appCredentials.GetCredentialsAsync(default)
    .ConfigureAwait(false);
```

---

### Phase 9 — Unit tests for `AppCredentialsProvider`

> **GOAL-009**: Cover the four key scenarios.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-009 | Create `tests/ZapiCli.Tests/Auth/AppCredentialsProviderTests.cs`. Add tests for: (a) keychain has both values → returns them; (b) keychain missing client-id → throws `APP_CREDENTIALS_NOT_CONFIGURED`; (c) keychain missing client-secret → throws `APP_CREDENTIALS_NOT_CONFIGURED`; (d) keychain returns dummy-default value → throws `APP_CREDENTIALS_NOT_CONFIGURED`. Use `NSubstitute` mock for `IKeychainProvider`. | | |

```csharp
// Scenario (a): valid credentials returned
[Fact]
public async Task GetCredentialsAsync_ValidKeychain_ReturnsCredentials()
{
    var keychain = Substitute.For<IKeychainProvider>();
    keychain.GetAsync(AppClientDefaults.KeychainClientIdKey, default)
            .Returns("real-client-id");
    keychain.GetAsync(AppClientDefaults.KeychainClientSecretKey, default)
            .Returns("real-client-secret");

    var sut = new AppCredentialsProvider(keychain);
    var (id, secret) = await sut.GetCredentialsAsync();

    Assert.Equal("real-client-id", id);
    Assert.Equal("real-client-secret", secret);
}

// Scenario (b): keychain missing client-id → error
[Fact]
public async Task GetCredentialsAsync_MissingClientId_Throws()
{
    var keychain = Substitute.For<IKeychainProvider>();
    keychain.GetAsync(AppClientDefaults.KeychainClientIdKey, default)
            .Returns((string?)null);
    keychain.GetAsync(AppClientDefaults.KeychainClientSecretKey, default)
            .Returns("real-client-secret");

    var sut = new AppCredentialsProvider(keychain);
    var ex = await Assert.ThrowsAsync<ZapiCliException>(() => sut.GetCredentialsAsync());
    Assert.Equal(ErrorCodes.APP_CREDENTIALS_NOT_CONFIGURED, ex.ErrorCode);
}

// Scenarios (c) and (d) follow the same pattern.
```

---

## 4. File Change Summary

| File | Action |
|------|--------|
| `src/ZapiCli.Core/Auth/AppClientDefaults.cs` | **Create** — dummy constants + keychain key names |
| `src/ZapiCli.Core/Auth/IAppCredentialsProvider.cs` | **Create** — interface |
| `src/ZapiCli.Core/Auth/AppCredentialsProvider.cs` | **Create** — keychain-reading implementation |
| `src/ZapiCli.Core/ErrorCodes.cs` | **Modify** — add `APP_CREDENTIALS_NOT_CONFIGURED` |
| `src/ZapiCli/Commands/ConfigCommands.cs` | **Modify** — add `client-id` and `client-secret` sub-commands |
| `src/ZapiCli/Program.cs` | **Modify** — register two new sub-commands in command tree |
| `src/ZapiCli/DependencyInjectionRegistrar.cs` | **Modify** — register `IAppCredentialsProvider` → `AppCredentialsProvider` |
| `src/ZapiCli/Commands/AccountCommands.cs` | **Modify** — remove env var reads; inject + call `IAppCredentialsProvider` |
| `tests/ZapiCli.Tests/Auth/AppCredentialsProviderTests.cs` | **Create** — 4 unit tests |

---

## 5. Command Reference (after this feature)

```
zapi-cli config set client-id <CLIENT_ID>
    Stores the Zoho OAuth client ID in the OS keychain.
    Required before the first account login.

zapi-cli config set client-secret <CLIENT_SECRET>
    Stores the Zoho OAuth client secret in the OS keychain.
    Required before the first account login.
```

**Login error when credentials not configured:**
```
Error: No credentials found to perform login. Update client details using:
  zapi-cli config set client-id <CLIENT_ID>
  zapi-cli config set client-secret <CLIENT_SECRET>
```

---

## 6. Out of Scope

- `config set env-file` is **not removed** — it may still be needed for other env vars.
- Fetching the default `ClientId`/`ClientSecret` from any external service or file is out of scope — they remain hardcoded dummy values until replaced by a future ADR.
- No migration path for users who previously used env vars — this is a breaking change in behavior for self-hosted usage, which is acceptable at this stage.
