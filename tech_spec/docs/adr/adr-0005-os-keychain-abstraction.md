---
title: "ADR-0005: OS Keychain Abstraction via P/Invoke with Encrypted File Fallback"
status: "Accepted"
date: "2026-03-18"
authors: "zapi-cli Core Team"
tags: ["architecture", "decision", "security", "keychain", "credentials"]
supersedes: ""
superseded_by: ""
---

# ADR-0005: OS Keychain Abstraction via P/Invoke with Encrypted File Fallback

## Status

**Accepted**

## Context

zapi-cli stores OAuth access tokens, client IDs, and client secrets for each configured account. These credentials must be kept secure and must never appear in plain-text files, stdout, log files, or environment variables.

The standard approach for secure credential storage on each platform is the OS-provided credential store:
- **macOS**: Keychain via `Security.framework` (`SecKeychainAddGenericPassword`, `SecKeychainFindGenericPassword`).
- **Windows**: Credential Manager via `CredWrite` / `CredRead`.
- **Linux**: Secret Service API via `libsecret`.

However, these OS APIs are native C libraries. .NET can call them through P/Invoke (Platform Invocation Services) without any additional managed wrappers.

The key design question is: **should we use existing NuGet packages for keychain access, or implement P/Invoke directly?**

Context factors:
- zapi-cli is distributed as a self-contained single-file binary. Adding NuGet dependencies for keychain access increases binary size and adds third-party supply-chain risk.
- Existing .NET keychain NuGet packages (e.g., `SecureStorage.Net`, platform-specific wrappers) have varying maintenance quality and abstract away details needed for fine-grained key control.
- Not all Linux environments have `libsecret` available (e.g., headless servers, containers). A fallback mechanism is required.

## Decision

Implement the `IKeychainProvider` interface with four concrete classes:

```csharp
public interface IKeychainProvider
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
```

| Class | Platform | Backend |
|---|---|---|
| `MacOsKeychainProvider` | macOS | `Security.framework` via P/Invoke |
| `WindowsKeychainProvider` | Windows | `CredWrite` / `CredRead` via P/Invoke |
| `LinuxKeychainProvider` | Linux | `libsecret` Secret Service API via P/Invoke |
| `EncryptedFileKeychainProvider` | Any (fallback) | AES-256-GCM, key derived from machine entropy |

Runtime selection via `RuntimeInformation.IsOSPlatform(...)`. The `EncryptedFileKeychainProvider` is used when the OS keychain is unavailable (e.g., headless Linux without a Secret Service daemon).

Keychain key format: `zapi-cli:<accountName>:oauth` (sealed constant; not configurable).

All implementations are located in the dedicated `ZapiCli.Keychain` project, isolated from both the CLI layer and domain logic.

## Consequences

### Positive

- **POS-001**: Direct P/Invoke to OS-native APIs eliminates all third-party NuGet dependencies for credential storage, keeping the binary fully self-contained and minimizing supply-chain risk.
- **POS-002**: The `IKeychainProvider` interface allows unit tests to inject a mock/in-memory keychain without OS dependencies, enabling full test coverage of authentication flows.
- **POS-003**: The `EncryptedFileKeychainProvider` fallback (AES-256-GCM) ensures the tool remains functional on headless Linux environments (CI, containers) where no Secret Service daemon is running.
- **POS-004**: Isolating keychain code in `ZapiCli.Keychain` keeps platform-specific P/Invoke declarations out of the domain layer, making the project portable for future platform expansions.

### Negative

- **NEG-001**: Direct P/Invoke to OS security APIs introduces platform-specific complexity. Changes to Security.framework or Windows Credential Manager APIs require corresponding changes to the P/Invoke signatures.
- **NEG-002**: `EncryptedFileKeychainProvider` stores an encrypted file on disk; if the machine entropy source used for key derivation changes (e.g., on a re-imaged machine), stored credentials become unrecoverable without re-running `account add`.
- **NEG-003**: P/Invoke signatures are error-prone; incorrect marshalling of native types (e.g., `SecKeychainAttributeList`) can cause crashes or memory corruption. Thorough platform-specific integration testing is required.
- **NEG-004**: The `ZapiCli.Keychain` project cannot be fully unit-tested without mocking — integration tests for keychain providers require a macOS, Windows, or Linux environment respectively.

