using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZapiCli.Keychain;

namespace ZapiCli.Core.Auth;

/// <summary>
/// OAuth Self-Client authentication provider (ADR-0002).
/// Stores the full credential bundle (access_token + client_id + client_secret)
/// as a camelCase JSON blob in the OS keychain under key <c>zapi-cli:&lt;accountName&gt;:oauth</c>.
/// </summary>
public sealed class OAuthProvider : IAuthProvider
{
    private const string KeyPrefix = "zapi-cli";

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IKeychainProvider _keychain;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OAuthProvider> _logger;

    public OAuthProvider(
        IKeychainProvider keychain,
        IHttpClientFactory httpClientFactory,
        ILogger<OAuthProvider> logger)
    {
        _keychain = keychain;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static string MakeKey(string accountName) => $"{KeyPrefix}:{accountName}:oauth";

    // ─── IAuthProvider ────────────────────────────────────────────────────────

    public async Task<string> GetTokenAsync(string accountName, CancellationToken ct = default)
    {
        var json = await _keychain.GetAsync(MakeKey(accountName), ct).ConfigureAwait(false);
        if (json is null)
            throw new ZapiCliException(
                $"No credentials found in keychain for account '{accountName}'.",
                ErrorCodes.KEYCHAIN_ERROR,
                exitCode: 2);

        var creds = JsonSerializer.Deserialize<OAuthCredentials>(json, CamelCaseOptions);
        if (creds is null)
            throw new ZapiCliException(
                $"Corrupted credential blob in keychain for account '{accountName}'.",
                ErrorCodes.KEYCHAIN_ERROR,
                exitCode: 2);

        return creds.AccessToken;
    }

    public async Task StoreTokenAsync(
        string accountName,
        string accessToken,
        string refreshToken,
        string clientId,
        string clientSecret,
        CancellationToken ct = default)
    {
        var creds = new OAuthCredentials
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ClientId = clientId,
            ClientSecret = clientSecret,
        };
        var json = JsonSerializer.Serialize(creds, CamelCaseOptions);
        await _keychain.SetAsync(MakeKey(accountName), json, ct).ConfigureAwait(false);
    }

    public async Task ClearTokenAsync(string accountName, CancellationToken ct = default)
    {
        try
        {
            await _keychain.DeleteAsync(MakeKey(accountName), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear keychain entry for account '{AccountName}'.", accountName);
        }
    }

    public async Task<string> RefreshTokenAsync(
        string accountName,
        IEnumerable<string> scopes,
        string dc,
        CancellationToken ct = default)
    {
        var json = await _keychain.GetAsync(MakeKey(accountName), ct).ConfigureAwait(false);
        if (json is null)
            throw new ZapiCliException(
                $"No credentials found in keychain for account '{accountName}'.",
                ErrorCodes.KEYCHAIN_ERROR,
                exitCode: 2);

        var creds = JsonSerializer.Deserialize<OAuthCredentials>(json, CamelCaseOptions);
        if (creds is null)
            throw new ZapiCliException(
                $"Corrupted credential blob in keychain for account '{accountName}'.",
                ErrorCodes.KEYCHAIN_ERROR,
                exitCode: 2);

        var baseUrl = DcResolver.GetAccountsBaseUrl(dc);
        var tokenUrl = $"{baseUrl}/oauth/v2/token";

        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = creds.ClientId,
            ["client_secret"] = creds.ClientSecret,
            ["refresh_token"] = creds.RefreshToken,
        };

        using var httpClient = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(formData),
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ZapiCliException(
                $"Token refresh request failed: {ex.Message}",
                ErrorCodes.AUTH_FAILURE,
                exitCode: 2);
        }

        if (!response.IsSuccessStatusCode)
            throw new ZapiCliException(
                $"Token refresh failed with HTTP {(int)response.StatusCode}.",
                ErrorCodes.AUTH_FAILURE,
                exitCode: 2);

        using var responseDoc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);

        if (!responseDoc.RootElement.TryGetProperty("access_token", out var tokenElement))
            throw new ZapiCliException(
                "Token refresh response did not contain 'access_token'.",
                ErrorCodes.AUTH_FAILURE,
                exitCode: 2);

        var newToken = tokenElement.GetString();
        if (string.IsNullOrEmpty(newToken))
            throw new ZapiCliException(
                "Token refresh response returned an empty 'access_token'.",
                ErrorCodes.AUTH_FAILURE,
                exitCode: 2);

        // Persist the updated token (preserve existing refresh_token, client_id + client_secret).
        var updatedCreds = creds with { AccessToken = newToken };
        var updatedJson = JsonSerializer.Serialize(updatedCreds, CamelCaseOptions);
        await _keychain.SetAsync(MakeKey(accountName), updatedJson, ct).ConfigureAwait(false);

        return newToken;
    }
}
