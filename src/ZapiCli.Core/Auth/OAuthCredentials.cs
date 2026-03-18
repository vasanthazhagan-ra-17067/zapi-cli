namespace ZapiCli.Core.Auth;

/// <summary>
/// Internal keychain blob for OAuth Self-Client credentials (OQ-002).
/// Serialized with camelCase (NOT snake_case) since this is an internal keychain format,
/// not a user-visible output.
/// Layout: { "accessToken": "...", "refreshToken": "...", "clientId": "...", "clientSecret": "..." }
/// </summary>
internal sealed record OAuthCredentials
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
}
