using System.Net;
using System.Net.Http;
using System.Text;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Auth;
using ZapiCli.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZapiCli.Tests.Auth;

/// <summary>
/// Unit tests for AccountService.LoginAsync (browser OAuth flow).
/// Uses FakeLocalCallbackServer and FakeOAuthBrowserFlow to avoid actual network/browser activity.
/// </summary>
public sealed class LoginAsyncTests
{
    private static AccountService CreateService(
        FakeAccountStore accountStore,
        FakeAuthProvider authProvider,
        IHttpClientFactory httpClientFactory,
        FakeOAuthBrowserFlow browserFlow)
    {
        return new AccountService(
            accountStore,
            authProvider,
            httpClientFactory,
            NullLogger<AccountService>.Instance,
            browserFlow);
    }

    private static IHttpClientFactory BuildSuccessHttpFactory(
        string accessToken = "at_test",
        string refreshToken = "rt_test",
        string email = "test@example.com",
        string zuid = "12345")
    {
        return FakeHttpMessageHandler.ToFactory(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("token"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"access_token\":\"{accessToken}\",\"refresh_token\":\"{refreshToken}\"}}",
                        Encoding.UTF8, "application/json"),
                };
            if (req.RequestUri.AbsolutePath.Contains("user/info"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"Email\":\"{email}\",\"ZUID\":\"{zuid}\"}}",
                        Encoding.UTF8, "application/json"),
                };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task LoginAsync_CallsGenerateStateAndOpenBrowser()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("test-state");
        var factory = BuildSuccessHttpFactory();

        var service = CreateService(store, auth, factory, browser);
        var fakeServer = new FakeLocalCallbackServer("grant-code", "test-state");

        await service.LoginAsync("myacc", "cid", "csecret", ["ZohoAPI.READ"], "us",
            8085, () => fakeServer);

        Assert.Equal(1, browser.GenerateStateCalls);
        Assert.Single(browser.OpenBrowserCalls);
    }

    [Fact]
    public async Task LoginAsync_PassesGeneratedStateToBuildsAuthorizationUrl()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("csrf-state");
        var factory = BuildSuccessHttpFactory();

        var service = CreateService(store, auth, factory, browser);
        var fakeServer = new FakeLocalCallbackServer("grant-code", "csrf-state");

        await service.LoginAsync("myacc", "cid", "csecret", ["ZohoAPI.READ"], "us",
            8085, () => fakeServer);

        var buildCall = Assert.Single(browser.BuildUrlCalls);
        Assert.Equal("csrf-state", buildCall.State);
    }

    [Fact]
    public async Task LoginAsync_ThrowsStateMismatch_WhenReturnedStateDiffers()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("expected-state");
        var factory = BuildSuccessHttpFactory();

        var service = CreateService(store, auth, factory, browser);
        // Server returns wrong state — simulates CSRF attack
        var fakeServer = new FakeLocalCallbackServer("grant-code", "wrong-state");

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            service.LoginAsync("myacc", "cid", "csecret", ["ZohoAPI.READ"], "us",
                8085, () => fakeServer));

        Assert.Equal(ErrorCodes.STATE_MISMATCH, ex.Code);
    }

    [Fact]
    public async Task LoginAsync_CompletesTokenExchangeAndPersistsAccount_WhenStateMatches()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("state-ok");
        var factory = BuildSuccessHttpFactory(accessToken: "at_ok", email: "user@example.com");

        var service = CreateService(store, auth, factory, browser);
        var fakeServer = new FakeLocalCallbackServer("auth-code-123", "state-ok");

        var (name, dc) = await service.LoginAsync("myacc", "cid", "csecret", ["ZohoAPI.READ"], "us",
            8085, () => fakeServer);

        Assert.Equal("myacc", name);
        Assert.Equal("us", dc);
        // Keychain was written
        Assert.Equal(1, auth.StoreTokenCallCount);
        // Account was persisted
        Assert.Equal(1, store.SaveCallCount);
        var saved = await store.FindAsync("myacc");
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task LoginAsync_ThrowsAccountAlreadyExists_WhenDuplicateName()
    {
        var store = new FakeAccountStore();
        store.AddAccount(new ZapiCli.Core.Accounts.AccountEntry
        {
            Name = "existing",
            Dc = "us",
            Email = "user@example.com",
            Zuid = "1",
            Scopes = [],
            IsDefault = true,
            NeedsReauth = false,
        });

        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow();
        var factory = BuildSuccessHttpFactory();

        var service = CreateService(store, auth, factory, browser);
        var fakeServer = new FakeLocalCallbackServer("code", "state");

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            service.LoginAsync("existing", "cid", "csecret", ["ZohoAPI.READ"], "us",
                8085, () => fakeServer));

        Assert.Equal(ErrorCodes.ACCOUNT_ALREADY_EXISTS, ex.Code);
        // No browser was opened since check is done first
        Assert.Empty(browser.OpenBrowserCalls);
    }

    [Fact]
    public async Task LoginAsync_ThrowsLoginTimeout_WhenFakeServerSignalsTimeout()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("state");
        var factory = BuildSuccessHttpFactory();

        var service = CreateService(store, auth, factory, browser);
        var timeoutException = new ZapiCliException(
            "Browser authentication timed out. Run the command again.",
            ErrorCodes.LOGIN_TIMEOUT, 1);
        var fakeServer = new FakeLocalCallbackServer(timeoutException);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            service.LoginAsync("myacc", "cid", "csecret", ["ZohoAPI.READ"], "us",
                8085, () => fakeServer));

        Assert.Equal(ErrorCodes.LOGIN_TIMEOUT, ex.Code);
    }
}
