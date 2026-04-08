using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using ZapiCli.Commands;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

// ── AccountService / AccountCommand behaviour tests ───────────────────────────

public sealed class AccountCommandTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly AccountStore _store;
    private readonly FakeAuthProvider _auth = new();

    public AccountCommandTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"zapi-cli-test-{Guid.NewGuid():N}");
        _store = new AccountStore(_tmpDir, NullLogger<AccountStore>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private const string FakeTokenJson =
        "{\"access_token\":\"test-access-token\",\"refresh_token\":\"test-refresh-token\",\"token_type\":\"Bearer\",\"expires_in\":3600000}";

    private static IHttpClientFactory UserInfoFactory(string email, string zuid = "Z001")
    {
        var userInfoJson = $"{{\"Email\":\"{email}\",\"ZUID\":\"Z001\"}}";
        if (zuid != "Z001")
            userInfoJson = $"{{\"Email\":\"{email}\",\"ZUID\":\"{zuid}\"}}";
        return FakeHttpMessageHandler.ToFactory(req =>
            req.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(FakeTokenJson, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(userInfoJson, Encoding.UTF8, "application/json") });
    }

    private static IHttpClientFactory UserInfoAuthFailFactory(HttpStatusCode status)
        => FakeHttpMessageHandler.ToFactory(_ => new HttpResponseMessage(status));

    private static IHttpClientFactory UserInfoNoEmailFactory()
        => FakeHttpMessageHandler.ToFactory(req =>
            req.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(FakeTokenJson, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent("{\"ZUID\":\"Z001\"}", Encoding.UTF8, "application/json") });

    private AccountService CreateService(IHttpClientFactory? httpFactory = null)
        => new AccountService(
            _store,
            _auth,
            httpFactory ?? UserInfoFactory("user@example.com"),
            NullLogger<AccountService>.Instance,
            new FakeOAuthBrowserFlow());

    // ── account list ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AccountList_ReturnsProjectionWithoutTokenField()
    {
        await _store.SaveAsync(new AccountsRoot
        {
            Accounts = [
                new AccountEntry { Name = "work", Dc = "eu", Email = "e@example.com", IsDefault = true },
                new AccountEntry { Name = "dev",  Dc = "us", Email = "d@example.com" },
            ],
        });

        var service = CreateService();
        var list = await service.ListAccountsAsync();

        Assert.Equal(2, list.Count);
        Assert.All(list, item => Assert.IsType<AccountListView>(item));

        var work = list.First(i => i.Name == "work");
        Assert.True(work.IsDefault);
        Assert.Equal("eu", work.Dc);
        // AccountListView has no Token property — compile-time guarantee.
    }

    // ── account show ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AccountShow_NoTokenField_InOutput()
    {
        await _store.SaveAsync(new AccountsRoot
        {
            Accounts = [new AccountEntry { Name = "work", Dc = "us", Email = "e@example.com" }],
        });

        var service = CreateService();
        var view = await service.ShowAccountAsync("work");

        // The view must not expose any token/credential field (ADR-0008).
        var props = typeof(ZapiCli.Core.Accounts.AccountShowView)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();
        Assert.DoesNotContain("Token", props);
        Assert.Equal("work", view.Name);
    }

    [Fact]
    public async Task AccountShow_NotFound_ThrowsAccountNotFound()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.ShowAccountAsync("ghost"));

        Assert.Equal(ErrorCodes.ACCOUNT_NOT_FOUND, ex.Code);
    }

    // ── account set-default ───────────────────────────────────────────────────

    [Fact]
    public async Task AccountSetDefault_UpdatesIsDefaultCorrectly()
    {
        await _store.SaveAsync(new AccountsRoot
        {
            Accounts = [
                new AccountEntry { Name = "alice", Dc = "us", IsDefault = true },
                new AccountEntry { Name = "bob",   Dc = "eu" },
            ],
        });

        var service = CreateService();
        await service.SetDefaultAsync("bob");

        var alice = await _store.FindAsync("alice");
        var bob   = await _store.FindAsync("bob");

        Assert.NotNull(alice);
        Assert.NotNull(bob);
        Assert.False(alice.IsDefault);
        Assert.True(bob.IsDefault);
    }

    [Fact]
    public async Task AccountSetDefault_NotFound_ThrowsAccountNotFound()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.SetDefaultAsync("ghost"));

        Assert.Equal(ErrorCodes.ACCOUNT_NOT_FOUND, ex.Code);
    }

    // ── account remove ────────────────────────────────────────────────────────

    [Fact]
    public async Task AccountRemove_RemovesEntryAndClearsKeychain()
    {
        await _store.SaveAsync(new AccountsRoot
        {
            Accounts = [new AccountEntry { Name = "work", Dc = "us", IsDefault = true }],
        });
        // Seed keychain so revocation attempt can read a token.
        await _auth.StoreTokenAsync("work", "mytoken", "ref-tok", "cid", "csec");

        // Mock HTTP: accept any request (user-info + revoke).
        var factory = FakeHttpMessageHandler.ToFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"Email\":\"x@example.com\",\"ZUID\":\"Z1\"}",
                    Encoding.UTF8, "application/json"),
            });

        var service = CreateService(factory);
        await service.RemoveAccountAsync("work");

        var entry = await _store.FindAsync("work");
        Assert.Null(entry);
        Assert.Equal(1, _auth.ClearTokenCallCount);
    }

    [Fact]
    public async Task AccountRemove_DefaultRemoved_PromotesFirstRemaining()
    {
        await _store.SaveAsync(new AccountsRoot
        {
            Accounts = [
                new AccountEntry { Name = "alice", Dc = "us", IsDefault = true },
                new AccountEntry { Name = "bob",   Dc = "eu" },
            ],
        });
        await _auth.StoreTokenAsync("alice", "tok", "ref-tok", "cid", "csec");

        var service = CreateService(FakeHttpMessageHandler.ToFactory(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            }));

        await service.RemoveAccountAsync("alice");

        var bob = await _store.FindAsync("bob");
        Assert.NotNull(bob);
        Assert.True(bob.IsDefault);
    }

    [Fact]
    public async Task AccountRemove_NotFound_ThrowsAccountNotFound()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.RemoveAccountAsync("ghost"));

        Assert.Equal(ErrorCodes.ACCOUNT_NOT_FOUND, ex.Code);
    }

    // ── account refresh ───────────────────────────────────────────────────────

    [Fact]
    public async Task AccountReAuth_CallsRefreshToken()
    {
        await _store.SaveAsync(new AccountsRoot
        {
            Accounts = [
                new AccountEntry { Name = "work", Dc = "us" },
            ],
        });
        await _auth.StoreTokenAsync("work", "oldtok", "ref-tok", "cid", "csec");

        var service = CreateService();
        await service.ReAuthAsync("work");

        Assert.Equal(1, _auth.RefreshTokenCallCount);
    }

    [Fact]
    public async Task AccountReAuth_NotFound_ThrowsAccountNotFound()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.ReAuthAsync("ghost"));

        Assert.Equal(ErrorCodes.ACCOUNT_NOT_FOUND, ex.Code);
    }
}
