namespace ZapiCli.Keychain;

/// <summary>
/// Abstraction for OS-native credential storage. Implementations exist for
/// macOS Keychain Services, Windows Credential Manager, Linux Secret Service,
/// and an AES-256-GCM encrypted-file fallback.
/// </summary>
public interface IKeychainProvider
{
    /// <summary>Retrieves a previously stored secret by its key.</summary>
    /// <param name="key">The storage key, e.g. <c>zapi-cli:work:oauth</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored secret, or <see langword="null"/> if not found.</returns>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Stores or updates a secret under the given key.</summary>
    /// <param name="key">The storage key, e.g. <c>zapi-cli:work:oauth</c>.</param>
    /// <param name="value">The secret value to store.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetAsync(string key, string value, CancellationToken ct = default);

    /// <summary>Deletes the secret associated with the given key if it exists.</summary>
    /// <param name="key">The storage key to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(string key, CancellationToken ct = default);
}
