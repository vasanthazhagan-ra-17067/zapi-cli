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
    /// <para>
    /// <paramref name="scopes"/>: The OAuth scope strings that were selected when generating the grant code.
    /// These are persisted in <see cref="AccountEntry.Scopes"/> so that scope list and scope add have a correct baseline.
    /// </para>
    /// </summary>
    Task<(string Name, string Dc)> AddAccountAsync(
        string name,
        string code,
        string redirectUri,
        string clientId,
        string clientSecret,
        string dc,
        IEnumerable<string> scopes,
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
    /// Calls <see cref="ZapiCli.Core.Auth.IAuthProvider.RefreshTokenAsync"/> to obtain a fresh access token.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    Task ReAuthAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Starts a local callback server, opens the Zoho OAuth authorization URL in the system browser,
    /// waits for the redirect callback (up to 120 seconds), verifies the CSRF state, then exchanges
    /// the grant code for tokens and persists the account (same as AddAccountAsync).
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_ALREADY_EXISTS</c> if a duplicate name is provided.
    /// Throws <see cref="ZapiCliException"/> with <c>STATE_MISMATCH</c> if CSRF state does not match.
    /// Throws <see cref="ZapiCliException"/> with <c>LOGIN_TIMEOUT</c> if browser auth times out.
    /// </summary>
    /// <param name="callbackPort">
    /// The local port the callback HTTP server will bind to (default 8085).
    /// Register <c>http://localhost:{callbackPort}/callback</c> as a redirect URI in the
    /// Zoho Developer Console — this must match exactly every time.
    /// </param>
    Task<(string Name, string Dc)> LoginAsync(
        string name,
        string clientId,
        string clientSecret,
        string[] scopes,
        string dc,
        int callbackPort = 8085,
        CancellationToken ct = default);

    /// <summary>
    /// Adds one or more scopes to the account's scope list via the Zoho incremental authorization
    /// two-step browser flow (scope enhancement). Enforces the ZohoCorp block.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    /// <param name="accountName">Target account; if null, the default account is used.</param>
    /// <param name="scopesToAdd">Individual scope strings to add (already split and trimmed).</param>
    /// <param name="callbackPort">
    /// The local port the scope-enhancement callback HTTP server will bind to (default 8085).
    /// Register <c>http://localhost:{callbackPort}/callback</c> as a redirect URI in the
    /// Zoho Developer Console — this must match exactly.
    /// </param>
    /// <returns>The account name and its updated scope list.</returns>
    Task<(string AccountName, List<string> UpdatedScopes)> AddScopesAsync(
        string accountName,
        IEnumerable<string> scopesToAdd,
        int callbackPort = 8085,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the scope list for the named account.
    /// Enforces the ZohoCorp block.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    Task<List<string>> GetScopesAsync(string accountName, CancellationToken ct = default);
}
