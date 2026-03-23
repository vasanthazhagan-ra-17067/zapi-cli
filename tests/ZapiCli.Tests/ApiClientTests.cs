using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Api;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

/// <summary>
/// Unit tests for <see cref="ApiClient"/> — host allowlist (ADR-0004), ZohoCorp block (ADR-0003),
/// auto-refresh flow (ADR-0002), and response pass-through (ADR-0008).
/// </summary>
public sealed class ApiClientTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static AccountEntry MakeAccount(
        string name = "testacct",
        string email = "dev@zohopartner.com")
        => new()
        {
            Name = name,
            Dc = "us",
            Email = email,
            Scopes = ["ZohoCliq.Channels.READ"],
            IsDefault = true,
        };

    private static ApiClient MakeApiClient(
        FakeAccountStore store,
        FakeAuthProvider auth,
        IHttpClientFactory httpFactory,
        FakeTraceWriter? traceWriter = null)
        => new(auth, store, httpFactory, traceWriter ?? new FakeTraceWriter(), NullLogger<ApiClient>.Instance);

    private static ApiRequest MakeRequest(
        string url = "https://cliq.zoho.com/api/v2/channels",
        string method = "GET",
        string accountName = "testacct")
        => new() { Url = url, Method = method, AccountName = accountName };

    // ─── ValidateHost tests ───────────────────────────────────────────────────

    [Fact]
    public void ValidateHost_AcceptsValidZohoSubdomain()
    {
        // cliq.zoho.com ends with zoho.com — should not throw.
        var uri = new Uri("https://cliq.zoho.com/api/v2/channels");
        var ex = Record.Exception(() => ApiClient.ValidateHost(uri));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateHost_AcceptsZohoEuSubdomain()
    {
        var uri = new Uri("https://api.zoho.eu/v2/resource");
        var ex = Record.Exception(() => ApiClient.ValidateHost(uri));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateHost_AcceptsZohoApisSubdomain()
    {
        var uri = new Uri("https://desk.zohoapis.com/api/v1/tickets");
        var ex = Record.Exception(() => ApiClient.ValidateHost(uri));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateHost_RejectsEvilDotCom()
    {
        var uri = new Uri("https://evil.com/api");
        var ex = Assert.Throws<ZapiCliException>(() => ApiClient.ValidateHost(uri));
        Assert.Equal(ErrorCodes.HOST_NOT_ALLOWED, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public void ValidateHost_RejectsEvilZohoDotCom()
    {
        // 'evilzoho.com' does NOT end with '.zoho.com' — it ends with 'zoho.com' but
        // the boundary check ensures it can only match as '<sub>.zoho.com' or 'zoho.com'.
        var uri = new Uri("https://evilzoho.com/api");
        var ex = Assert.Throws<ZapiCliException>(() => ApiClient.ValidateHost(uri));
        Assert.Equal(ErrorCodes.HOST_NOT_ALLOWED, ex.Code);
    }

    [Fact]
    public void ValidateHost_RejectsZohoComInPath()
    {
        // The domain is evil.io — path happens to contain zoho.com, but host doesn't match.
        var uri = new Uri("https://evil.io/redirect?to=zoho.com");
        var ex = Assert.Throws<ZapiCliException>(() => ApiClient.ValidateHost(uri));
        Assert.Equal(ErrorCodes.HOST_NOT_ALLOWED, ex.Code);
    }

    // ─── ZohoCorp block tests ─────────────────────────────────────────────────

    [Fact]
    public async Task CallAsync_ZohoCorpAccount_ThrowsBeforeHttpDispatch()
    {
        var callCount = 0;
        var factory = FakeHttpMessageHandler.ToFactory(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var store = new FakeAccountStore();
        store.AddAccount(MakeAccount(email: "engineer@zohocorp.com"));

        var auth = new FakeAuthProvider();
        auth.SetToken("testacct", "tok_abc");

        var client = MakeApiClient(store, auth, factory);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            client.CallAsync(MakeRequest()));

        Assert.Equal(ErrorCodes.ACCOUNT_DOMAIN_BLOCKED, ex.Code);
        Assert.Equal(0, callCount); // No HTTP dispatch.
    }

    // ─── Host allowlist enforcement ───────────────────────────────────────────

    [Fact]
    public async Task CallAsync_EvilUrl_ThrowsHostNotAllowedBeforeHttpDispatch()
    {
        var callCount = 0;
        var factory = FakeHttpMessageHandler.ToFactory(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var store = new FakeAccountStore();
        store.AddAccount(MakeAccount());

        var auth = new FakeAuthProvider();
        auth.SetToken("testacct", "tok_abc");

        var client = MakeApiClient(store, auth, factory);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            client.CallAsync(MakeRequest(url: "https://evil.com/api")));

        Assert.Equal(ErrorCodes.HOST_NOT_ALLOWED, ex.Code);
        Assert.Equal(1, ex.ExitCode);
        Assert.Equal(0, callCount); // No HTTP dispatch.
    }

    // ─── 401 auto-refresh + single retry ─────────────────────────────────────

    [Fact]
    public async Task CallAsync_401Response_RefreshesAndRetriesOnce()
    {
        var callCount = 0;
        var factory = FakeHttpMessageHandler.ToFactory(_ =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK)
                  {
                      Content = new StringContent("{\"data\":\"ok\"}"),
                  };
        });

        var store = new FakeAccountStore();
        store.AddAccount(MakeAccount());

        var auth = new FakeAuthProvider();
        auth.SetToken("testacct", "tok_first");

        var client = MakeApiClient(store, auth, factory);

        var response = await client.CallAsync(MakeRequest());

        Assert.Equal(1, auth.RefreshTokenCallCount); // Refreshed once.
        Assert.Equal(2, callCount);                  // Two HTTP attempts.
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task CallAsync_401ThenAgain401_ThrowsAuthFailureExitCode2()
    {
        var factory = FakeHttpMessageHandler.ToFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var store = new FakeAccountStore();
        store.AddAccount(MakeAccount());

        var auth = new FakeAuthProvider();
        auth.SetToken("testacct", "tok_expired");

        var client = MakeApiClient(store, auth, factory);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            client.CallAsync(MakeRequest()));

        Assert.Equal(ErrorCodes.AUTH_FAILURE, ex.Code);
        Assert.Equal(2, ex.ExitCode);
        Assert.Equal(1, auth.RefreshTokenCallCount); // Tried to refresh once.
    }

    // ─── Non-2xx responses ────────────────────────────────────────────────────

    [Fact]
    public async Task CallAsync_NonSuccessResponse_ReturnsBodyWithStatusCode()
    {
        const string errorBody = "{\"error\":\"not found\"}";
        var factory = FakeHttpMessageHandler.ToFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(errorBody),
            });

        var store = new FakeAccountStore();
        store.AddAccount(MakeAccount());

        var auth = new FakeAuthProvider();
        auth.SetToken("testacct", "tok_abc");

        var client = MakeApiClient(store, auth, factory);

        var response = await client.CallAsync(MakeRequest());

        Assert.Equal(404, response.StatusCode);
        Assert.False(response.IsSuccess);
        Assert.Equal(errorBody, response.Body);
    }

    // ─── Authorization header injection ──────────────────────────────────────

    [Fact]
    public async Task CallAsync_AuthorizationHeader_InjectedByApiClientNotCommand()
    {
        HttpRequestMessage? capturedRequest = null;
        var factory = FakeHttpMessageHandler.ToFactory(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}"),
            };
        });

        var store = new FakeAccountStore();
        store.AddAccount(MakeAccount());

        var auth = new FakeAuthProvider();
        auth.SetToken("testacct", "my_oauth_token");

        var client = MakeApiClient(store, auth, factory);

        await client.CallAsync(MakeRequest());

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!.Headers.TryGetValues("Authorization", out var values));
        var authValue = values.Single();
        Assert.Equal("Zoho-oauthtoken my_oauth_token", authValue);
    }

    // ─── Account not found ────────────────────────────────────────────────────

    [Fact]
    public async Task CallAsync_AccountNotFound_ThrowsAccountNotFound()
    {
        var factory = FakeHttpMessageHandler.ToFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));

        var store = new FakeAccountStore(); // Empty — no accounts.
        var auth = new FakeAuthProvider();
        var client = MakeApiClient(store, auth, factory);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            client.CallAsync(MakeRequest(accountName: "missing")));

        Assert.Equal(ErrorCodes.ACCOUNT_NOT_FOUND, ex.Code);
    }
}
