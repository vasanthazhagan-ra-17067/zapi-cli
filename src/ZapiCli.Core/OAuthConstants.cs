namespace ZapiCli.Core;

/// <summary>
/// App-wide OAuth constants shared across the CLI layer and ZapiCli.Core.
/// All additions must also be documented in tech_spec/docs/constants-reference.md.
/// </summary>
public static class OAuthConstants
{
    /// <summary>
    /// Zoho profile scope that must be included in every login request to guarantee
    /// that the user-info endpoint returns the account email and ZUID.
    /// </summary>
    public const string RequiredProfileScope = "aaaServer.profile.READ";

    /// <summary>
    /// Fixed local TCP port for the OAuth callback HTTP server.
    /// Register <c>http://localhost:8085/callback</c> as a redirect URI in the
    /// Zoho Developer Console for every app type that uses the browser flow.
    /// </summary>
    public const int DefaultCallbackPort = 8085;
}