## Alternatives Considered

##### NuGet Package: `Microsoft.Windows.SDK.NET` / `CsWin32` (Windows Only)

- **ALT-001**: **Description**: Use Microsoft's CsWin32 source generator to produce P/Invoke stubs from Win32 metadata, removing hand-authored P/Invoke signatures for the Windows provider.
- **ALT-002**: **Rejection Reason**: CsWin32 solves Windows-only; macOS and Linux still require hand-authored P/Invoke. Adding CsWin32 just for Windows would create asymmetric patterns across providers. Revisit for v2 if Windows P/Invoke maintenance becomes a burden.

##### Third-Party NuGet: `SecureStorage` / `Luss.Net.SecureStorage`

- **ALT-003**: **Description**: Several community NuGet packages wrap OS keychains for .NET. They handle P/Invoke internally and expose a simple `Set/Get/Delete` API.
- **ALT-004**: **Rejection Reason**: Community packages introduce third-party supply-chain risk, may not support all required platforms or key-naming schemes, and add package dependencies that complicate the self-contained single-file publish. Direct P/Invoke with clear ownership is preferred.

##### Plain-Text Credential File (Restricted Permissions)

- **ALT-005**: **Description**: Write credentials to `accounts.json` with `0600` permissions (Unix) or NTFS user-only ACL (Windows). No OS keychain involvement.
- **ALT-006**: **Rejection Reason**: Plain-text credentials are vulnerable to file system-level access by other processes running as the same user, backup tools, and cloud sync services. OS keychain is specifically hardened against these vectors and must be used where available.

##### DPAPI (Windows) + AES File (Others)

- **ALT-007**: **Description**: Use Windows DPAPI (Data Protection API) on Windows and AES-encrypted files on macOS/Linux. DPAPI is simpler to call via `System.Security.Cryptography.ProtectedData`.
- **ALT-008**: **Rejection Reason**: macOS Keychain and Linux Secret Service are the idiomatic and hardened mechanisms on their respective platforms. Using file-based encryption as the primary (non-fallback) mechanism on macOS and Linux would be a regression in security posture.

## Implementation Notes

- **IMP-001**: `EncryptedFileKeychainProvider` must derive the AES-256 key from a combination of machine-specific entropy (e.g., machine GUID, hardware serial) using HKDF (HMAC-based Key Derivation Function), not a hard-coded or random-per-install key.
- **IMP-002**: The fallback provider stores encrypted files in `<configDir>/keystore/<accountName>.bin`. These files must be created with `0600` permissions on Unix.
- **IMP-003**: `DependencyInjectionRegistrar` selects the platform-appropriate implementation at startup using `RuntimeInformation.IsOSPlatform(...)`. The registration must not expose the concrete type — all consumers receive `IKeychainProvider`.
- **IMP-004**: On macOS, the P/Invoke target is the `Security` framework located at `/System/Library/Frameworks/Security.framework/Security`. On Linux, `libsecret-1.so.0` is loaded; if not found, fall back to `EncryptedFileKeychainProvider` gracefully.

## References

- **REF-001**: [tech_spec/tech_spec.md — Section 8: Storage](../tech_spec/tech_spec.md#8-storage)
- **REF-002**: [ADR-0002: Authentication via OAuth Self-Client](adr-0002-oauth-self-client-authentication.md)
- **REF-003**: [Apple Security.framework documentation](https://developer.apple.com/documentation/security/keychain_services)
- **REF-004**: [Windows Credential Manager (CredWrite)](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew)
