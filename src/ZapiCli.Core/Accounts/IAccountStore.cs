namespace ZapiCli.Core.Accounts;

/// <summary>
/// Persistent storage for the accounts.json configuration file.
/// </summary>
public interface IAccountStore
{
    /// <summary>Loads all accounts from disk. Returns an empty root if the file does not exist.</summary>
    Task<AccountsRoot> LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the entire accounts root to disk with restrictive file permissions (0600 on Unix).</summary>
    Task SaveAsync(AccountsRoot root, CancellationToken ct = default);

    /// <summary>Returns the named account, or <see langword="null"/> if not found.</summary>
    Task<AccountEntry?> FindAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Returns the default account (IsDefault=true first, then first account).
    /// Throws <see cref="ZapiCliException"/> with code <c>NO_DEFAULT_ACCOUNT</c> if no accounts are configured.
    /// </summary>
    Task<AccountEntry> GetDefaultAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the first account whose <c>Email</c> matches <paramref name="email"/> case-insensitively,
    /// or <see langword="null"/> if no account has that email.
    /// </summary>
    Task<AccountEntry?> FindByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Returns the first account whose <c>Zuid</c> (zuidstring on disk) matches
    /// <paramref name="zuidstring"/> with <see cref="StringComparison.Ordinal"/> (exact),
    /// or <see langword="null"/> if no account has that ZUID.
    /// </summary>
    Task<AccountEntry?> FindByZuidAsync(string zuidstring, CancellationToken ct = default);

    /// <summary>
    /// Copies <c>accounts.json</c> from the current data directory to <paramref name="newDataDir"/>
    /// if the source file exists and a file does not already exist at the destination.
    /// Returns <see langword="true"/> if a file was copied; <see langword="false"/> otherwise.
    /// </summary>
    Task<bool> MigrateToDirectoryAsync(string newDataDir, CancellationToken ct = default);
}
