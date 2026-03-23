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

    // ── account add ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AccountAdd_HappyPath_PersistsAccountAndReturnsNameDc()
    {
        var service = CreateService(UserInfoFactory("alice@example.com", "Z42"));
        var (name, dc) = await service.AddAccountAsync("work", "code", "https://www.zoho.com", "cid", "csec", "us", []);

        Assert.Equal("work", name);
        Assert.Equal("us", dc);

        var entry = await _store.FindAsync("work");
        Assert.NotNull(entry);
        Assert.Equal("alice@example.com", entry.Email);
        Assert.Equal("Z42", entry.Zuid);
        Assert.True(entry.IsDefault);  // first account → default
        Assert.Equal(1, _auth.StoreTokenCallCount);
    }

    [Fact]
    public async Task AccountAdd_DuplicateName_ThrowsAccountAlreadyExists()
    {
        await _store.SaveAsync(new AccountsRoot
        {
            Accounts = [new AccountEntry { Name = "work", Dc = "us" }],
        });

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.AddAccountAsync("work", "code", "https://www.zoho.com", "cid", "csec", "us", []));

        Assert.Equal(ErrorCodes.ACCOUNT_ALREADY_EXISTS, ex.Code);
        Assert.Equal(0, _auth.StoreTokenCallCount);  // no keychain write
    }

    [Fact]
    public async Task AccountAdd_ZohoCorpEmail_ThrowsDomainBlockedBeforeKeychainWrite()
    {
        var service = CreateService(UserInfoFactory("attacker@zohocorp.com"));
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.AddAccountAsync("corp", "code", "https://www.zoho.com", "cid", "csec", "us", []));

        Assert.Equal(ErrorCodes.ACCOUNT_DOMAIN_BLOCKED, ex.Code);
        Assert.Equal(1, ex.ExitCode);
        Assert.Equal(0, _auth.StoreTokenCallCount);  // keychain write never called
    }

    [Fact]
    public async Task AccountAdd_MissingEmailInUserInfo_ThrowsEmailRequired()
    {
        var service = CreateService(UserInfoNoEmailFactory());
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.AddAccountAsync("work", "code", "https://www.zoho.com", "cid", "csec", "us", []));

        Assert.Equal(ErrorCodes.EMAIL_REQUIRED, ex.Code);
        Assert.Equal(0, _auth.StoreTokenCallCount);  // keychain write never called
    }

    [Fact]
    public async Task AccountAdd_HttpAuthFailure_ThrowsAuthFailure()
    {
        var service = CreateService(UserInfoAuthFailFactory(HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.AddAccountAsync("work", "code", "https://www.zoho.com", "cid", "csec", "us", []));

        Assert.Equal(ErrorCodes.AUTH_FAILURE, ex.Code);
        Assert.Equal(2, ex.ExitCode);
        Assert.Equal(0, _auth.StoreTokenCallCount);
    }

    // ── AccountAddSettings.Validate() ─────────────────────────────────────────

    [Fact]
    public void AccountAddSettings_MissingToken_ValidationFails()
    {
        var settings = new AccountCommands.AccountAddSettings
        {
            Name = "work",
            // Code intentionally omitted (null)
            ClientId = "cid",
            ClientSecret = "csec",
        };
        var result = settings.Validate();
        Assert.False(result.Successful);
    }

    [Fact]
    public void AccountAddSettings_MissingName_ValidationFails()
    {
        var settings = new AccountCommands.AccountAddSettings
        {
            Code = "gc",
            ClientId = "cid",
            ClientSecret = "csec",
        };
        var result = settings.Validate();
        Assert.False(result.Successful);
    }

    [Fact]
    public void AccountAddSettings_InvalidDc_ValidationFails()
    {
        var settings = new AccountCommands.AccountAddSettings
        {
            Name = "work",
            Code = "gc",
            ClientId = "cid",
            ClientSecret = "csec",
            Dc = "xx",  // not a valid DC
        };
        var result = settings.Validate();
        Assert.False(result.Successful);
    }

    [Fact]
    public void AccountAddSettings_AllValid_ValidationSucceeds()
    {
        var settings = new AccountCommands.AccountAddSettings
        {
            Name = "work",
            Code = "gc",
            ClientId = "cid",
            ClientSecret = "csec",
            Scope = "ZohoCRM.Contacts.READ",
            Dc = "eu",
        };
        var result = settings.Validate();
        Assert.True(result.Successful);
    }

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
    public async Task AccountShow_TokenAlwaysMasked()
    {
        await _store.SaveAsync(new AccountsRoot
        {
            Accounts = [new AccountEntry { Name = "work", Dc = "us", Email = "e@example.com" }],
        });

        var service = CreateService();
        var view = await service.ShowAccountAsync("work");

        Assert.Equal("***", view.Token);
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

    // ── account re-auth ───────────────────────────────────────────────────────

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
