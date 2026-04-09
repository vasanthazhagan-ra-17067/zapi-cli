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
- **macOS**: Keychain via `Security.framework`
- **Windows**: Credential Manager via `CredWrite` / `CredRead`
- **Linux**: Secret Service API via `libsecret`

These OS APIs are native C libraries accessible through P/Invoke.

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

Runtime selection via `RuntimeInformation.IsOSPlatform(...)`. The `EncryptedFileKeychainProvider` is used when the OS keychain is unavailable.

Keychain key format: `zapi-cli:<accountName>:oauth` (sealed constant; not configurable).

All implementations are located in the dedicated `ZapiCli.Keychain` project.

## Consequences

### Positive

- **POS-001**: Direct P/Invoke to OS-native APIs eliminates all third-party NuGet dependencies for credential storage.
- **POS-002**: The `IKeychainProvider` interface allows unit tests to inject a mock/in-memory keychain.
- **POS-003**: The `EncryptedFileKeychainProvider` fallback ensures the tool remains functional on headless Linux environments.
- **POS-004**: Isolating keychain code in `ZapiCli.Keychain` keeps platform-specific P/Invoke declarations out of the domain layer.

### Negative

- **NEG-001**: Direct P/Invoke to OS security APIs introduces platform-specific complexity.
- **NEG-002**: `EncryptedFileKeychainProvider` stores an encrypted file on disk; machine reimaging can make stored credentials unrecoverable.
- **NEG-003**: P/Invoke signatures are error-prone; incorrect marshalling can cause crashes.

## Implementation Notes

- **IMP-001**: `EncryptedFileKeychainProvider` must derive the AES-256 key from machine-specific entropy using HKDF.
- **IMP-002**: The fallback provider stores encrypted files in `<configDir>/keystore/<accountName>.bin` with `0600` permissions on Unix.
- **IMP-003**: `DependencyInjectionRegistrar` selects the platform-appropriate implementation at startup.
- **IMP-004**: On macOS, the P/Invoke target is `Security` framework at `/System/Library/Frameworks/Security.framework/Security`.

## References

- **REF-001**: [ADR-0002: Authentication via OAuth Self-Client](adr-0002-oauth-self-client-authentication.md)
- **REF-002**: [ADR-0007: Three-Layer Project Structure](adr-0007-three-layer-project-structure.md)
