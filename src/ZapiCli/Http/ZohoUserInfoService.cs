using System.Net.Http.Headers;
using System.Text.Json;
using ZapiCli.Core.Auth;

namespace ZapiCli.Http;

/// <summary>
/// Fetches user information from the Zoho OAuth user-info endpoint.
/// Endpoint: GET https://accounts.{domain}/oauth/user/info
/// Authorisation: Zoho-oauthtoken {token}
/// </summary>
internal sealed class ZohoUserInfoService : IUserInfoService
{
    private readonly IHttpClientFactory _factory;

    public ZohoUserInfoService(IHttpClientFactory factory) => _factory = factory;

    public async Task<string?> GetEmailAsync(string domain, string token, CancellationToken ct = default)
    {
        using var client = _factory.CreateClient();
        var url = $"https://accounts.{domain}/oauth/user/info";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Zoho-oauthtoken", token);

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (doc.RootElement.TryGetProperty("Email", out var emailEl))
            return emailEl.GetString();

        return null;
    }
}
