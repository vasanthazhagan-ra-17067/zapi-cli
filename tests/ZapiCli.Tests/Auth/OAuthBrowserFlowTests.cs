using ZapiCli.Core.Auth;

namespace ZapiCli.Tests.Auth;

public sealed class OAuthBrowserFlowTests
{
    private readonly OAuthBrowserFlow _flow = new();

    [Fact]
    public void GenerateState_ReturnsNonEmptyString_AtLeast32Chars()
    {
        var state = _flow.GenerateState();

        Assert.NotEmpty(state);
        // Base64URL of 32 bytes = 43 chars (no padding)
        Assert.True(state.Length >= 32, $"Expected length >= 32 but got {state.Length}");
    }

    [Fact]
    public void GenerateState_TwoSuccessiveCallsReturnDifferentValues()
    {
        var s1 = _flow.GenerateState();
        var s2 = _flow.GenerateState();

        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void GenerateState_IsUrlSafeBase64()
    {
        var state = _flow.GenerateState();

        // URL-safe base64: only contains A-Z, a-z, 0-9, -, _
        Assert.Matches(@"^[A-Za-z0-9\-_]+$", state);
        // Must not contain standard base64 chars +, /, or padding =
        Assert.DoesNotContain('+', state);
        Assert.DoesNotContain('/', state);
        Assert.DoesNotContain('=', state);
    }

    [Fact]
    public void BuildAuthorizationUrl_ContainsResponseTypeCode()
    {
        var url = BuildSampleUrl();
        Assert.Contains("response_type=code", url);
    }

    [Fact]
    public void BuildAuthorizationUrl_ContainsAccessTypeOffline()
    {
        var url = BuildSampleUrl();
        Assert.Contains("access_type=offline", url);
    }

    [Fact]
    public void BuildAuthorizationUrl_ContainsClientId()
    {
        var url = BuildSampleUrl();
        Assert.Contains("my-client-id", url);
    }

    [Fact]
    public void BuildAuthorizationUrl_ContainsEncodedRedirectUri()
    {
        var url = BuildSampleUrl();
        Assert.Contains(Uri.EscapeDataString("http://localhost:12345/callback"), url);
    }

    [Fact]
    public void BuildAuthorizationUrl_ContainsScopesJoinedWithComma()
    {
        var url = BuildSampleUrl();
        Assert.Contains(Uri.EscapeDataString("ZohoAPI.Resource.READ,ZohoAPI.Resource.WRITE"), url);
    }

    [Fact]
    public void BuildAuthorizationUrl_ContainsState()
    {
        var url = BuildSampleUrl();
        Assert.Contains("my-state", url);
    }

    private string BuildSampleUrl() =>
        _flow.BuildAuthorizationUrl(
            "https://accounts.zoho.com",
            "my-client-id",
            "http://localhost:12345/callback",
            ["ZohoAPI.Resource.READ", "ZohoAPI.Resource.WRITE"],
            "my-state");
}
