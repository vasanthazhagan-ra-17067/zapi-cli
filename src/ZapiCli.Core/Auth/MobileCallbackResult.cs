namespace ZapiCli.Core.Auth;

/// <summary>
/// Parsed result from the Zoho Mobile OAuth redirect callback.
/// Returned by <see cref="LocalCallbackServer.WaitForMobileCallbackAsync"/>.
/// </summary>
/// <param name="Code">The short-lived authorization code (<c>?code=...</c>).</param>
/// <param name="State">The CSRF state token — must match what was sent in the auth URL.</param>
/// <param name="GtHash">
/// Optional token hash (<c>?gt_hash=...</c>) — sent as <c>rt_hash</c> in the token exchange POST
/// when present. Only returned by clients configured as Mobile Application type in Zoho.
/// </param>
/// <param name="GtSec">
/// Optional RSA-encrypted client secret (<c>?gt_sec=...</c>). Only returned by Zoho for Mobile
/// Application clients that use the RSA key-exchange path. When absent, the caller must provide
/// the client secret explicitly (e.g. via <c>--client-secret</c>).
/// </param>
/// <param name="AccountsServer">
/// Optional per-user Zoho Accounts server URL (<c>?accounts-server=...</c>).
/// When present, overrides the DC-derived base URL for token exchange, user-info, and refresh calls.
/// </param>
/// <param name="Location">Optional DCL location prefix (e.g. <c>"us"</c>, <c>"eu"</c>).</param>
public record MobileCallbackResult(
    string Code,
    string State,
    string? GtHash,
    string? GtSec,
    string? AccountsServer,
    string? Location);
