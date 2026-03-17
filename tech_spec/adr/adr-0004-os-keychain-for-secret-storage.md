---
title: "ADR-0004: OS Keychain for Secret Storage with Encrypted File Fallback"
status: "Proposed"
date: "2026-03-17"
authors: "zapi-cli Engineering"
tags: ["architecture", "decision", "security", "secrets", "keychain"]
supersedes: ""
superseded_by: ""
---

## Status

**Proposed**

## Context

zapi-cli stores Zoho authentication tokens (PAT, and OAuth access + refresh tokens in v2) on behalf of configured accounts. These tokens grant full access to the user's Zoho products and must be stored securely.

Storage options evaluated:

1. **OS keychain** — platform-managed secure credential storage (macOS Keychain, Windows Credential Manager, Linux Secret Service via libsecret). Protected by OS access control; tokens are not stored on disk in plaintext.
2. **Plaintext config file** — store tokens alongside account config in `accounts.json`.
3. **Encrypted file** — AES-256 encrypted file on disk, key derived from machine entropy.
4. **Environment variables** — pass tokens at invocation time.

The primary constraints are:
- Tokens must **never appear in plaintext on disk** — `accounts.json` is a human-readable JSON file likely to appear in backup snapshots, synced file paths, or version control by accident.
- The solution must be **self-contained** — no external secret manager (e.g., 1Password CLI, Vault) dependency.
- The keychain access must work without a native addon build step — ruling out NuGet packages that bundle platform native libraries with complex build requirements.
- The implementation must cover macOS (Security.framework), Windows (Credential Manager), and Linux (libsecret/Secret Service) from a single C# codebase.
- A fallback must exist for headless Linux environments (CI runners, containers) where no Secret Service daemon is available.

## Decision

**Use the OS keychain as the primary secrets backend**, implemented via direct P/Invoke to platform-native APIs — no NuGet keychain package dependency. Provide **AES-256-GCM encrypted file** as the fallback for environments where the OS keychain is unavailable.

Platform implementations:

| Platform | Class | Backend API |
|----------|-------|-------------|
| macOS | `MacOsKeychainProvider` | `SecKeychainAddGenericPassword` (Security.framework) |
| Windows | `WindowsKeychainProvider` | `CredWrite` / `CredRead` (advapi32.dll) |
| Linux | `LinuxKeychainProvider` | `libsecret` Secret Service API |
| Fallback | `EncryptedFileKeychainProvider` | AES-256-GCM, key derived from machine entropy |

All implementations expose a common `IKeychainProvider` interface:
```csharp
public interface IKeychainProvider
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
```

Key naming convention: `"zapi-cli:<accountName>:<tokenType>"` (e.g., `"zapi-cli:work:pat"`).

Runtime platform detection uses `RuntimeInformation.IsOSPlatform(...)`. The fallback is selected programmatically when the OS keychain is unavailable (e.g., `libsecret` daemon not running).

Tokens are **never written to `accounts.json`**, stdout, or any trace log. `account show` displays `"token": "***"`.

## Consequences

### Positive

- **POS-001**: Tokens stored in the OS keychain are protected by the OS ACL — not accessible to other users and not present in filesystem snapshots, backups, or git history.
- **POS-002**: No NuGet keychain package dependency — P/Invoke keeps the binary fully self-contained with no native addon build step.
- **POS-003**: The `IKeychainProvider` interface allows the fallback to be swapped seamlessly, and simplifies unit testing via mock injection.
- **POS-004**: The AES-256-GCM fallback enables headless Linux deployments (e.g., CI agents, containers) without requiring a running Secret Service daemon.
- **POS-005**: Key namespacing (`zapi-cli:<accountName>:<tokenType>`) prevents collisions with other applications in the OS keychain.

### Negative

- **NEG-001**: P/Invoke implementations require platform-specific testing on all three OS families — CI must cover macOS, Windows, and Linux runners.
- **NEG-002**: The encrypted file fallback's security is bounded by the quality of the machine-entropy key derivation — on shared or virtualized hosts, entropy uniqueness may be weaker.
- **NEG-003**: macOS Keychain prompts for user confirmation on first access unless the application is code-signed and has the keychain entitlement — unsigned developer builds may trigger UI prompts that are disruptive in agent use.
- **NEG-004**: On Linux, `libsecret` requires a running GNOME Keyring or KWallet daemon; automated/headless environments silently fall through to the encrypted file, which may not be the user's intent.

## Alternatives Considered

##### Plaintext tokens in accounts.json

- **ALT-001**: **Description**: Store tokens in the same JSON file alongside account metadata.
- **ALT-002**: **Rejection Reason**: Plaintext tokens in a config file are a critical security risk. Config files are routinely synced, backed up, and accidentally committed to version control. Rejected without further consideration.

##### NuGet keychain library (e.g., Meziantou.Framework.Win32.CredentialManager)

- **ALT-003**: **Description**: Use an existing NuGet package to abstract keychain access instead of P/Invoke.
- **ALT-004**: **Rejection Reason**: Available NuGet packages cover Windows and macOS but lack Linux support. Cross-platform coverage requires either multiple packages or custom P/Invoke anyway. Direct P/Invoke keeps the dependency manifest minimal and the binary self-contained.

##### External secret manager (Vault, 1Password CLI, AWS Secrets Manager)

- **ALT-005**: **Description**: Delegate secret storage to an external secret manager accessed via its CLI or SDK.
- **ALT-006**: **Rejection Reason**: Introduces an external runtime dependency that cannot be guaranteed in all environments. Contradicts the zero-dependency install goal. Adds operational complexity for a developer tool.

##### Machine-scoped environment variables

- **ALT-007**: **Description**: Require the user to set `ZOHO_PAT_<ACCOUNT_NAME>` environment variables.
- **ALT-008**: **Rejection Reason**: Environment variables are globally readable by processes under the same user, commonly leaked in CI logs, and poorly suited to multi-account management. Not appropriate for long-lived credential storage.

## Implementation Notes

- **IMP-001**: `ZapiCli.Keychain.csproj` isolates all P/Invoke code to a single project, keeping `ZapiCli.Core` clean and the keychain layer independently testable.
- **IMP-002**: `EncryptedFileKeychainProvider` stores encrypted blobs in `<configDir>/keystore/<accountName>.bin`. AES-256-GCM with a random 12-byte nonce prepended to the ciphertext. Machine entropy key: PBKDF2-SHA256 over a stable machine identifier (e.g., machine GUID on Windows, `ioreg` serial on macOS, `/etc/machine-id` on Linux).
- **IMP-003**: `accounts.json` is written with `0600` Unix permissions (`File.SetUnixFileMode`) on macOS and Linux. On Windows, NTFS ACL restricts access to the current user only.
- **IMP-004**: The ZohoCorp domain check in `PatAuthProvider.StoreTokenAsync` must execute before any keychain write — see ADR-0005.
- **IMP-005**: Success metric: `account add` → keychain write → `account remove` → keychain delete cycle completes without OS UI prompts in a signed release build on macOS.

## References

- **REF-001**: [tech_spec.md — Section 8: Storage](../tech_spec.md#8-storage)
- **REF-002**: [tech_spec.md — Section 6: Authentication — IKeychainProvider Contract](../tech_spec.md#6-authentication)
- **REF-003**: [ADR-0002: Pluggable Authentication Interface with PAT-First Approach](./adr-0002-pluggable-auth-interface-pat-first.md)
- **REF-004**: [ADR-0005: ZohoCorp Domain Hard Block](./adr-0005-zohocorp-domain-hard-block.md)
