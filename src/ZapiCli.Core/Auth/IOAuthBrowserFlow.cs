namespace ZapiCli.Core.Auth;

/// <summary>
/// Abstraction for the browser-based OAuth flow — generating CSRF state tokens,
/// building the Zoho authorization URL, and launching the system browser.
/// Abstracted as an interface for DI and testability.
/// </summary>
public interface IOAuthBrowserFlow
{
    /// <summary>
    /// Generates a cryptographically random, URL-safe base64 state token for CSRF protection.
    /// </summary>
    string GenerateState();

    /// <summary>
    /// Builds the Zoho OAuth 2.0 authorization URL that the browser should navigate to.
    /// </summary>
    /// <param name="baseUrl">Zoho Accounts base URL (e.g. <c>https://accounts.zoho.com</c>).</param>
    /// <param name="clientId">OAuth client ID.</param>
    /// <param name="redirectUri">Redirect URI registered in Zoho Developer Console (no trailing slash).</param>
    /// <param name="scopes">Array of scope strings to request (joined with comma).</param>
    /// <param name="state">CSRF state token from <see cref="GenerateState"/>.</param>
    string BuildAuthorizationUrl(string baseUrl, string clientId, string redirectUri, string[] scopes, string state);

    /// <summary>
    /// Prints <paramref name="url"/> to <c>stderr</c> (always visible) then attempts to open
    /// the system browser. Any browser-launch failure is silently swallowed; the printed URL
    /// serves as the manual-paste fallback.
    /// </summary>
    void OpenBrowser(string url);
}
