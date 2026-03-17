using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Api;
using ZapiCli.Core.Auth;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

/// <summary>
/// Unit tests for <see cref="ApiClient"/> covering Story 05 acceptance criteria.
/// </summary>
public sealed class ApiClientTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ApiClient BuildClient(
        FakeHttpMessageHandler? handler = null,
        IAuthProvider? auth = null,
        IAccountStore? store = null)
    {
        var httpClient = new HttpClient(handler ?? new FakeHttpMessageHandler());
        return new ApiClient(
            httpClient,
            auth ?? new PatAuthProvider(new InMemoryKeychainProvider()),
            store ?? new FakeAccountStore(),
            NullLogger<ApiClient>.Instance);
    }

    private static FakeAccountStore StoreWithAccount(AccountEntry entry) =>
        new(new AccountsRoot { Accounts = [entry] });

    private static (PatAuthProvider, InMemoryKeychainProvider) KeychainWithToken(
        string accountName, string token = "test-token")
    {
        var kc = new InMemoryKeychainProvider();
        kc.SetAsync($"zapi-cli:{accountName}:pat", token).Wait();
        return (new PatAuthProvider(kc), kc);
    }

    private static ApiRequest ValidRequest(
        string baseUrl = "https://cliq.zoho.com/api/v2",
        string path = "/channels",
        string accountName = "my-account") =>
        new()
        {
            BaseUrl = baseUrl,
            Method = "GET",
            Path = path,
            AccountName = accountName,
        };

    // ─── Host allowlist ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://evil.com/api")]
    [InlineData("https://notzoho.com/v1")]
    [InlineData("https://zoho.com.evil.com/v1")]
    [InlineData("https://malicious.zohoapis.com.evil.com/v1")]
    public async Task HostNotAllowed_ThrowsHostNotAllowed(string baseUrl)
    {
        const string account = "test";
        var (auth, _) = KeychainWithToken(account);
        var store = StoreWithAccount(new AccountEntry
        {
            Name = account,
            TokenType = "pat",
            Email = "user@zoho.com",
        });

        var client = BuildClient(auth: auth, store: store);
        var request = ValidRequest(baseUrl: baseUrl, accountName: account);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => client.CallAsync(request));
        Assert.Equal(ErrorCodes.HostNotAllowed, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Theory]
    [InlineData("https://cliq.zoho.com/api/v2")]
    [InlineData("https://desk.zoho.eu/api/v1")]
    [InlineData("https://crm.zohoapis.com/crm/v3")]
    [InlineData("https://api.zoho.in/v1")]
    [InlineData("https://mail.zoho.com.au/api/v2")]
    [InlineData("https://analytics.zohoapis.in/v2")]
    public async Task AllowedHosts_DoNotThrowHostNotAllowed(string baseUrl)
    {
        const string account = "test";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"id":"ok"}""");
        var (auth, _) = KeychainWithToken(account);
        var store = StoreWithAccount(new AccountEntry
        {
            Name = account,
            TokenType = "pat",
            Email = "user@zoho.com",
        });

        var client = BuildClient(handler, auth, store);
        var response = await client.CallAsync(ValidRequest(baseUrl, "/test", account));

        Assert.True(response.IsSuccess);
    }

    // ─── HOST_NOT_ALLOWED fires BEFORE GetTokenAsync ─────────────────────────

    [Fact]
    public async Task HostNotAllowed_FiresBeforeGetTokenAsync()
    {
        const string account = "test";
        var spyAuth = new SpyAuthProvider();
        var store = StoreWithAccount(new AccountEntry
        {
            Name = account,
            TokenType = "pat",
            Email = "user@zoho.com",
        });

        var client = BuildClient(auth: spyAuth, store: store);
        var request = new ApiRequest
        {
            BaseUrl = "https://evil.com/api",
            Method = "GET",
            Path = "/steal",
            AccountName = account,
        };

        await Assert.ThrowsAsync<ZapiCliException>(() => client.CallAsync(request));

        Assert.Equal(0, spyAuth.GetTokenCallCount);
    }

    // ─── ZohoCorp block ───────────────────────────────────────────────────────

    [Fact]
    public async Task ZohoCorpAccount_ThrowsAccountDomainBlocked()
    {
        const string account = "corp";
        var (auth, _) = KeychainWithToken(account);
        var store = StoreWithAccount(new AccountEntry
        {
            Name = account,
            TokenType = "pat",
            Email = "employee@zohocorp.com",
        });

        var client = BuildClient(auth: auth, store: store);
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => client.CallAsync(ValidRequest(accountName: account)));

        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
    }

    // ─── NeedsReauth ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NeedsReauth_ThrowsNeedsReauth()
    {
        const string account = "stale";
        var (auth, _) = KeychainWithToken(account);
        var store = StoreWithAccount(new AccountEntry
        {
            Name = account,
            TokenType = "pat",
            NeedsReauth = true,
        });

        var client = BuildClient(auth: auth, store: store);
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => client.CallAsync(ValidRequest(accountName: account)));

        Assert.Equal(ErrorCodes.NeedsReauth, ex.Code);
        Assert.Equal(2, ex.ExitCode);
    }

    // ─── Path normalisation ───────────────────────────────────────────────────

    [Fact]
    public async Task PathWithoutLeadingSlash_IsNormalized()
    {
        const string account = "test";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var (auth, _) = KeychainWithToken(account);
        var store = StoreWithAccount(new AccountEntry
        {
            Name = account,
            TokenType = "pat",
            Email = "user@zoho.com",
        });

        var client = BuildClient(handler, auth, store);
        await client.CallAsync(new ApiRequest
        {
            BaseUrl = "https://cliq.zoho.com/api/v2",
            Method = "GET",
            Path = "channels",   // <-- no leading slash
            AccountName = account,
        });

        Assert.NotNull(handler.LastRequest);
        Assert.Contains("/channels", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    // ─── Non-2xx → API_ERROR with detail ────────────────────────────────────

    [Fact]
    public async Task Non2xxResponse_ThrowsApiErrorWithDetail()
    {
        const string account = "test";
        const string errorBody = """{"message":"Unauthorized"}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized, errorBody);
        var (auth, _) = KeychainWithToken(account);
        var store = StoreWithAccount(new AccountEntry
        {
            Name = account,
            TokenType = "pat",
            Email = "user@zoho.com",
        });

        var client = BuildClient(handler, auth, store);
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => client.CallAsync(ValidRequest(accountName: account)));

        Assert.Equal(ErrorCodes.ApiError, ex.Code);
        Assert.Equal(errorBody, ex.Detail);
    }

    // ─── Authorization header injected by ApiClient ───────────────────────────

    [Fact]
    public async Task AuthorizationHeader_IsInjectedByApiClient()
    {
        const string account = "test";
        const string token = "my-secret-pat";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var (auth, _) = KeychainWithToken(account, token);
        var store = StoreWithAccount(new AccountEntry
        {
            Name = account,
            TokenType = "pat",
            Email = "user@zoho.com",
        });

        var client = BuildClient(handler, auth, store);
        await client.CallAsync(ValidRequest(accountName: account));

        Assert.NotNull(handler.LastRequest);
        var authHeader = handler.LastRequest!.Headers.Authorization;
        Assert.Equal("Zoho-oauthtoken", authHeader?.Scheme);
        Assert.Equal(token, authHeader?.Parameter);
    }

    // ─── Default account resolution ───────────────────────────────────────────

    [Fact]
    public async Task NullAccountName_ResolvesToDefaultAccount()
    {
        const string account = "default-acc";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var (auth, _) = KeychainWithToken(account);
        var store = StoreWithAccount(new AccountEntry
        {
            Name = account,
            TokenType = "pat",
            Email = "user@zoho.com",
            IsDefault = true,
        });

        var client = BuildClient(handler, auth, store);
        await client.CallAsync(new ApiRequest
        {
            BaseUrl = "https://cliq.zoho.com/api/v2",
            Method = "GET",
            Path = "/channels",
            AccountName = null,   // <-- no account specified
        });

        // If we reach here without exception, default account was resolved correctly.
        Assert.Equal(1, handler.CallCount);
    }

    // ─── Spy auth provider ────────────────────────────────────────────────────

    private sealed class SpyAuthProvider : IAuthProvider
    {
        public int GetTokenCallCount { get; private set; }

        public Task<string> GetTokenAsync(string accountName, CancellationToken ct = default)
        {
            GetTokenCallCount++;
            return Task.FromResult("spy-token");
        }

        public Task StoreTokenAsync(string accountName, string token, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ClearTokenAsync(string accountName, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
