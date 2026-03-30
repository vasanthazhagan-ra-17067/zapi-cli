namespace ZapiCli.Core.Accounts;

/// <summary>
/// Business logic for all account subcommands.
/// All network calls, security checks, and persistence live here — command classes are thin wrappers.
/// </summary>
public interface IAccountService
{
    /// <summary>Returns masked projections of all accounts (no credential values).</summary>
    Task<IReadOnlyList<AccountListView>> ListAccountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns all account fields with <c>token</c> unconditionally masked as <c>"***"</c>.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    Task<AccountShowView> ShowAccountAsync(string? name, string? email = null, string? zuidstring = null, CancellationToken ct = default);

    /// <summary>
    /// Sets <c>is_default=true</c> on the named account and clears the flag on all others.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    Task SetDefaultAsync(string? name, string? email = null, string? zuidstring = null, CancellationToken ct = default);

    /// <summary>
    /// Attempts server-side token revocation (best-effort), then removes the keychain entry
    /// and deletes the account from accounts.json.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    Task RemoveAccountAsync(string? name, string? email = null, string? zuidstring = null, CancellationToken ct = default);

    /// <summary>
    /// Calls <see cref="ZapiCli.Core.Auth.IAuthProvider.RefreshTokenAsync"/> to obtain a fresh access token.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    Task ReAuthAsync(string? name, string? email = null, string? zuidstring = null, CancellationToken ct = default);

    /// <summary>
    /// Starts a local callback server, opens the Zoho OAuth authorization URL in the system browser,
    /// waits for the redirect callback (up to 120 seconds), verifies the CSRF state, then exchanges
    /// the grant code for tokens and persists the account (same as AddAccountAsync).
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_ALREADY_EXISTS</c> if a duplicate name is provided.
    /// Throws <see cref="ZapiCliException"/> with <c>STATE_MISMATCH</c> if CSRF state does not match.
    /// Throws <see cref="ZapiCliException"/> with <c>LOGIN_TIMEOUT</c> if browser auth times out.
    /// </summary>
    /// <summary>
    /// Authenticates via the Zoho Mobile OAuth 2.0 flow (<c>/oauth/v2/mobile/auth</c>).
    /// <para>
    /// No <c>client_secret</c> is required upfront. The CLI generates an RSA key pair, sends the
    /// public key as <c>ss_id</c> in the auth URL, and Zoho encrypts the <c>client_secret</c> in
    /// the redirect callback (<c>gt_sec</c>). The CLI decrypts it with the RSA private key.
    /// DC is derived from the <c>location</c> parameter in the OAuth callback.
    /// </para>
    /// <para>
    /// When <paramref name="name"/> is <c>null</c>, the account name is derived from the email address
    /// returned by the user-info endpoint (replacing <c>@</c> and <c>.</c> with <c>_</c>).
    /// </para>
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_ALREADY_EXISTS</c> if the resolved name is taken.
    /// Throws <see cref="ZapiCliException"/> with <c>STATE_MISMATCH</c> if CSRF state does not match.
    /// Throws <see cref="ZapiCliException"/> with <c>LOGIN_TIMEOUT</c> if browser auth times out.
    /// Throws <see cref="ZapiCliException"/> with <c>RSA_DECRYPT_FAILURE</c> if <c>gt_sec</c> cannot be decrypted.
    /// Throws <see cref="ZapiCliException"/> with <c>DCL_MISSING</c> if the token response lacks <c>dc_locations</c>.
    /// </summary>
    Task<(string Name, string Dc)> MobileLoginAsync(
        string? name,
        string clientId,
        string[] scopes,
        string? clientSecret = null,
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
        int callbackPort = OAuthConstants.DefaultCallbackPort,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the scope list for the named account.
    /// Enforces the ZohoCorp block.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if absent.
    /// </summary>
    Task<List<string>> GetScopesAsync(string accountName, CancellationToken ct = default);

    /// <summary>
    /// Renames an account identified by any one of <paramref name="name"/>, <paramref name="email"/>,
    /// or <paramref name="zuidstring"/> to <paramref name="newName"/>.
    /// Updates <c>accounts.json</c> and the keychain credential key.
    /// Throws <see cref="ZapiCliException"/> with <c>INVALID_ARGS</c> if none or multiple identifiers are provided,
    /// or if <paramref name="newName"/> is empty or contains invalid characters.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_NOT_FOUND</c> if no account matches.
    /// Throws <see cref="ZapiCliException"/> with <c>ACCOUNT_ALREADY_EXISTS</c> if <paramref name="newName"/> is taken.
    /// Throws <see cref="ZapiCliException"/> with <c>KEYCHAIN_ERROR</c> or <c>ACCOUNT_RENAME_FAILED</c> on keychain failure.
    /// </summary>
    Task<(string OldName, string NewName)> RenameAccountAsync(
        string? name,
        string? email,
        string? zuidstring,
        string newName,
        CancellationToken ct = default);
}
