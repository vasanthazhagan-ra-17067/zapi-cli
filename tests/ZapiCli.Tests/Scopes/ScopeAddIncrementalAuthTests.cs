using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Auth;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests.Scopes;

/// <summary>
/// Tests for Story 15: scope add incremental authorization two-step browser flow.
/// </summary>
public sealed class ScopeAddIncrementalAuthTests : IDisposable
{
    private readonly string _tmpDir;

    public ScopeAddIncrementalAuthTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"zapi-scope15-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private static OAuthProvider BuildOAuthProvider(
        InMemoryKeychainProvider keychain,
        IHttpClientFactory httpFactory)
        => new(keychain, httpFactory, NullLogger<OAuthProvider>.Instance);

    private static InMemoryKeychainProvider BuildKeychainWithCreds(string accountName)
    {
        var keychain = new InMemoryKeychainProvider();
        // OAuthProvider uses CamelCase serialization for the keychain blob.
        var camelCase = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var credsJson = JsonSerializer.Serialize(new
        {
            accessToken = "at_initial",
            refreshToken = "rt_initial",
            clientId = "test-client-id",
            clientSecret = "test-secret",
        }, camelCase);
        keychain.SetAsync($"zapi-cli:{accountName}:oauth", credsJson).GetAwaiter().GetResult();
        return keychain;
    }

    private AccountService CreateAccountService(
        FakeAccountStore store,
        FakeAuthProvider auth,
        IHttpClientFactory? httpFactory = null)
        => new(
            store,
            auth,
            httpFactory ?? FakeHttpMessageHandler.ToFactory(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                }),
            NullLogger<AccountService>.Instance,
            new FakeOAuthBrowserFlow());

    // ─── Test 1: GetScopeEnhancementTokenAsync success ────────────────────────

    [Fact]
    public async Task GetScopeEnhancementTokenAsync_Success_ReturnsEnhanceTokenAndClientId()
    {
        var keychain = BuildKeychainWithCreds("work");
        var factory = FakeHttpMessageHandler.ToFactory(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("scopeenhance"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"access_token\":\"enhance-tok\"}",
                        Encoding.UTF8, "application/json"),
                };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var provider = BuildOAuthProvider(keychain, factory);

        var (enhanceToken, clientId) = await provider.GetScopeEnhancementTokenAsync("work", "us");

        Assert.Equal("enhance-tok", enhanceToken);
        Assert.Equal("test-client-id", clientId);
    }

    // ─── Test 2: GetScopeEnhancementTokenAsync HTTP failure ──────────────────

    [Fact]
    public async Task GetScopeEnhancementTokenAsync_HttpFailure_ThrowsScopeEnhanceFailed()
    {
        var keychain = BuildKeychainWithCreds("work");
        var factory = FakeHttpMessageHandler.ToFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest));

        var provider = BuildOAuthProvider(keychain, factory);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => provider.GetScopeEnhancementTokenAsync("work", "us"));

        Assert.Equal(ErrorCodes.SCOPE_ENHANCE_FAILED, ex.Code);
        Assert.Equal(2, ex.ExitCode);
    }

    // ─── Test 3: GetScopeEnhancementTokenAsync missing access_token ──────────

    [Fact]
    public async Task GetScopeEnhancementTokenAsync_MissingToken_ThrowsScopeEnhanceFailed()
    {
        var keychain = BuildKeychainWithCreds("work");
        var factory = FakeHttpMessageHandler.ToFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });

        var provider = BuildOAuthProvider(keychain, factory);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => provider.GetScopeEnhancementTokenAsync("work", "us"));

        Assert.Equal(ErrorCodes.SCOPE_ENHANCE_FAILED, ex.Code);
    }

    // ─── Test 4: WaitForScopeEnhancedCallbackAsync success ───────────────────

    [Fact]
    public async Task WaitForScopeEnhancedCallbackAsync_SuccessPreset_DoesNotThrow()
    {
        // Default FakeLocalCallbackServer (no _scopeEnhancedExceptionToThrow set) returns success.
        var fakeServer = new FakeLocalCallbackServer(code: "irrelevant", state: "irrelevant");

        var ex = await Record.ExceptionAsync(
            () => fakeServer.WaitForScopeEnhancedCallbackAsync(TimeSpan.FromSeconds(5)));

        Assert.Null(ex);
    }

    // ─── Test 5: WaitForScopeEnhancedCallbackAsync error callback ────────────

    [Fact]
    public async Task WaitForScopeEnhancedCallbackAsync_ErrorPreset_ThrowsScopeEnhanceDenied()
    {
        var fakeServer = new FakeLocalCallbackServer(code: "irrelevant", state: "irrelevant");
        fakeServer.SetScopeEnhancedError(
            new ZapiCliException("access_denied", ErrorCodes.SCOPE_ENHANCE_DENIED, exitCode: 1));

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => fakeServer.WaitForScopeEnhancedCallbackAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(ErrorCodes.SCOPE_ENHANCE_DENIED, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    // ─── Test 6: AddScopesAsync end-to-end integration ────────────────────────

    [Fact]
    public async Task AddScopesAsync_EndToEnd_SavesMergedScopesAndCallsRefresh()
    {
        var store = new FakeAccountStore();
        store.AddAccount(new AccountEntry
        {
            Name = "work",
            Dc = "us",
            Email = "dev@example.com",
            Scopes = ["ZohoCRM.Contacts.READ"],
            IsDefault = true,
        });

        var auth = new FakeAuthProvider();
        auth.SetToken("work", "initial-token");

        var service = CreateAccountService(store, auth);

        var fakeServer = new FakeLocalCallbackServer(code: "irrelevant", state: "irrelevant");

        var (accountName, updatedScopes) = await service.AddScopesAsync(
            "work",
            new[] { "ZohoCRM.Deals.READ" },
            callbackPort: 8085,
            serverFactory: _ => fakeServer);

        // (a) Accounts.json saved with merged scope list.
        var entry = await store.FindAsync("work");
        Assert.NotNull(entry);
        Assert.Contains("ZohoCRM.Contacts.READ", entry!.Scopes);
        Assert.Contains("ZohoCRM.Deals.READ", entry.Scopes);
        Assert.Equal(2, entry.Scopes.Count);

        // (b) RefreshTokenAsync called.
        Assert.Equal(1, auth.RefreshTokenCallCount);

        // (c) Return value is consistent.
        Assert.Equal("work", accountName);
        Assert.Contains("ZohoCRM.Deals.READ", updatedScopes);
    }

    // ─── Test 7: AddScopesAsync skips already-present scopes ─────────────────

    [Fact]
    public async Task AddScopesAsync_DuplicateScope_NotAddedTwice()
    {
        var store = new FakeAccountStore();
        store.AddAccount(new AccountEntry
        {
            Name = "work",
            Dc = "us",
            Email = "dev@example.com",
            Scopes = ["ZohoCRM.Contacts.READ"],
            IsDefault = true,
        });

        var auth = new FakeAuthProvider();

        var service = CreateAccountService(store, auth);
        var fakeServer = new FakeLocalCallbackServer(code: "irrelevant", state: "irrelevant");

        var (_, updatedScopes) = await service.AddScopesAsync(
            "work",
            new[] { "ZohoCRM.Contacts.READ" },
            callbackPort: 8085,
            serverFactory: _ => fakeServer);

        Assert.Single(updatedScopes);
        Assert.Equal("ZohoCRM.Contacts.READ", updatedScopes[0]);
    }
}
