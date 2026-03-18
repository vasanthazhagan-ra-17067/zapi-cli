namespace ZapiCli.Core.Accounts;

/// <summary>
/// Business logic for all six <c>account</c> subcommands.
/// All network calls, security checks, and persistence live here — command classes are thin wrappers.
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Exchanges a grant code for OAuth tokens, validates identity via user-info, enforces the ZohoCorp block,
    /// stores credentials in the keychain, and persists the account entry.
    /// </summary>
    Task<(string Name, string Dc)> AddAccountAsync(
        string name,
        string code,
        string redirectUri,
        string clientId,
        string clientSecret,
        string dc,
        CancellationToken ct = default);

    /// <summary>Returns masked projections of all accounts (no credential values).</summary>
    Task<IReadOnlyList<AccountListView>> ListAccountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns all account fields with <c>token</c> unconditionally masked as <c>"***"</c>.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    Task<AccountShowView> ShowAccountAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Sets <c>is_default=true</c> on the named account and clears the flag on all others.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    Task SetDefaultAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Attempts server-side token revocation (best-effort), then removes the keychain entry
    /// and deletes the account from accounts.json.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    Task RemoveAccountAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Calls <see cref="ZapiCli.Core.Auth.IAuthProvider.RefreshTokenAsync"/> and clears
    /// the <c>NeedsReauth</c> flag on success.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    Task ReAuthAsync(string name, CancellationToken ct = default);
}
