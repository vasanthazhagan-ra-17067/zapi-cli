namespace ZapiCli.Core.Auth;

/// <summary>
/// Pluggable authentication provider interface (ADR-0002).
/// v1 concrete: <see cref="OAuthProvider"/> — OAuth Self-Client flow.
/// </summary>
public interface IAuthProvider
{
    /// <summary>
    /// Retrieves the stored access token for the named account from the OS keychain.
    /// Throws <see cref="ZapiCliException"/> with code <c>KEYCHAIN_ERROR</c> (exit 2) if not found.
    /// </summary>
    Task<string> GetTokenAsync(string accountName, CancellationToken ct = default);

    /// <summary>
    /// Stores the full OAuth credential bundle (access_token + refresh_token + client_id + client_secret)
    /// in the OS keychain as a JSON blob.
    /// </summary>
    Task StoreTokenAsync(
        string accountName,
        string accessToken,
        string refreshToken,
        string clientId,
        string clientSecret,
        CancellationToken ct = default);

    /// <summary>Removes the OAuth credentials from the keychain for the named account. Best-effort.</summary>
    Task ClearTokenAsync(string accountName, CancellationToken ct = default);

    /// <summary>
    /// Refreshes the access token using stored client_id + client_secret via the Zoho token endpoint.
    /// Persists the new access_token to the keychain and returns it.
    /// Throws <see cref="ZapiCliException"/> with <c>AUTH_FAILURE</c> (exit 2) on failure.
    /// </summary>
    Task<string> RefreshTokenAsync(
        string accountName,
        IEnumerable<string> scopes,
        string dc,
        CancellationToken ct = default);

    /// <summary>
    /// Exchanges the stored refresh token for a short-lived scope enhancement token
    /// via POST /oauth/v2/token/scopeenhance (grant_type=update_scopes_token).
    /// Returns a tuple of (enhanceToken, clientId).
    /// The enhanceToken expires in ~600s and must not be stored.
    /// Throws <see cref="ZapiCliException"/> with <c>SCOPE_ENHANCE_FAILED</c> (exit 2) on HTTP failure
    /// or if access_token is missing from the response.
    /// </summary>
    Task<(string EnhanceToken, string ClientId)> GetScopeEnhancementTokenAsync(
        string accountName,
        string dc,
        CancellationToken ct = default);
}
