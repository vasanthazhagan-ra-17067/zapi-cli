using System.Net;
using System.Net.Http;
using ZapiCli.Core;
using ZapiCli.Core.Auth;

namespace ZapiCli.Tests.Auth;

public sealed class LocalCallbackServerTests
{
    // Bypass the system proxy (e.g. corporate Squid at 127.0.0.1:3128) for localhost test connections.
    private static HttpClient CreateNoProxyHttpClient() =>
        new HttpClient(new HttpClientHandler { UseProxy = false });

    [Fact]
    public void Constructor_BindsEphemeralPort_PortIsNonZero()
    {
        using var server = new LocalCallbackServer();
        Assert.True(server.Port > 0);
    }

    [Fact]
    public async Task WaitForCallbackAsync_ReturnsCodeAndState_OnValidCallback()
    {
        using var server = new LocalCallbackServer();

        // Start waiting for callback — HttpListener is already bound and listening.
        var callbackTask = server.WaitForCallbackAsync(TimeSpan.FromSeconds(10));

        // Simulate the browser redirect with code and state query params.
        // UseProxy=false bypasses corporate proxies that intercept localhost HTTP.
        using var httpClient = CreateNoProxyHttpClient();
        var response = await httpClient.GetAsync(
            $"http://localhost:{server.Port}/callback?code=testcode&state=teststate");

        var (code, state) = await callbackTask;

        Assert.Equal("testcode", code);
        Assert.Equal("teststate", state);
        // Browser received a response (did not hang).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WaitForCallbackAsync_ThrowsLoginTimeout_WhenTimeoutExpires()
    {
        using var server = new LocalCallbackServer();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            server.WaitForCallbackAsync(TimeSpan.FromMilliseconds(200)));

        Assert.Equal(ErrorCodes.LOGIN_TIMEOUT, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public async Task WaitForCallbackAsync_ThrowsStateMismatch_WhenErrorQueryParam()
    {
        using var server = new LocalCallbackServer();

        var callbackTask = server.WaitForCallbackAsync(TimeSpan.FromSeconds(10));

        using var httpClient = CreateNoProxyHttpClient();
        // Simulate Zoho returning ?error=access_denied
        await httpClient.GetAsync(
            $"http://localhost:{server.Port}/callback?error=access_denied");

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() => callbackTask);

        Assert.Equal(ErrorCodes.STATE_MISMATCH, ex.Code);
        Assert.Equal("access_denied", ex.Message);
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenCalledMultipleTimes()
    {
        var server = new LocalCallbackServer();
        server.Dispose();
        server.Dispose(); // second call must not throw
    }
}
