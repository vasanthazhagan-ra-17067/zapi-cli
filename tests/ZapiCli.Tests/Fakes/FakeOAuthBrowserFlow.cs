using ZapiCli.Core.Auth;

namespace ZapiCli.Tests.Fakes;

/// <summary>
/// Records all calls to IOAuthBrowserFlow methods and returns preset values.
/// Used in tests for AccountService.LoginAsync.
/// </summary>
internal sealed class FakeOAuthBrowserFlow : IOAuthBrowserFlow
{
    private readonly string _presetState;

    public FakeOAuthBrowserFlow(string presetState = "fake-state-token")
    {
        _presetState = presetState;
    }

    public List<string> OpenBrowserCalls { get; } = [];
    public List<(string BaseUrl, string ClientId, string RedirectUri, string[] Scopes, string State)> BuildUrlCalls { get; } = [];
    public List<(string BaseUrl, string ClientId, string RedirectUri, string[] Scopes, string State, string PublicKeyBase64)> BuildMobileUrlCalls { get; } = [];
    public int GenerateStateCalls { get; private set; }

    public string GenerateState()
    {
        GenerateStateCalls++;
        return _presetState;
    }

    public string BuildAuthorizationUrl(string baseUrl, string clientId, string redirectUri, string[] scopes, string state)
    {
        BuildUrlCalls.Add((baseUrl, clientId, redirectUri, scopes, state));
        return $"https://accounts.zoho.com/oauth/v2/auth?client_id={clientId}&state={state}";
    }

    public string BuildMobileAuthorizationUrl(string baseUrl, string clientId, string redirectUri, string[] scopes, string state, string publicKeyBase64)
    {
        BuildMobileUrlCalls.Add((baseUrl, clientId, redirectUri, scopes, state, publicKeyBase64));
        return $"https://accounts.zoho.com/oauth/v2/mobile/auth?client_id={clientId}&state={state}&ss_id={Uri.EscapeDataString(publicKeyBase64)}";
    }

    public void OpenBrowser(string url)
    {
        OpenBrowserCalls.Add(url);
    }
}
